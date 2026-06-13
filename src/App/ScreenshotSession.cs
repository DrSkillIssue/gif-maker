using GifMaker.Core;
using GifMaker.Screenshot;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

public sealed class ScreenshotSession
{
    private const int WindowHideDelayMs = 100;

    private readonly IProcessRunner _processRunner;
    private readonly ScreenshotService _screenshotService;
    private readonly ILogger<ScreenshotSession> _logger;

    public ScreenshotSession(
        IProcessRunner? processRunner = null,
        ScreenshotService? screenshotService = null,
        ILogger<ScreenshotSession>? logger = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
        _screenshotService = screenshotService ?? new ScreenshotService(_processRunner);
        _logger = logger ?? NullLogger<ScreenshotSession>.Instance;
    }

    public async Task<Result<ScreenshotViewState>> CaptureAsync(
        ScreenshotSource source,
        ScreenshotPointer pointer,
        WindowVisibilityLease visibility,
        CancellationToken ct = default)
    {
        var captureSource = source;
        if (source is ScreenshotSource.InteractiveSelection)
        {
            var regionResult = await SelectRegionAsync(pointer, visibility, ct).ConfigureAwait(false);
            if (!regionResult.IsSuccess)
                return regionResult.Match(
                    _ => throw new InvalidOperationException("Unreachable result state"),
                    error => error == SlopScreenRegionSelector.SelectionCancelled
                        ? Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Idle())
                        : Result<ScreenshotViewState>.Fail(error));

            captureSource = new ScreenshotSource.SelectedRegion(regionResult.GetValueOrThrow());
        }

        var plan = new ScreenshotPlan(captureSource, pointer, ScreenshotDestination.Default);
        var captureResult = await _screenshotService.CaptureAsync(plan, ct).ConfigureAwait(false);
        return captureResult.Match(
            result =>
            {
                var media = new SavedMedia(result.File.Path);
                var preview = LoadPreview(result.File.Path);
                return Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Saved(media, result.Region, preview));
            },
            Result<ScreenshotViewState>.Fail);
    }

    private async Task<Result<ScreenRegion>> SelectRegionAsync(
        ScreenshotPointer pointer,
        WindowVisibilityLease visibility,
        CancellationToken ct)
    {
        using var hiddenWindow = visibility;
        ScreenOverlay? cursorOverlay = null;

        try
        {
            if (pointer is ScreenshotPointer.FrozenAt frozen)
            {
                var createResult = ScreenOverlay.CreateCursor(frozen.Position);
                if (!createResult.IsSuccess)
                {
                    createResult.Match(
                        _ => throw new InvalidOperationException("Unreachable result state"),
                        error => _logger.LogWarning("Failed to create cursor overlay: {Error}", error));
                }
                else
                {
                    var overlay = createResult.GetValueOrThrow();
                    var showResult = overlay.Show();
                    if (showResult.IsSuccess)
                    {
                        cursorOverlay = overlay;
                    }
                    else
                    {
                        showResult.Match(
                            () => throw new InvalidOperationException("Unreachable result state"),
                            error => _logger.LogWarning("Failed to show cursor overlay: {Error}", error));
                        overlay.Dispose();
                    }
                }
            }

            await Task.Delay(WindowHideDelayMs, ct);

            var selector = new SlopScreenRegionSelector(_processRunner);
            var selection = await selector.SelectAsync(ct);
            if (cursorOverlay is not null)
            {
                cursorOverlay.Hide().Match(
                    () => { },
                    error => _logger.LogWarning("Failed to hide cursor overlay: {Error}", error));
            }

            return selection;
        }
        finally
        {
            cursorOverlay?.Dispose();
        }
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
