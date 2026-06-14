using GifMaker.Core;
using GifMaker.X11;

namespace GifMaker.UnitTests.X11;

public sealed class SlopScreenRegionSelectorTests
{
    [Fact]
    public async Task SelectAsyncReturnsCancelledOnExitCodeOne()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(1, "", "")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(SlopScreenRegionSelector.SelectionCancelled, result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsFailureWhenProcessStartFails()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => throw new InvalidOperationException("slop missing"));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to start slop: slop missing", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsFailureForNonZeroExit()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "bad display")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("slop failed with exit code 2: bad display", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsFailureForEmptyOutput()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, " ", "")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("slop produced no output", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsFailureForMalformedOutput()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "not geometry", "")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid slop output format: not geometry", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsFailureForZeroAreaOutput()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "0x20+1+2", "")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Selected region has zero area", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task SelectAsyncReturnsRegionForValidOutput()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "320x200+10+20", "")));

        var result = await new SlopScreenRegionSelector(runner).SelectAsync();

        Assert.True(result.IsSuccess);
        var region = result.GetValueOrThrow();
        Assert.Equal(10, region.X);
        Assert.Equal(20, region.Y);
        Assert.Equal(320, region.Width);
        Assert.Equal(200, region.Height);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("slop", command.FileName);
        Assert.Equal(ProcessIo.Capture, command.Io);
        Assert.Contains("-f", command.Arguments);
    }

    [Fact]
    public async Task SelectAsyncPropagatesCallerCancellation()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ProcessResult(0, "", "");
        });
        using var cts = new CancellationTokenSource();

        var task = new SlopScreenRegionSelector(runner).SelectAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
