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
                    error => error == "Selection cancelled"
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
        using var cursorOverlay = CreateCursorOverlay(pointer);

        cursorOverlay?.Show();
        await Task.Delay(WindowHideDelayMs, ct);

        using var selector = new AreaSelector(_processRunner);
        var selection = await selector.SelectAsync(ct);
        cursorOverlay?.Hide();
        return selection;
    }

    private CursorOverlay? CreateCursorOverlay(ScreenshotPointer pointer)
    {
        if (pointer is not ScreenshotPointer.FrozenAt frozen)
            return null;

        try
        {
            return new CursorOverlay(frozen.Position.X, frozen.Position.Y);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to create cursor overlay");
            return null;
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
