using System.Runtime.Versioning;
using GifMaker.Core;
using GifMaker.X11;

namespace GifMaker.Screenshot;

/// <summary>
/// Coordinates screenshot source resolution, file reservation, and capture.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ScreenshotService
{
    private readonly IProcessRunner _processRunner;
    private readonly ScreenshotFiles _files;
    private readonly FfmpegScreenshotCapture _capture;
    private readonly ActiveWindowRegionReader _activeWindow;

    public ScreenshotService(
        IProcessRunner? processRunner = null,
        ScreenshotFiles? files = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
        _files = files ?? new ScreenshotFiles();
        _capture = new FfmpegScreenshotCapture(_processRunner);
        _activeWindow = new ActiveWindowRegionReader(_processRunner);
    }

    public async Task<Result<CapturedScreenshot>> CaptureAsync(
        ScreenshotPlan plan,
        CancellationToken ct = default)
    {
        if (plan is null)
            return Result<CapturedScreenshot>.Fail("Screenshot plan is required");

        if (plan.Pointer is null)
            return Result<CapturedScreenshot>.Fail("Screenshot pointer is required");

        var regionResult = await ResolveSourceAsync(plan.Source, ct).ConfigureAwait(false);
        if (!regionResult.IsSuccess)
            return regionResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var fileResult = _files.Reserve(plan.Destination);
        if (!fileResult.IsSuccess)
            return fileResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var region = regionResult.GetValueOrThrow();
        var file = fileResult.GetValueOrThrow();
        var frame = new ResolvedScreenshotFrame(
            region,
            plan.Pointer,
            file,
            X11Display.GetCurrent());

        var captureResult = await _capture.CaptureAsync(frame, ct).ConfigureAwait(false);
        return captureResult.Match(
            capturedFile => Result<CapturedScreenshot>.Ok(new CapturedScreenshot(capturedFile, region)),
            Result<CapturedScreenshot>.Fail);
    }

    private async Task<Result<ScreenRegion>> ResolveSourceAsync(ScreenshotSource source, CancellationToken ct)
    {
        if (source is null)
            return Result<ScreenRegion>.Fail("Screenshot source is required");

        return source switch
        {
            ScreenshotSource.SelectedRegion selected => Result<ScreenRegion>.Ok(selected.Region),
            ScreenshotSource.InteractiveSelection => await SelectRegionAsync(ct).ConfigureAwait(false),
            ScreenshotSource.FullScreen => GetFullScreenRegion(),
            ScreenshotSource.ActiveWindow => await _activeWindow.ReadAsync(ct).ConfigureAwait(false),
            _ => Result<ScreenRegion>.Fail($"Unknown screenshot source: {source.GetType().Name}")
        };
    }

    private async Task<Result<ScreenRegion>> SelectRegionAsync(CancellationToken ct)
    {
        using var selector = new AreaSelector(_processRunner);
        return await selector.SelectAsync(ct).ConfigureAwait(false);
    }

    private static Result<ScreenRegion> GetFullScreenRegion()
    {
        var bounds = WindowPositioner.GetScreenBounds();
        if (bounds is null)
            return Result<ScreenRegion>.Fail("Failed to get screen bounds");

        return ScreenRegion.Create(0, 0, bounds.Value.Width, bounds.Value.Height);
    }
}
