using GifMaker.App;
using GifMaker.Conversion;
using GifMaker.Core;
using GifMaker.Recording;

namespace GifMaker.UnitTests.App;

public sealed class RecordingSessionTests
{
    [Fact]
    public async Task StartFromSelectionReturnsIdleOnSelectionCancel()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(1, "", "")));
        await using var session = new RecordingSession(processRunner: runner);
        var leaseDisposed = false;

        var result = await session.StartFromSelectionAsync(
            new RecordStartOptions(30),
            new WindowVisibilityLease(() => leaseDisposed = true));

        Assert.True(result.IsSuccess);
        Assert.IsType<RecordViewState.Idle>(result.GetValueOrThrow());
        Assert.True(leaseDisposed);
    }

    [Fact]
    public async Task StartFromSelectionReturnsFailureOnSelectionFailure()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "bad display")));
        await using var session = new RecordingSession(processRunner: runner);

        var result = await session.StartFromSelectionAsync(
            new RecordStartOptions(30),
            new WindowVisibilityLease(() => { }));

        Assert.False(result.IsSuccess);
        Assert.Equal("slop failed with exit code 2: bad display", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task StartFromSelectionReturnsFailureOnInvalidSettings()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "1x1+0+0", "")));
        await using var session = new RecordingSession(processRunner: runner);

        var result = await session.StartFromSelectionAsync(
            new RecordStartOptions(30),
            new WindowVisibilityLease(() => { }));

        Assert.False(result.IsSuccess);
        Assert.Equal("Region too small (min 2x2)", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task StartFromSelectionReturnsRecordingOnStartSuccess()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+5+7", "")));
            var process = new FakeInteractiveProcess();
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () => new FFmpegRecorder(
                    processLauncher: launcher,
                    tempPath: Path.Combine(directory.Path, "capture.mkv")));

            var result = await session.StartFromSelectionAsync(
                new RecordStartOptions(24),
                new WindowVisibilityLease(() => { }));

            Assert.True(result.IsSuccess);
            var recording = Assert.IsType<RecordViewState.Recording>(result.GetValueOrThrow());
            Assert.Equal(24, recording.Fps);
            var command = Assert.Single(launcher.Commands);
            Assert.Contains(":99+5,7", command.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StartFromSelectionFailsWhenAlreadyRecording()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(new FakeInteractiveProcess());
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () => new FFmpegRecorder(
                    processLauncher: launcher,
                    tempPath: Path.Combine(directory.Path, "capture.mkv")));

            var first = await session.StartFromSelectionAsync(
                new RecordStartOptions(30),
                new WindowVisibilityLease(() => { }));
            var second = await session.StartFromSelectionAsync(
                new RecordStartOptions(30),
                new WindowVisibilityLease(() => { }));

            Assert.True(first.IsSuccess);
            Assert.False(second.IsSuccess);
            Assert.Equal("Recording already in progress", second.Match(_ => "", error => error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAndConvertReturnsFailureWhenNoRecordingInProgress()
    {
        await using var session = new RecordingSession();

        var result = await session.StopAndConvertAsync(new RecordExportTarget(ConversionFormat.Gif, null));

        Assert.False(result.IsSuccess);
        Assert.Equal("No recording in progress", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task StopAndConvertReturnsIdleWhenCancellationIsRequested()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(new FakeInteractiveProcess());
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () => new FFmpegRecorder(
                    processLauncher: launcher,
                    tempPath: Path.Combine(directory.Path, "capture.mkv")));
            await session.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var result = await session.StopAndConvertAsync(
                new RecordExportTarget(ConversionFormat.Gif, directory.Path),
                cts.Token);

            Assert.True(result.IsSuccess);
            Assert.IsType<RecordViewState.Idle>(result.GetValueOrThrow());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAndConvertReturnsFailureWhenRecorderStopThrows()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var process = new FakeInteractiveProcess("failed") { HasExited = true, ExitCode = 9 };
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () => new FFmpegRecorder(
                    processLauncher: launcher,
                    tempPath: Path.Combine(directory.Path, "capture.mkv")));
            await session.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));

            var result = await session.StopAndConvertAsync(new RecordExportTarget(ConversionFormat.Gif, directory.Path));

            Assert.False(result.IsSuccess);
            Assert.Equal("FFmpeg exited unexpectedly", result.Match(_ => "", error => error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAndConvertReturnsFailureWhenConversionFails()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var process = new FakeInteractiveProcess();
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            FFmpegRecorder? recorder = null;
            var converterRunner = new FakeProcessRunner();
            converterRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "conversion failed")));
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () =>
                {
                    recorder = new FFmpegRecorder(
                        processLauncher: launcher,
                        tempPath: Path.Combine(directory.Path, "capture.mkv"));
                    return recorder;
                },
                createConverter: () => new FFmpegConverter(converterRunner));
            await session.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));
            var complete = Task.Run(async () =>
            {
                await Task.Delay(10);
                File.WriteAllText(recorder!.TempPath, "recorded");
                process.CompleteExit();
            });

            var result = await session.StopAndConvertAsync(new RecordExportTarget(ConversionFormat.Gif, directory.Path));
            await complete;

            Assert.False(result.IsSuccess);
            Assert.Equal("FFmpeg conversion failed: conversion failed", result.Match(_ => "", error => error));
            Assert.False(File.Exists(recorder!.TempPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAndConvertDeletesTempFileOnSuccessAndFailure()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var failureDirectory = new TemporaryDirectory();
            var failureSelectionRunner = new FakeProcessRunner();
            failureSelectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var failureProcess = new FakeInteractiveProcess();
            var failureLauncher = new FakeProcessLauncher();
            failureLauncher.Processes.Enqueue(failureProcess);
            FFmpegRecorder? failureRecorder = null;
            var failureConverterRunner = new FakeProcessRunner();
            failureConverterRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(2, "", "conversion failed")));
            await using (var failureSession = new RecordingSession(
                processRunner: failureSelectionRunner,
                createRecorder: () =>
                {
                    failureRecorder = new FFmpegRecorder(
                        processLauncher: failureLauncher,
                        tempPath: Path.Combine(failureDirectory.Path, "failure.mkv"));
                    return failureRecorder;
                },
                createConverter: () => new FFmpegConverter(failureConverterRunner)))
            {
                await failureSession.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));
                var failureComplete = Task.Run(async () =>
                {
                    await Task.Delay(10);
                    File.WriteAllText(failureRecorder!.TempPath, "recorded");
                    failureProcess.CompleteExit();
                });

                var failure = await failureSession.StopAndConvertAsync(
                    new RecordExportTarget(ConversionFormat.Gif, failureDirectory.Path));
                await failureComplete;

                Assert.False(failure.IsSuccess);
                Assert.False(File.Exists(failureRecorder!.TempPath));
            }

            using var successDirectory = new TemporaryDirectory();
            var successSelectionRunner = new FakeProcessRunner();
            successSelectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var successProcess = new FakeInteractiveProcess();
            var successLauncher = new FakeProcessLauncher();
            successLauncher.Processes.Enqueue(successProcess);
            FFmpegRecorder? successRecorder = null;
            var successConverterRunner = new FakeProcessRunner();
            successConverterRunner.Results.Enqueue((command, _) =>
            {
                File.WriteAllText(command.Arguments[^1], "gif");
                return Task.FromResult(new ProcessResult(0, "", ""));
            });
            await using var successSession = new RecordingSession(
                processRunner: successSelectionRunner,
                createRecorder: () =>
                {
                    successRecorder = new FFmpegRecorder(
                        processLauncher: successLauncher,
                        tempPath: Path.Combine(successDirectory.Path, "success.mkv"));
                    return successRecorder;
                },
                createConverter: () => new FFmpegConverter(successConverterRunner));
            await successSession.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));
            var successComplete = Task.Run(async () =>
            {
                await Task.Delay(10);
                File.WriteAllText(successRecorder!.TempPath, "recorded");
                successProcess.CompleteExit();
            });

            var success = await successSession.StopAndConvertAsync(
                new RecordExportTarget(ConversionFormat.Gif, successDirectory.Path));
            await successComplete;

            Assert.True(success.IsSuccess, success.Match(_ => "", error => error));
            Assert.False(File.Exists(successRecorder!.TempPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task StopAndConvertReturnsSavedMediaOnSuccess()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var process = new FakeInteractiveProcess();
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            FFmpegRecorder? recorder = null;
            var converterRunner = new FakeProcessRunner();
            converterRunner.Results.Enqueue((command, _) =>
            {
                File.WriteAllText(command.Arguments[^1], "gif");
                return Task.FromResult(new ProcessResult(0, "", ""));
            });
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () =>
                {
                    recorder = new FFmpegRecorder(
                        processLauncher: launcher,
                        tempPath: Path.Combine(directory.Path, "capture.mkv"));
                    return recorder;
                },
                createConverter: () => new FFmpegConverter(converterRunner));
            await session.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));
            var complete = Task.Run(async () =>
            {
                await Task.Delay(10);
                File.WriteAllText(recorder!.TempPath, "recorded");
                process.CompleteExit();
            });

            var result = await session.StopAndConvertAsync(new RecordExportTarget(ConversionFormat.Gif, directory.Path));
            await complete;

            Assert.True(result.IsSuccess, result.Match(_ => "", error => error));
            var saved = Assert.IsType<RecordViewState.Saved>(result.GetValueOrThrow());
            Assert.EndsWith(".gif", saved.Media.Path, StringComparison.Ordinal);
            Assert.True(File.Exists(saved.Media.Path));
            Assert.False(File.Exists(recorder!.TempPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public async Task DisposeAsyncCancelsConversionAndDisposesRecorder()
    {
        var previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":99");
        try
        {
            using var directory = new TemporaryDirectory();
            var selectionRunner = new FakeProcessRunner();
            selectionRunner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(0, "100x100+0+0", "")));
            var process = new FakeInteractiveProcess();
            var launcher = new FakeProcessLauncher();
            launcher.Processes.Enqueue(process);
            await using var session = new RecordingSession(
                processRunner: selectionRunner,
                createRecorder: () => new FFmpegRecorder(
                    processLauncher: launcher,
                    tempPath: Path.Combine(directory.Path, "capture.mkv")));
            await session.StartFromSelectionAsync(new RecordStartOptions(30), new WindowVisibilityLease(() => { }));

            await session.DisposeAsync();

            Assert.Equal(1, process.KillCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }
}
