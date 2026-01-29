namespace GifMaker.Core;

/// <summary>
/// Exception thrown when FFmpeg operation fails.
/// </summary>
public sealed class FFmpegException(string message, int exitCode, string stderr) 
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = stderr;
}
