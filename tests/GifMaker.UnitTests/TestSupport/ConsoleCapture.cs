namespace GifMaker.UnitTests;

internal sealed class ConsoleCapture : IDisposable
{
    private readonly TextWriter _originalOutput = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly StringWriter _standardOutput = new();
    private readonly StringWriter _standardError = new();

    public ConsoleCapture()
    {
        Console.SetOut(_standardOutput);
        Console.SetError(_standardError);
    }

    public string StandardOutput => _standardOutput.ToString();

    public string StandardError => _standardError.ToString();

    public void Dispose()
    {
        Console.SetOut(_originalOutput);
        Console.SetError(_originalError);
        _standardOutput.Dispose();
        _standardError.Dispose();
    }
}
