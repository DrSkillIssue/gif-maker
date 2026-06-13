namespace GifMaker.Recording;

public sealed class RecordingException(string message, int exitCode, string stderr)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public string StandardError { get; } = stderr;
}
