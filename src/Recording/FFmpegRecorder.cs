using System.Buffers;
using System.Diagnostics;
using System.Text;
using GifMaker.Core;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;


namespace GifMaker.Recording;

/// <summary>
/// Records screen region to video using FFmpeg with x11grab.
/// Thread-safe state transitions, zero-allocation hot paths, testable via process abstraction.
/// </summary>
public sealed class FFmpegRecorder : IAsyncDisposable, IDisposable
{
    #region State Machine

    /// <summary>
    /// Discriminated union for recorder state. Makes illegal state transitions unrepresentable.
    /// </summary>
    private abstract record RecorderState
    {
        private RecorderState() { }

        /// <summary>Ready to start recording.</summary>
        public sealed record Idle : RecorderState
        {
            public static readonly Idle Instance = new();
            private Idle() { }
        }

        /// <summary>FFmpeg process is running.</summary>
        public sealed record Recording(
            IInteractiveProcess Process,
            Task<string> StderrTask,
            long StartTimestamp) : RecorderState;

        /// <summary>Recording stopped, disposed.</summary>
        public sealed record Stopped : RecorderState
        {
            public static readonly Stopped Instance = new();
            private Stopped() { }
        }
    }

    #endregion

    #region Constants

    /// <summary>Timeout waiting for FFmpeg graceful shutdown.</summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>FFmpeg exit code when stopped gracefully with 'q' command.</summary>
    private const int FFmpegQuitExitCode = 255;

    /// <summary>Min supported framerate.</summary>
    private const int MinFps = 1;

    /// <summary>Max supported framerate.</summary>
    private const int MaxFps = 240;

    /// <summary>Min region dimension (libx264 requirement).</summary>
    private const int MinDimension = 2;

    /// <summary>Buffer size for stderr reading.</summary>
    private const int StderrBufferSize = 4096;

    #endregion

    private readonly ILogger<FFmpegRecorder> _logger;
    private readonly IProcessLauncher _processLauncher;
    private readonly Lock _stateLock = new();
    private RecorderState _state = RecorderState.Idle.Instance;
    private int _disposed;

    /// <summary>Path to the temporary recording file.</summary>
    public string TempPath { get; }

    /// <summary>Timeout for stop operation.</summary>
    public TimeSpan StopTimeout { get; init; } = DefaultStopTimeout;

    /// <summary>
    /// Creates a new FFmpegRecorder with optional dependencies for testing.
    /// </summary>
    public FFmpegRecorder(
        ILogger<FFmpegRecorder>? logger = null,
        IProcessLauncher? processLauncher = null,
        string? tempPath = null)
    {
        _logger = logger ?? NullLogger<FFmpegRecorder>.Instance;
        _processLauncher = processLauncher ?? ProcessRunner.Default;
        TempPath = tempPath ?? GenerateTempPath();
    }

    /// <summary>
    /// Generates unique temp path without allocating intermediate strings.
    /// </summary>
    private static string GenerateTempPath()
    {
        var tempDir = Path.GetTempPath();
        Span<char> guidChars = stackalloc char[32];
        Guid.NewGuid().TryFormat(guidChars, out _, "N");
        return string.Create(
            tempDir.Length + 9 + 32 + 4, // "gifmaker_" + guid + ".mkv"
            (tempDir, guidChars.ToString()),
            static (span, state) =>
            {
                state.tempDir.CopyTo(span);
                var pos = state.tempDir.Length;
                "gifmaker_".CopyTo(span[pos..]);
                pos += 9;
                state.Item2.CopyTo(span[pos..]);
                pos += 32;
                ".mkv".CopyTo(span[pos..]);
            });
    }

    /// <summary>
    /// Validated, immutable recording settings.
    /// </summary>
    public readonly struct RecordingSettings : IEquatable<RecordingSettings>
    {
        /// <summary>Screen region to capture.</summary>
        public ScreenRegion Region { get; }

        /// <summary>Frames per second (1-240).</summary>
        public int Fps { get; }

        /// <summary>X11 display identifier.</summary>
        public string Display { get; }

        /// <summary>Capture width (even, derived from region).</summary>
        public int CaptureWidth { get; }

        /// <summary>Capture height (even, derived from region).</summary>
        public int CaptureHeight { get; }

        private RecordingSettings(ScreenRegion region, int fps, string display)
        {
            Region = region;
            Fps = fps;
            Display = display;
            // Ensure even dimensions (libx264 requirement)
            CaptureWidth = region.Width - (region.Width & 1);
            CaptureHeight = region.Height - (region.Height & 1);
        }

        /// <summary>
        /// Creates validated settings, returning error if invalid.
        /// </summary>
        public static Result<RecordingSettings> Create(
            ScreenRegion region,
            int fps = 30,
            string? display = null)
        {
            if (region.Width < MinDimension || region.Height < MinDimension)
                return Result<RecordingSettings>.Fail($"Region too small (min {MinDimension}x{MinDimension})");

            if (fps is < MinFps or > MaxFps)
                return Result<RecordingSettings>.Fail($"FPS must be between {MinFps} and {MaxFps}");

            var resolvedDisplay = X11Display.Resolve(display);

            return Result<RecordingSettings>.Ok(new RecordingSettings(region, fps, resolvedDisplay));
        }

        public bool Equals(RecordingSettings other) =>
            Region == other.Region && Fps == other.Fps && Display == other.Display;

        public override bool Equals(object? obj) =>
            obj is RecordingSettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Region, Fps, Display);

        public static bool operator ==(RecordingSettings left, RecordingSettings right) => left.Equals(right);
        public static bool operator !=(RecordingSettings left, RecordingSettings right) => !left.Equals(right);
    }

    /// <summary>
    /// Starts recording the specified screen region.
    /// </summary>
    /// <param name="settings">Validated recording settings.</param>
    /// <exception cref="InvalidOperationException">Recording already in progress or stopped.</exception>
    public void Start(RecordingSettings settings)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_state is not RecorderState.Idle)
                throw new InvalidOperationException("Recording already in progress or stopped");

            _logger.LogDebug(
                "Starting recording: {Width}x{Height} at {Fps}fps on {Display}",
                settings.CaptureWidth, settings.CaptureHeight, settings.Fps, settings.Display);

            var process = _processLauncher.StartInteractive(BuildCommand(settings, TempPath));

            var stderrTask = ReadStderrAsync(process);
            var startTimestamp = Stopwatch.GetTimestamp();

            _state = new RecorderState.Recording(process, stderrTask, startTimestamp);
            _logger.LogInformation("Recording started: {TempPath}", TempPath);
        }
    }

    private static ProcessCommand BuildCommand(RecordingSettings settings, string outputPath)
    {
        return new ProcessCommand(
            "ffmpeg",
            [
                "-y",
                "-f",
                "x11grab",
                "-framerate",
                settings.Fps.ToString(),
                "-video_size",
                FormatVideoSize(settings.CaptureWidth, settings.CaptureHeight),
                "-i",
                FormatInput(settings.Display, settings.Region.X, settings.Region.Y),
                "-c:v",
                "libx264",
                "-preset",
                "ultrafast",
                "-crf",
                "18",
                "-pix_fmt",
                "yuv420p",
                outputPath
            ],
            ProcessIo.Interactive);
    }



    /// <summary>
    /// Formats video size string with minimal allocation.
    /// </summary>
    private static string FormatVideoSize(int width, int height)
    {
        // "{width}x{height}" - max ~10 chars for typical resolutions
        Span<char> wChars = stackalloc char[5];
        Span<char> hChars = stackalloc char[5];
        width.TryFormat(wChars, out var wLen);
        height.TryFormat(hChars, out var hLen);

        return string.Create(wLen + 1 + hLen, (width, height), static (span, state) =>
        {
            state.width.TryFormat(span, out var wl);
            span[wl] = 'x';
            state.height.TryFormat(span[(wl + 1)..], out _);
        });
    }

    /// <summary>
    /// Formats input string for x11grab.
    /// </summary>
    private static string FormatInput(string display, int x, int y)
    {
        // "{display}+{x},{y}"
        Span<char> xChars = stackalloc char[6]; // signed int
        Span<char> yChars = stackalloc char[6];
        x.TryFormat(xChars, out var xLen);
        y.TryFormat(yChars, out var yLen);

        return string.Create(display.Length + 1 + xLen + 1 + yLen, (display, x, y), static (span, state) =>
        {
            state.display.CopyTo(span);
            var pos = state.display.Length;
            span[pos++] = '+';
            state.x.TryFormat(span[pos..], out var xl);
            pos += xl;
            span[pos++] = ',';
            state.y.TryFormat(span[pos..], out _);
        });
    }

    /// <summary>
    /// Reads stderr asynchronously, returning full output.
    /// Uses pooled buffer for zero steady-state allocation.
    /// </summary>
    private static async Task<string> ReadStderrAsync(IInteractiveProcess process)
    {
        var builder = Pools.StringBuilder.Get();
        var buffer = ArrayPool<char>.Shared.Rent(StderrBufferSize);

        try
        {
            var reader = process.StandardError;
            while (true)
            {
                var charsRead = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                if (charsRead == 0)
                    break;
                builder.Append(buffer.AsSpan(0, charsRead));
            }
            return builder.ToString();
        }
        catch (IOException)
        {
            // Process exited during read
            return builder.ToString();
        }
        catch (ObjectDisposedException)
        {
            // Process disposed during read
            return builder.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
            Pools.StringBuilder.Return(builder);
        }
    }

    /// <summary>
    /// Recording statistics returned after stopping.
    /// </summary>
    public readonly record struct RecordingResult(TimeSpan Duration, long FileSizeBytes);

    /// <summary>
    /// Stops recording and waits for FFmpeg to finish writing.
    /// </summary>
    /// <returns>Recording statistics.</returns>
    /// <exception cref="InvalidOperationException">No recording in progress.</exception>
    /// <exception cref="RecordingException">FFmpeg failed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public async Task<RecordingResult> StopAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        RecorderState.Recording recording;
        lock (_stateLock)
        {
            if (_state is not RecorderState.Recording r)
                throw new InvalidOperationException("No recording in progress");
            recording = r;
        }

        var (process, stderrTask, startTimestamp) = recording;

        try
        {
            // Check if process already exited unexpectedly
            if (process.HasExited)
            {
                var error = await GetStderrSafeAsync(stderrTask).ConfigureAwait(false);
                _logger.LogError("FFmpeg exited unexpectedly with code {ExitCode}", process.ExitCode);
                throw new RecordingException("FFmpeg exited unexpectedly", process.ExitCode, error);
            }

            // Send quit signal
            await SendQuitSignalAsync(process, ct).ConfigureAwait(false);

            // Wait for exit with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(StopTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout, not caller cancellation
                await process.KillSafelyAsync().ConfigureAwait(false);
                var error = await GetStderrSafeAsync(stderrTask).ConfigureAwait(false);
                _logger.LogError("FFmpeg failed to stop within timeout");
                throw new RecordingException("FFmpeg failed to stop in time", -1, error);
            }

            // Validate exit
            if (process.ExitCode is not (0 or FFmpegQuitExitCode))
            {
                var error = await GetStderrSafeAsync(stderrTask).ConfigureAwait(false);
                _logger.LogError("FFmpeg recording failed with exit code {ExitCode}", process.ExitCode);
                throw new RecordingException("FFmpeg recording failed", process.ExitCode, error);
            }

            // Verify output
            if (!File.Exists(TempPath))
            {
                var error = await GetStderrSafeAsync(stderrTask).ConfigureAwait(false);
                _logger.LogError("FFmpeg did not produce output file");
                throw new RecordingException("FFmpeg did not produce output", 0, error);
            }

            var duration = Stopwatch.GetElapsedTime(startTimestamp);
            var fileSize = new FileInfo(TempPath).Length;

            lock (_stateLock)
            {
                _state = RecorderState.Stopped.Instance;
            }

            _logger.LogInformation(
                "Recording stopped: {Duration:F1}s, {Size:F2}MB",
                duration.TotalSeconds,
                fileSize / (1024.0 * 1024.0));

            return new RecordingResult(duration, fileSize);
        }
        catch
        {
            // Transition to stopped on any error
            lock (_stateLock)
            {
                _state = RecorderState.Stopped.Instance;
            }
            throw;
        }
    }

    private async Task SendQuitSignalAsync(IInteractiveProcess process, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteAsync("q".AsMemory(), ct).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Sent quit signal to FFmpeg");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to send quit signal, killing process");
            await process.KillSafelyAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> GetStderrSafeAsync(Task<string> stderrTask)
    {
        try
        {
            // Give stderr task a bit of time to complete
            return await stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch
        {
            return "Failed to read stderr";
        }
    }



    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    /// <summary>
    /// Async disposal - preferred for cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RecorderState.Recording? recording;
        lock (_stateLock)
        {
            recording = _state as RecorderState.Recording;
            _state = RecorderState.Stopped.Instance;
        }

        if (recording is not null)
        {
            await recording.Process.KillSafelyAsync().ConfigureAwait(false);
            recording.Process.Dispose();
            _logger.LogDebug("Disposed FFmpeg process");
        }
    }

    /// <summary>
    /// Sync disposal - kills process without waiting.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RecorderState.Recording? recording;
        lock (_stateLock)
        {
            recording = _state as RecorderState.Recording;
            _state = RecorderState.Stopped.Instance;
        }

        if (recording is not null)
        {
            try
            {
                if (!recording.Process.HasExited)
                    recording.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited
            }
            recording.Process.Dispose();
            _logger.LogDebug("Disposed FFmpeg process");
        }
    }
}
