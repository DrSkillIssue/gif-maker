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
        _screenshotService = screenshotService ?? new ScreenshotService(logger: null, _processRunner);
        _logger = logger ?? NullLogger<ScreenshotSession>.Instance;
    }

    public async Task<Result<ScreenshotViewState>> CaptureAsync(
        ScreenshotOptions options,
        WindowVisibilityLease visibility,
        CancellationToken ct = default)
    {
        var regionResult = options.Mode == CaptureMode.Selection
            ? await SelectRegionAsync(options.Pointer, visibility, ct)
            : await _screenshotService.GetRegionAsync(options.Mode, ct);

        if (!regionResult.IsSuccess)
            return regionResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                error => error == "Selection cancelled"
                    ? Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Idle())
                    : Result<ScreenshotViewState>.Fail(error));

        var region = regionResult.GetValueOrThrow();
        var (showPointer, fixedCursorPosition) = ResolvePointer(options.Pointer);
        var request = new ScreenshotRequest(
            region,
            options.Mode,
            showPointer,
            fixedCursorPosition,
            options.OutputTarget);

        var captureResult = await _screenshotService.CaptureAsync(request, ct);
        return captureResult.Match(
            result =>
            {
                var media = new SavedMedia(result.FilePath);
                var preview = LoadPreview(result.FilePath);
                return Result<ScreenshotViewState>.Ok(new ScreenshotViewState.Saved(media, result.Region, preview));
            },
            Result<ScreenshotViewState>.Fail);
    }

    private async Task<Result<ScreenRegion>> SelectRegionAsync(
        PointerCapture pointer,
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

    private CursorOverlay? CreateCursorOverlay(PointerCapture pointer)
    {
        if (pointer is not PointerCapture.FrozenAt frozen)
            return null;

        try
        {
            return new CursorOverlay(frozen.X, frozen.Y);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to create cursor overlay");
            return null;
        }
    }

    private static (bool ShowPointer, (int X, int Y)? FixedCursorPosition) ResolvePointer(PointerCapture pointer) =>
        pointer switch
        {
            PointerCapture.Excluded => (false, null),
            PointerCapture.Live => (true, null),
            PointerCapture.FrozenAt frozen => (true, (frozen.X, frozen.Y)),
            _ => throw new InvalidOperationException($"Unhandled pointer capture: {pointer.GetType().Name}")
        };

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
