using GifMaker.Core;
using GifMaker.Recording;

namespace GifMaker.UnitTests.Recording;

public sealed class FFmpegRecorderTests
{
    [Fact]
    public void RecordingSettingsRejectsTinyRegion()
    {
        var region = ScreenRegion.Create(0, 0, 1, 2).GetValueOrThrow();

        var result = FFmpegRecorder.RecordingSettings.Create(region);

        Assert.False(result.IsSuccess);
        Assert.Equal("Region too small (min 2x2)", result.Match(_ => "", error => error));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void RecordingSettingsRejectsBadFps(int fps)
    {
        var region = ScreenRegion.Create(0, 0, 100, 100).GetValueOrThrow();

        var result = FFmpegRecorder.RecordingSettings.Create(region, fps);

        Assert.False(result.IsSuccess);
        Assert.Equal("FPS must be between 1 and 240", result.Match(_ => "", error => error));
    }

    [Fact]
    public void RecordingSettingsFailsWhenDisplayMissing()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", null);
        try
        {
            var region = ScreenRegion.Create(0, 0, 100, 100).GetValueOrThrow();

            var result = FFmpegRecorder.RecordingSettings.Create(region);

            Assert.False(result.IsSuccess);
            Assert.Equal("DISPLAY is not set", result.Match(_ => "", error => error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public void RecordingSettingsRoundsCaptureDimensionsToEven()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            var region = ScreenRegion.Create(3, 5, 101, 99).GetValueOrThrow();

            var result = FFmpegRecorder.RecordingSettings.Create(region, fps: 30);

            Assert.True(result.IsSuccess);
            var settings = result.GetValueOrThrow();
            Assert.Equal(100, settings.CaptureWidth);
            Assert.Equal(98, settings.CaptureHeight);
            Assert.Equal(":99+3,5", settings.Display.FormatInput(settings.Region));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public void StartThrowsWhenAlreadyRecording()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var process = new FakeInteractiveProcess();
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            using var recorder = new FFmpegRecorder(
                processLauncher: launcher,
                tempPath: Path.Combine(directory.Path, "capture.mkv"));
            var settings = FFmpegRecorder.RecordingSettings.Create(
                ScreenRegion.Create(10, 20, 100, 100).GetValueOrThrow()).GetValueOrThrow();

            recorder.Start(settings);

            var exception = Assert.Throws<InvalidOperationException>(() => recorder.Start(settings));
            Assert.Equal("Recording already in progress or stopped", exception.Message);
            var command = Assert.Single(launcher.Commands);
            Assert.Equal("ffmpeg", command.FileName);
            Assert.Contains("x11grab", command.Arguments);
            Assert.Contains(":99+10,20", command.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAsyncThrowsWhenNotRecording()
    {
        using var recorder = new FFmpegRecorder(processLauncher: new FakeProcessLauncher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync());

        Assert.Equal("No recording in progress", exception.Message);
    }

    [Fact]
    public async Task StopAsyncThrowsWhenProcessAlreadyExited()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess("stderr text") { HasExited = true, ExitCode = 7 };
        var recorder = StartRecorder(directory, process);

        var exception = await Assert.ThrowsAsync<RecordingException>(() => recorder.StopAsync());

        Assert.Equal("FFmpeg exited unexpectedly", exception.Message);
        Assert.Equal(7, exception.ExitCode);
        await recorder.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncKillsOnStopTimeout()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess("timeout stderr");
        var recorder = StartRecorder(directory, process, stopTimeout: TimeSpan.FromMilliseconds(10));

        var exception = await Assert.ThrowsAsync<RecordingException>(() => recorder.StopAsync());

        Assert.Equal("FFmpeg failed to stop in time", exception.Message);
        Assert.Equal(-1, exception.ExitCode);
        Assert.Equal(1, process.KillCount);
        await recorder.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncThrowsOnBadExitCode()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess("bad exit stderr");
        var recorder = StartRecorder(directory, process);

        var complete = Task.Run(async () =>
        {
            await Task.Delay(10);
            process.CompleteExit(2);
        });
        var exception = await Assert.ThrowsAsync<RecordingException>(() => recorder.StopAsync());
        await complete;

        Assert.Equal("FFmpeg recording failed", exception.Message);
        Assert.Equal(2, exception.ExitCode);
        await recorder.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncThrowsWhenOutputMissing()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess();
        var recorder = StartRecorder(directory, process);

        var complete = Task.Run(async () =>
        {
            await Task.Delay(10);
            process.CompleteExit();
        });
        var exception = await Assert.ThrowsAsync<RecordingException>(() => recorder.StopAsync());
        await complete;

        Assert.Equal("FFmpeg did not produce output", exception.Message);
        Assert.Equal(0, exception.ExitCode);
        await recorder.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncReturnsDurationAndSizeOnExitCodeZeroOrQuit()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess();
        var recorder = StartRecorder(directory, process);
        File.WriteAllText(recorder.TempPath, "recorded");

        var complete = Task.Run(async () =>
        {
            await Task.Delay(10);
            process.CompleteExit(255);
        });
        var result = await recorder.StopAsync();
        await complete;

        Assert.Equal(8, result.FileSizeBytes);
        Assert.True(result.Duration >= TimeSpan.Zero);
        await recorder.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncKillsRunningProcess()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess();
        var recorder = StartRecorder(directory, process);

        await recorder.DisposeAsync();

        Assert.Equal(1, process.KillCount);
    }

    [Fact]
    public void DisposeKillsRunningProcess()
    {
        using var directory = new TemporaryDirectory();
        var process = new FakeInteractiveProcess();
        var recorder = StartRecorder(directory, process);

        recorder.Dispose();

        Assert.Equal(1, process.KillCount);
    }

    private static FFmpegRecorder StartRecorder(
        TemporaryDirectory directory,
        FakeInteractiveProcess process,
        TimeSpan? stopTimeout = null)
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            var recorder = new FFmpegRecorder(
                processLauncher: launcher,
                tempPath: Path.Combine(directory.Path, "capture.mkv"))
            {
                StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(5)
            };
            var settings = FFmpegRecorder.RecordingSettings.Create(
                ScreenRegion.Create(0, 0, 100, 100).GetValueOrThrow()).GetValueOrThrow();
            recorder.Start(settings);
            return recorder;
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }
}
