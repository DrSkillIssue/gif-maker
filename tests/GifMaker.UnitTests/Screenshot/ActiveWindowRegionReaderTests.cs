using GifMaker.Core;
using GifMaker.Screenshot;

namespace GifMaker.UnitTests.Screenshot;

public sealed class ActiveWindowRegionReaderTests
{
    [Fact]
    public async Task ReadAsyncReturnsFailureWhenGetActiveWindowFails()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(1, "", "")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to get active window (is xdotool installed?)", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ReadAsyncReturnsFailureWhenActiveWindowEmpty()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, " ", "")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("No active window found", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ReadAsyncReturnsFailureWhenGeometryCommandFails()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "123", "")));
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "bad window")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to get window geometry: bad window", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ReadAsyncReturnsFailureWhenGeometryOutputMalformed()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "123", "")));
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "missing geometry", "")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to parse window geometry: missing geometry", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ReadAsyncReturnsFailureWhenWindowHasZeroArea()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "123", "")));
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(
            0,
            """
            Window 123
              Position: 10,20 (screen: 0)
              Geometry: 0x200
            """,
            "")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Window has zero area", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ReadAsyncReturnsRegionForValidGeometry()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "123", "")));
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(
            0,
            """
            Window 123
              Position: 10,20 (screen: 0)
              Geometry: 300x200
            """,
            "")));

        var result = await new ActiveWindowRegionReader(runner).ReadAsync();

        Assert.True(result.IsSuccess);
        var region = result.GetValueOrThrow();
        Assert.Equal(10, region.X);
        Assert.Equal(20, region.Y);
        Assert.Equal(300, region.Width);
        Assert.Equal(200, region.Height);
        Assert.Collection(
            runner.Commands,
            command =>
            {
                Assert.Equal("xdotool", command.FileName);
                Assert.Equal(["getactivewindow"], command.Arguments);
            },
            command =>
            {
                Assert.Equal("xdotool", command.FileName);
                Assert.Equal(["getwindowgeometry", "123"], command.Arguments);
            });
    }

    [Fact]
    public async Task ReadAsyncPropagatesCallerCancellation()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ProcessResult(0, "", "");
        });
        using var cts = new CancellationTokenSource();

        var task = new ActiveWindowRegionReader(runner).ReadAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
