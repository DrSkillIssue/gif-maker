using System.Diagnostics;

namespace GifMaker.Recording;

/// <summary>
/// Default <see cref="IFFmpegRecordingProcessFactory"/> implementation.
/// Wraps <see cref="System.Diagnostics.Process"/> for FFmpeg execution.
/// </summary>
/// <remarks>
/// Singleton pattern via <see cref="Default"/>. Configures process for:
/// <list type="bullet">
///   <item>Redirected stdin (for 'q' quit command)</item>
///   <item>Redirected stderr (for progress/error output)</item>
///   <item>No shell execution (direct FFmpeg invocation)</item>
///   <item>No visible window</item>
/// </list>
/// </remarks>
public sealed class FFmpegRecordingProcessFactory : IFFmpegRecordingProcessFactory
{
    /// <summary>Shared singleton instance.</summary>
    public static FFmpegRecordingProcessFactory Default { get; } = new();

    private FFmpegRecordingProcessFactory() { }

    /// <inheritdoc/>
    public IFFmpegRecordingProcess Create(ReadOnlySpan<string> args) => new FFmpegProcess(args);

    /// <summary>
    /// Thin wrapper over <see cref="Process"/> implementing <see cref="IFFmpegRecordingProcess"/>.
    /// </summary>
    private sealed class FFmpegProcess : IFFmpegRecordingProcess
    {
        private readonly Process _process;

        /// <summary>Creates process with FFmpeg configured for recording.</summary>
        /// <param name="args">FFmpeg command-line arguments.</param>
        public FFmpegProcess(ReadOnlySpan<string> args)
        {
            var startInfo = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            _process = new Process { StartInfo = startInfo };
        }

        /// <inheritdoc/>
        public bool Start() => _process.Start();

        /// <inheritdoc/>
        public bool HasExited => _process.HasExited;

        /// <inheritdoc/>
        public int ExitCode => _process.ExitCode;

        /// <inheritdoc/>
        public StreamWriter StandardInput => _process.StandardInput;

        /// <inheritdoc/>
        public StreamReader StandardError => _process.StandardError;

        /// <inheritdoc/>
        public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);

        /// <inheritdoc/>
        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        /// <inheritdoc/>
        public void Dispose() => _process.Dispose();
    }
}
