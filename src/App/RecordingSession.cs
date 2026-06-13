using GifMaker.Conversion;
using GifMaker.Core;
using GifMaker.Recording;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

public sealed class RecordingSession : IAsyncDisposable
{
    private const int WindowHideDelayMs = 100;

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<RecordingSession> _logger;
    private readonly Func<FFmpegRecorder> _createRecorder;
    private readonly Func<FFmpegConverter> _createConverter;

    private FFmpegRecorder? _recorder;
    private int? _fps;
    private CancellationTokenSource? _conversionCts;
    private int _disposed;

    public RecordingSession(
        IProcessRunner? processRunner = null,
        ILogger<RecordingSession>? logger = null,
        Func<FFmpegRecorder>? createRecorder = null,
        Func<FFmpegConverter>? createConverter = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
        _logger = logger ?? NullLogger<RecordingSession>.Instance;
        _createRecorder = createRecorder ?? (() => new FFmpegRecorder());
        _createConverter = createConverter ?? (() => new FFmpegConverter());
    }

    public async Task<Result<RecordViewState>> StartFromSelectionAsync(
        RecordStartOptions options,
        WindowVisibilityLease visibility)
    {
        ThrowIfDisposed();

        if (_recorder is not null)
            return Result<RecordViewState>.Fail("Recording already in progress");

        using var hiddenWindow = visibility;
        await Task.Delay(WindowHideDelayMs);

        var selector = new SlopScreenRegionSelector(_processRunner);
        var selection = await selector.SelectAsync();

        return selection.Match(
            region =>
            {
                var settingsResult = FFmpegRecorder.RecordingSettings.Create(region, options.Fps);
                return settingsResult.Match(
                    StartRecording,
                    Result<RecordViewState>.Fail);
            },
            error =>
            {
                if (error != SlopScreenRegionSelector.SelectionCancelled)
                    _logger.LogWarning("Selection failed: {Error}", error);

                return error == SlopScreenRegionSelector.SelectionCancelled
                    ? Result<RecordViewState>.Ok(new RecordViewState.Idle())
                    : Result<RecordViewState>.Fail(error);
            });

        Result<RecordViewState> StartRecording(FFmpegRecorder.RecordingSettings settings)
        {
            FFmpegRecorder? recorder = null;
            try
            {
                recorder = _createRecorder();
                recorder.Start(settings);
                _recorder = recorder;
                _fps = options.Fps;
                return Result<RecordViewState>.Ok(new RecordViewState.Recording(options.Fps));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                recorder?.Dispose();
                _logger.LogError(ex, "Recording start failed");
                return Result<RecordViewState>.Fail(ex.Message);
            }
        }
    }

    public async Task<Result<RecordViewState>> StopAndConvertAsync(
        RecordExportTarget target,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_recorder is not { } recorder || _fps is not { } fps)
            return Result<RecordViewState>.Fail("No recording in progress");

        string? tempPath = null;
        _conversionCts?.Dispose();
        _conversionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await recorder.StopAsync(ct);
            tempPath = recorder.TempPath;

            var outputPath = RecordingOutputPaths.GenerateRecordingPath(target.Format, target.OutputDirectory);
            var profile = ConversionProfile.Create(target.Format, fps).GetValueOrThrow();
            var job = ConversionJob.Create(tempPath, outputPath, profile).GetValueOrThrow();

            var conversionResult = await _createConverter().ConvertAsync(job, _conversionCts.Token);
            if (!conversionResult.IsSuccess)
                return Result<RecordViewState>.Fail(conversionResult.Match(_ => "", error => error));

            var converted = conversionResult.GetValueOrThrow();
            var saved = new SavedMedia(converted.OutputFile.Path);
            _logger.LogInformation("Saved: {OutputPath}", saved.Path);

            return Result<RecordViewState>.Ok(new RecordViewState.Saved(saved));
        }
        catch (OperationCanceledException)
        {
            return Result<RecordViewState>.Ok(new RecordViewState.Idle());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Recording conversion failed");
            return Result<RecordViewState>.Fail(ex.Message);
        }
        finally
        {
            _conversionCts?.Dispose();
            _conversionCts = null;
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {Path}", tempPath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file: {Path}", tempPath);
                }
            }

            recorder.Dispose();
            _recorder = null;
            _fps = null;
        }
    }

    public void CancelConversion()
    {
        if (_disposed != 0) return;
        _conversionCts?.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _conversionCts?.Cancel();
        _conversionCts?.Dispose();
        _recorder?.Dispose();
        _conversionCts = null;
        _recorder = null;
        _fps = null;
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
