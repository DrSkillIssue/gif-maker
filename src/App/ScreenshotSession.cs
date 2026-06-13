using GifMaker.Core;
using GifMaker.Screenshot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

public sealed class ScreenshotSession
{
    private const int WindowHideDelayMs = 100;

    private readonly Gtk.Application _application;
    private readonly ScreenshotService _screenshotService;
    private readonly ILogger<ScreenshotSession> _logger;

    public ScreenshotSession(
        Gtk.Application application,
        IProcessRunner? processRunner = null,
        ScreenshotService? screenshotService = null,
        ILogger<ScreenshotSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        _screenshotService = screenshotService ?? new ScreenshotService(processRunner ?? ProcessRunner.Default);
        _logger = logger ?? NullLogger<ScreenshotSession>.Instance;
    }

    public async Task<Result<ScreenshotViewState>> CaptureAsync(
        ScreenshotCapture capture,
        CancellationToken ct = default)
    {
        var captureResult = await _screenshotService.CaptureAsync(capture, ct);
        return CreateSavedState(captureResult);
    }

    public async Task<Result<ScreenshotViewState>> CaptureSelectionAsync(
        ScreenshotPointer pointer,
        WindowVisibilityLease visibility,
        CancellationToken ct = default)
    {
        using var hiddenWindow = visibility;

        await Task.Delay(WindowHideDelayMs, ct);

        var freezeResult = await _screenshotService.FreezeScreenAsync(pointer, ct);
        if (!freezeResult.IsSuccess)
            return freezeResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenshotViewState>.Fail);

        using var frozen = freezeResult.GetValueOrThrow();
        using var selectionWindow = new FrozenScreenshotSelectionWindow(_application, frozen);
        var selectionResult = await selectionWindow.SelectAsync(ct);
        if (!selectionResult.IsSuccess)
            return selectionResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                error => error == FrozenScreenshotSelectionWindow.SelectionCancelled
                    ? Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Idle())
                    : Result<ScreenshotViewState>.Fail(error));

        var crop = new FrozenScreenshotCrop(
            frozen,
            selectionResult.GetValueOrThrow(),
            ScreenshotDestination.Default);
        return CreateSavedState(_screenshotService.Crop(crop));
    }

    private Result<ScreenshotViewState> CreateSavedState(Result<CapturedScreenshot> captureResult)
    {
        return captureResult.Match(
            result =>
            {
                var media = new SavedMedia(result.File.Path);
                var preview = LoadPreview(result.File.Path);
                return Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Saved(media, result.Region, preview));
            },
            Result<ScreenshotViewState>.Fail);
    }

    private Gdk.Texture? LoadPreview(string filePath)
    {
        try
        {
            return Gdk.Texture.NewFromFilename(filePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to load screenshot preview");
            return null;
        }
    }
}
