using GifMaker.Cli;

namespace GifMaker.UnitTests.Cli;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task RunAsyncHelpWritesHelpAndReturnsZero()
    {
        using var console = new ConsoleCapture();

        var exitCode = await CliRunner.RunAsync(new CliArgs.Help());

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: gifmaker [command] [options]", console.StandardOutput, StringComparison.Ordinal);
        Assert.Equal("", console.StandardError);
    }

    [Fact]
    public async Task RunAsyncVersionWritesVersionAndReturnsZero()
    {
        using var console = new ConsoleCapture();

        var exitCode = await CliRunner.RunAsync(new CliArgs.Version());

        Assert.Equal(0, exitCode);
        Assert.Equal(CliParser.GetVersionText() + Environment.NewLine, console.StandardOutput);
        Assert.Equal("", console.StandardError);
    }

    [Fact]
    public async Task RunAsyncInvalidWritesErrorAndReturnsOne()
    {
        using var console = new ConsoleCapture();

        var exitCode = await CliRunner.RunAsync(new CliArgs.Invalid("bad input"));

        Assert.Equal(1, exitCode);
        Assert.Equal("", console.StandardOutput);
        Assert.Contains("Error: bad input", console.StandardError, StringComparison.Ordinal);
        Assert.Contains("Run 'gifmaker --help' for usage.", console.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsyncGuiReturnsOne()
    {
        using var console = new ConsoleCapture();

        var exitCode = await CliRunner.RunAsync(new CliArgs.Gui());

        Assert.Equal(1, exitCode);
        Assert.Equal("", console.StandardOutput);
        Assert.Equal("", console.StandardError);
    }
}
