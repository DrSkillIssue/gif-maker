using GifMaker.Conversion;
using GifMaker.Core;

namespace GifMaker.UnitTests.Conversion;

public sealed class FFmpegConverterTests
{
    public static TheoryData<TimeSpan> InvalidTimeouts => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(-1)
    };

    [Theory]
    [MemberData(nameof(InvalidTimeouts))]
    public void TimeoutRejectsNonPositiveValues(TimeSpan timeout)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new FFmpegConverter { Timeout = timeout });
    }

    [Fact]
    public Task ConvertAsyncBuildsGifCommand()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory, ConversionFormat.Gif);

        var command = FFmpegConverter.BuildCommand(job);

        Assert.Equal("ffmpeg", command.FileName);
        Assert.Equal(ProcessIo.CaptureError, command.Io);
        Assert.Equal("-filter_complex", command.Arguments[3]);
        Assert.Contains("palettegen=stats_mode=diff", command.Arguments[4], StringComparison.Ordinal);
        Assert.Equal(job.OutputFile.Path, command.Arguments[^1]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ConvertAsyncBuildsMp4Command()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory, ConversionFormat.Mp4, fps: 60);

        var command = FFmpegConverter.BuildCommand(job);

        Assert.Contains("libx264", command.Arguments);
        Assert.Contains("60", command.Arguments);
        Assert.Contains("+faststart", command.Arguments);
        Assert.Equal(job.OutputFile.Path, command.Arguments[^1]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ConvertAsyncBuildsWebMCommand()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory, ConversionFormat.WebM, fps: 24);

        var command = FFmpegConverter.BuildCommand(job);

        Assert.Contains("libvpx-vp9", command.Arguments);
        Assert.Contains("24", command.Arguments);
        Assert.Contains("0", command.Arguments);
        Assert.Equal(job.OutputFile.Path, command.Arguments[^1]);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConvertAsyncReturnsFailureWhenRunnerThrows()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => throw new InvalidOperationException("ffmpeg missing"));

        var result = await new FFmpegConverter(runner).ConvertAsync(job);

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to start FFmpeg: ffmpeg missing", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ConvertAsyncReturnsFailureOnTimeout()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ProcessResult(0, "", "");
        });

        var result = await new FFmpegConverter(runner)
        {
            Timeout = TimeSpan.FromMilliseconds(10)
        }.ConvertAsync(job);

        Assert.False(result.IsSuccess);
        Assert.Contains("FFmpeg conversion timed out", result.Match(_ => "", error => error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsyncPropagatesCallerCancellation()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ProcessResult(0, "", "");
        });
        using var cts = new CancellationTokenSource();

        var task = new FFmpegConverter(runner).ConvertAsync(job, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ConvertAsyncReturnsFailureOnNonZeroExit()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "bad codec")));

        var result = await new FFmpegConverter(runner).ConvertAsync(job);

        Assert.False(result.IsSuccess);
        Assert.Equal("FFmpeg conversion failed: bad codec", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ConvertAsyncReturnsFailureWhenOutputMissing()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);

        var result = await new FFmpegConverter(new FakeProcessRunner()).ConvertAsync(job);

        Assert.False(result.IsSuccess);
        Assert.Equal("FFmpeg completed but output file was not created", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task ConvertAsyncReturnsSuccessWhenOutputExists()
    {
        using var directory = new TemporaryDirectory();
        var job = CreateConversionJob(directory);
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) =>
        {
            File.WriteAllText(job.OutputFile.Path, "gif");
            return Task.FromResult(new ProcessResult(0, "", ""));
        });

        var result = await new FFmpegConverter(runner).ConvertAsync(job);

        Assert.True(result.IsSuccess);
        Assert.Equal(job.OutputFile, result.GetValueOrThrow().OutputFile);
    }

    private static ConversionJob CreateConversionJob(
        TemporaryDirectory directory,
        ConversionFormat format = ConversionFormat.Gif,
        int fps = 30)
    {
        var sourcePath = Path.Combine(directory.Path, "source.mkv");
        File.WriteAllText(sourcePath, "video");
        var outputPath = Path.Combine(directory.Path, "out" + format.GetExtension());
        var profile = ConversionProfile.Create(format, fps).GetValueOrThrow();
        return ConversionJob.Create(sourcePath, outputPath, profile).GetValueOrThrow();
    }
}
