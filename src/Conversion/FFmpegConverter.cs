using System.Buffers;
using System.Diagnostics;
using System.Text;
using GifMaker.Core;
using Microsoft.Extensions.ObjectPool;

namespace GifMaker.Conversion;

/// <summary>
/// Converts recorded video to various output formats using FFmpeg.
/// Thread-safe, cancellable, with proper resource cleanup.
/// </summary>
public sealed class FFmpegConverter(IFFmpegProcessFactory? processFactory = null)
{
    /// <summary>Default timeout for FFmpeg operations.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private const int MinWidth = 16;
    private const int MaxWidth = 7680; // 8K
    private const int MinFps = 1;
    private const int MaxFps = 120;
    private const int CharBufferSize = 4096;

    private readonly IFFmpegProcessFactory _processFactory = processFactory ?? FFmpegProcessFactory.Default;

    /// <summary>Timeout for FFmpeg operations.</summary>
    public TimeSpan Timeout
    {
        get => field;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultTimeout;

    /// <summary>
    /// Validated, immutable conversion settings. Cannot be constructed with invalid values.
    /// </summary>
    public readonly struct ConversionSettings : IEquatable<ConversionSettings>
    {
        /// <summary>Target output format.</summary>
        public OutputFormat Format { get; }

        /// <summary>Frames per second (1-120).</summary>
        public int Fps { get; }

        /// <summary>Output width in pixels (0 for original, or 16-7680).</summary>
        public int Width { get; }

        private ConversionSettings(OutputFormat format, int fps, int width)
        {
            Format = format;
            Fps = fps;
            Width = width;
        }

        /// <summary>
        /// Creates validated settings, returning error if invalid.
        /// </summary>
        public static Result<ConversionSettings> Create(
            OutputFormat format,
            int fps = 30,
            int width = 0)
        {
            if (fps is < MinFps or > MaxFps)
                return Result<ConversionSettings>.Fail($"FPS must be between {MinFps} and {MaxFps}");

            if (width != 0 && width is < MinWidth or > MaxWidth)
                return Result<ConversionSettings>.Fail($"Width must be 0 (original) or between {MinWidth} and {MaxWidth}");

            if (!Enum.IsDefined(format))
                return Result<ConversionSettings>.Fail($"Unknown format: {format}");

            return Result<ConversionSettings>.Ok(new ConversionSettings(format, fps, width));
        }

        /// <summary>
        /// Creates settings, throwing if invalid.
        /// </summary>
        /// <exception cref="ArgumentException">Settings are invalid.</exception>
        public static ConversionSettings CreateOrThrow(
            OutputFormat format,
            int fps = 30,
            int width = 0)
        {
            var result = Create(format, fps, width);
            return result.Match(
                settings => settings,
                error => throw new ArgumentException(error, nameof(ConversionSettings)));
        }

        public bool Equals(ConversionSettings other) =>
            Format == other.Format && Fps == other.Fps && Width == other.Width;

        public override bool Equals(object? obj) => obj is ConversionSettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Format, Fps, Width);

        public static bool operator ==(ConversionSettings left, ConversionSettings right) => left.Equals(right);
        public static bool operator !=(ConversionSettings left, ConversionSettings right) => !left.Equals(right);
    }

    /// <summary>Progress information during conversion.</summary>
    public readonly record struct ConversionProgress(string Message, TimeSpan ElapsedTime);

    /// <summary>
    /// Converts source video to specified format.
    /// </summary>
    /// <param name="sourcePath">Path to source video file.</param>
    /// <param name="destPath">Output path for converted file.</param>
    /// <param name="settings">Conversion settings.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException">Invalid arguments.</exception>
    /// <exception cref="FileNotFoundException">Source file not found.</exception>
    /// <exception cref="DirectoryNotFoundException">Output directory not found.</exception>
    /// <exception cref="FFmpegException">FFmpeg conversion failed.</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled.</exception>
    /// <exception cref="TimeoutException">Conversion exceeded timeout.</exception>
    public async Task ConvertAsync(
        string sourcePath,
        string destPath,
        ConversionSettings settings,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken ct = default)
    {
        ValidateInputs(sourcePath, destPath);

        var args = new ArgumentList();
        BuildArgumentList(ref args, sourcePath, destPath, settings);
        using var process = _processFactory.Create(args.AsSpan());
        var stderr = await RunFFmpegAsync(process, progress, ct).ConfigureAwait(false);

        if (!File.Exists(destPath))
        {
            throw new FFmpegException(
                "FFmpeg completed but output file was not created",
                exitCode: 0,
                stderr);
        }
    }

    private static void ValidateInputs(string sourcePath, string destPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source video not found", sourcePath);

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            throw new DirectoryNotFoundException($"Output directory not found: {destDir}");
    }

    /// <summary>
    /// Builds FFmpeg argument list. Arguments stored in stack-allocated inline array.
    /// </summary>
    private static void BuildArgumentList(
        ref ArgumentList args,
        string source,
        string dest,
        ConversionSettings settings)
    {
        switch (settings.Format)
        {
            case OutputFormat.Gif:
                BuildGifArgs(ref args, source, dest, settings.Fps, settings.Width);
                break;
            case OutputFormat.Mp4:
                BuildMp4Args(ref args, source, dest, settings.Fps, settings.Width);
                break;
            case OutputFormat.WebM:
                BuildWebMArgs(ref args, source, dest, settings.Fps, settings.Width);
                break;
            default:
                throw new UnreachableException($"Unhandled format: {settings.Format}");
        }
    }

    private static void BuildGifArgs(
        ref ArgumentList args,
        string source,
        string dest,
        int fps,
        int width)
    {
        args.Add("-y");
        args.Add("-i");
        args.Add(source);
        args.Add("-filter_complex");
        args.Add(BuildGifFilterComplex(fps, width));
        args.Add(dest);
    }

    /// <summary>
    /// Builds GIF filter complex string. Computes exact length upfront to avoid trimming.
    /// </summary>
    private static string BuildGifFilterComplex(int fps, int width)
    {
        const string paletteSuffix = ",split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=floyd_steinberg";

        // Calculate exact length needed
        Span<char> fpsChars = stackalloc char[4]; // max "120"
        fps.TryFormat(fpsChars, out var fpsLen);
        var fpsSpan = fpsChars[..fpsLen];

        int totalLength;
        int widthLen = 0;

        if (width > 0)
        {
            // "scale={width}:-2:flags=lanczos,fps={fps}" + suffix
            Span<char> widthChars = stackalloc char[5]; // max "7680"
            width.TryFormat(widthChars, out widthLen);
            totalLength = 6 + widthLen + 21 + fpsLen + paletteSuffix.Length;
            // scale= + width + :-2:flags=lanczos,fps= + fps + suffix

            return string.Create(totalLength, (width, fps, paletteSuffix), static (span, state) =>
            {
                var pos = 0;
                "scale=".CopyTo(span);
                pos = 6;
                state.width.TryFormat(span[pos..], out var wLen);
                pos += wLen;
                ":-2:flags=lanczos,fps=".CopyTo(span[pos..]);
                pos += 21;
                state.fps.TryFormat(span[pos..], out var fLen);
                pos += fLen;
                state.paletteSuffix.CopyTo(span[pos..]);
            });
        }

        // "fps={fps}" + suffix
        totalLength = 4 + fpsLen + paletteSuffix.Length;

        return string.Create(totalLength, (fps, paletteSuffix), static (span, state) =>
        {
            "fps=".CopyTo(span);
            state.fps.TryFormat(span[4..], out var fLen);
            state.paletteSuffix.CopyTo(span[(4 + fLen)..]);
        });
    }

    private static void BuildMp4Args(
        ref ArgumentList args,
        string source,
        string dest,
        int fps,
        int width)
    {
        args.Add("-y");
        args.Add("-i");
        args.Add(source);

        if (width > 0)
        {
            args.Add("-vf");
            args.Add(BuildScaleFilter(width));
        }

        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add("medium");
        args.Add("-crf");
        args.Add("23");
        args.Add("-r");
        args.Add(FormatFps(fps));
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add(dest);
    }

    private static void BuildWebMArgs(
        ref ArgumentList args,
        string source,
        string dest,
        int fps,
        int width)
    {
        args.Add("-y");
        args.Add("-i");
        args.Add(source);

        if (width > 0)
        {
            args.Add("-vf");
            args.Add(BuildScaleFilter(width));
        }

        args.Add("-c:v");
        args.Add("libvpx-vp9");
        args.Add("-crf");
        args.Add("30");
        args.Add("-b:v");
        args.Add("0");
        args.Add("-r");
        args.Add(FormatFps(fps));
        args.Add("-pix_fmt");
        args.Add("yuv420p");
        args.Add(dest);
    }

    /// <summary>
    /// Formats FPS as string. Simple int.ToString() - JIT optimizes small int formatting well.
    /// </summary>
    private static string FormatFps(int fps) => fps.ToString();

    /// <summary>
    /// Builds scale filter string.
    /// </summary>
    private static string BuildScaleFilter(int width)
    {
        // "scale={width}:-2:flags=lanczos" - max 30 chars
        const string prefix = "scale=";
        const string suffix = ":-2:flags=lanczos";

        Span<char> widthChars = stackalloc char[5];
        width.TryFormat(widthChars, out var widthLen);

        return string.Create(prefix.Length + widthLen + suffix.Length, width, static (span, w) =>
        {
            "scale=".CopyTo(span);
            w.TryFormat(span[6..], out var len);
            ":-2:flags=lanczos".CopyTo(span[(6 + len)..]);
        });
    }

    private async Task<string> RunFFmpegAsync(
        IFFmpegProcess process,
        IProgress<ConversionProgress>? progress,
        CancellationToken ct)
    {
        var stderrBuilder = StringBuilderPool.Shared.Get();
        var startTime = Stopwatch.GetTimestamp();

        // Single CTS with timeout, linked to user token
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        var combinedToken = timeoutCts.Token;

        try
        {
            if (!process.Start())
            {
                throw new FFmpegException("Failed to start FFmpeg process", exitCode: -1, stderr: "");
            }

            var stderrTask = ReadStderrAsync(process, stderrBuilder, progress, startTime, combinedToken);

            try
            {
                await process.WaitForExitAsync(combinedToken).ConfigureAwait(false);
                await stderrTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Stderr reading took too long after process exit - acceptable
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await KillProcessSafelyAsync(process).ConfigureAwait(false);
                throw new TimeoutException(
                    $"FFmpeg conversion timed out after {Timeout.TotalMinutes:F1} minutes. Stderr: {stderrBuilder}");
            }
            catch (OperationCanceledException)
            {
                await KillProcessSafelyAsync(process).ConfigureAwait(false);
                throw;
            }

            var stderr = stderrBuilder.ToString();

            if (process.ExitCode != 0)
            {
                throw new FFmpegException("FFmpeg conversion failed", process.ExitCode, stderr);
            }

            return stderr;
        }
        finally
        {
            StringBuilderPool.Shared.Return(stderrBuilder);
        }
    }

    private static async Task ReadStderrAsync(
        IFFmpegProcess process,
        StringBuilder buffer,
        IProgress<ConversionProgress>? progress,
        long startTimestamp,
        CancellationToken ct)
    {
        var reader = process.StandardError;
        var charBuffer = ArrayPool<char>.Shared.Rent(CharBufferSize);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var charsRead = await reader.ReadAsync(charBuffer.AsMemory(), ct).ConfigureAwait(false);
                if (charsRead == 0)
                    break;

                buffer.Append(charBuffer.AsSpan(0, charsRead));

                if (progress is not null)
                {
                    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                    var lastLine = ExtractLastLine(charBuffer.AsSpan(0, charsRead));
                    progress.Report(new ConversionProgress(lastLine, elapsed));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (IOException)
        {
            // Process may have exited
        }
        finally
        {
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    /// <summary>
    /// SearchValues for vectorized newline search.
    /// </summary>
    private static readonly SearchValues<char> NewlineChars = SearchValues.Create(['\r', '\n']);

    /// <summary>
    /// Extracts last line from text span.
    /// </summary>
    private static string ExtractLastLine(ReadOnlySpan<char> text)
    {
        var span = text.TrimEnd();
        var lastNewline = span.LastIndexOfAny(NewlineChars);
        return lastNewline >= 0 ? span[(lastNewline + 1)..].ToString() : span.ToString();
    }

    private static async Task KillProcessSafelyAsync(IFFmpegProcess process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (TimeoutException)
        {
            // Process didn't exit in time after kill
        }
        catch (SystemException)
        {
            // Various system-level errors during kill
        }
    }
}

/// <summary>
/// Pooled StringBuilder for stderr accumulation.
/// </summary>
file static class StringBuilderPool
{
    public static readonly ObjectPool<StringBuilder> Shared =
        new DefaultObjectPoolProvider { MaximumRetained = Environment.ProcessorCount * 2 }
            .CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);
}
