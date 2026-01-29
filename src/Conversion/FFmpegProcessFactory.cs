using System.Diagnostics;

namespace GifMaker.Conversion;

/// <summary>
/// Default <see cref="IFFmpegProcessFactory"/> implementation.
/// Wraps <see cref="System.Diagnostics.Process"/> for FFmpeg execution.
/// </summary>
/// <remarks>
/// Singleton pattern via <see cref="Default"/>. Configures process for:
/// <list type="bullet">
///   <item>Redirected stderr (for progress/error output)</item>
///   <item>Redirected stdout (captured but typically unused)</item>
///   <item>No shell execution (direct FFmpeg invocation)</item>
///   <item>No visible window</item>
/// </list>
/// </remarks>
public sealed class FFmpegProcessFactory : IFFmpegProcessFactory
{
    /// <summary>Shared singleton instance.</summary>
    public static FFmpegProcessFactory Default { get; } = new();

    private FFmpegProcessFactory() { }

    /// <inheritdoc/>
    public IFFmpegProcess Create(ReadOnlySpan<string> args) => new FFmpegProcess(args);

    /// <summary>
    /// Thin wrapper over <see cref="Process"/> implementing <see cref="IFFmpegProcess"/>.
    /// </summary>
    private sealed class FFmpegProcess : IFFmpegProcess
    {
        private readonly Process _process;

        /// <summary>Creates process with FFmpeg configured for conversion.</summary>
        /// <param name="args">FFmpeg command-line arguments.</param>
        public FFmpegProcess(ReadOnlySpan<string> args)
        {
            var startInfo = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            _process = new Process { StartInfo = startInfo };
        }

        /// <inheritdoc/>
        public bool Start() => _process.Start();

        /// <inheritdoc/>
        public Task WaitForExitAsync(CancellationToken ct) => _process.WaitForExitAsync(ct);

        /// <inheritdoc/>
        public int ExitCode => _process.ExitCode;

        /// <inheritdoc/>
        public StreamReader StandardError => _process.StandardError;

        /// <inheritdoc/>
        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        /// <inheritdoc/>
        public void Dispose() => _process.Dispose();
    }
}
