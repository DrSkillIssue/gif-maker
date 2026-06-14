using System.Text;
using GifMaker.Core;

namespace GifMaker.UnitTests;

internal sealed class FakeInteractiveProcess : IInteractiveProcess
{
    private readonly MemoryStream _standardError;
    private readonly MemoryStream _standardInput = new();

    public FakeInteractiveProcess(string standardError = "")
    {
        _standardError = new MemoryStream(Encoding.UTF8.GetBytes(standardError));
        StandardError = new StreamReader(_standardError, Encoding.UTF8);
        StandardInput = new StreamWriter(_standardInput, Encoding.UTF8) { AutoFlush = true };
    }

    public bool HasExited { get; set; }

    public int ExitCode { get; set; }

    public StreamReader StandardError { get; }

    public StreamWriter StandardInput { get; }

    public int KillCount { get; private set; }

    public TaskCompletionSource Exit { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForExitAsync(CancellationToken ct)
    {
        if (HasExited)
            return Task.CompletedTask;

        return WaitForExitCoreAsync(ct);
    }

    public void Kill(bool entireProcessTree)
    {
        KillCount++;
        CompleteExit(exitCode: -1);
    }

    public void CompleteExit(int exitCode = 0)
    {
        ExitCode = exitCode;
        HasExited = true;
        Exit.TrySetResult();
    }

    public void Dispose()
    {
        StandardError.Dispose();
        StandardInput.Dispose();
        _standardError.Dispose();
        _standardInput.Dispose();
    }

    private async Task WaitForExitCoreAsync(CancellationToken ct)
    {
        await Exit.Task.WaitAsync(ct);
        HasExited = true;
    }
}
