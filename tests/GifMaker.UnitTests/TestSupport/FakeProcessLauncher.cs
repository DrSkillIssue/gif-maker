using GifMaker.Core;

namespace GifMaker.UnitTests;

internal sealed class FakeProcessLauncher : IProcessLauncher
{
    public List<ProcessCommand> Commands { get; } = [];

    public Queue<IInteractiveProcess> Processes { get; } = [];

    public IInteractiveProcess StartInteractive(ProcessCommand command)
    {
        Commands.Add(command);

        if (Processes.Count == 0)
            throw new InvalidOperationException("No fake process was queued.");

        return Processes.Dequeue();
    }
}
