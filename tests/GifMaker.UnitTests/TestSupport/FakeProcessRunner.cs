using GifMaker.Core;

namespace GifMaker.UnitTests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessCommand> Commands { get; } = [];

    public Queue<Func<ProcessCommand, CancellationToken, Task<ProcessResult>>> Results { get; } = [];

    public Queue<bool> DetachedResults { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken ct = default)
    {
        Commands.Add(command);
        return Results.Count == 0
            ? Task.FromResult(new ProcessResult(0, "", ""))
            : Results.Dequeue().Invoke(command, ct);
    }

    public bool StartDetached(ProcessCommand command)
    {
        Commands.Add(command);
        return DetachedResults.Count == 0 || DetachedResults.Dequeue();
    }
}
