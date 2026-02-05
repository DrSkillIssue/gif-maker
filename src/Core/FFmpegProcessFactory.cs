using System.Diagnostics;

namespace GifMaker.Core;

/// <summary>
/// Base FFmpeg process wrapper implementing <see cref="IFFmpegProcess"/>.
/// </summary>
internal class FFmpegProcessBase : IFFmpegProcess
{
    protected readonly Process Process;

    protected FFmpegProcessBase(Process process) => Process = process;

    /// <inheritdoc/>
    public bool Start() => Process.Start();

    /// <inheritdoc/>
    public Task WaitForExitAsync(CancellationToken ct) => Process.WaitForExitAsync(ct);

    /// <inheritdoc/>
    public int ExitCode => Process.ExitCode;

    /// <inheritdoc/>
    public StreamReader StandardError => Process.StandardError;

    /// <inheritdoc/>
    public void Kill(bool entireProcessTree) => Process.Kill(entireProcessTree);

    /// <inheritdoc/>
    public void Dispose()
    {
        Process.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// FFmpeg process for conversion operations (no stdin needed).
/// </summary>
internal sealed class FFmpegConversionProcess : FFmpegProcessBase
{
    public FFmpegConversionProcess(ReadOnlySpan<string> args)
        : base(CreateProcess(args)) { }

    private static Process CreateProcess(ReadOnlySpan<string> args)
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

        return new Process { StartInfo = startInfo };
    }
}

/// <summary>
/// FFmpeg process for recording operations (with stdin for 'q' quit).
/// </summary>
internal sealed class FFmpegRecordingProcess : FFmpegProcessBase, IFFmpegRecordingProcess
{
    public FFmpegRecordingProcess(ReadOnlySpan<string> args)
        : base(CreateProcess(args)) { }

    /// <inheritdoc/>
    public bool HasExited => Process.HasExited;

    /// <inheritdoc/>
    public StreamWriter StandardInput => Process.StandardInput;

    private static Process CreateProcess(ReadOnlySpan<string> args)
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

        return new Process { StartInfo = startInfo };
    }
}

/// <summary>
/// Factory for creating FFmpeg conversion processes.
/// </summary>
public sealed class FFmpegConversionProcessFactory : IFFmpegProcessFactory<IFFmpegProcess>
{
    /// <summary>Shared singleton instance.</summary>
    public static FFmpegConversionProcessFactory Default { get; } = new();

    private FFmpegConversionProcessFactory() { }

    /// <inheritdoc/>
    public IFFmpegProcess Create(ReadOnlySpan<string> args) => new FFmpegConversionProcess(args);
}

/// <summary>
/// Factory for creating FFmpeg recording processes.
/// </summary>
public sealed class FFmpegRecordingProcessFactory : IFFmpegProcessFactory<IFFmpegRecordingProcess>
{
    /// <summary>Shared singleton instance.</summary>
    public static FFmpegRecordingProcessFactory Default { get; } = new();

    private FFmpegRecordingProcessFactory() { }

    /// <inheritdoc/>
    public IFFmpegRecordingProcess Create(ReadOnlySpan<string> args) => new FFmpegRecordingProcess(args);
}
