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
    private readonly X11Desktop _desktop;

    public ScreenshotService(
        IProcessRunner? processRunner = null,
        ScreenshotFiles? files = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
        _files = files ?? new ScreenshotFiles();
        _capture = new FfmpegScreenshotCapture(_processRunner);
        _activeWindow = new ActiveWindowRegionReader(_processRunner);
        _desktop = new X11Desktop();
    }

    public async Task<Result<CapturedScreenshot>> CaptureAsync(
        ScreenshotPlan plan,
        CancellationToken ct = default)
    {
        if (plan is null)
            return Result<CapturedScreenshot>.Fail("Screenshot plan is required");

        if (plan.Pointer is null)
            return Result<CapturedScreenshot>.Fail("Screenshot pointer is required");

        if (plan.Source is null)
            return Result<CapturedScreenshot>.Fail("Screenshot source is required");

        var regionResult = plan.Source switch
        {
            ScreenshotSource.SelectedRegion selected => Result<ScreenRegion>.Ok(selected.Region),
            ScreenshotSource.InteractiveSelection => await new SlopScreenRegionSelector(_processRunner).SelectAsync(ct).ConfigureAwait(false),
            ScreenshotSource.FullScreen => _desktop.GetScreenSize().Match(
                size => ScreenRegion.Create(0, 0, size.Width, size.Height),
                Result<ScreenRegion>.Fail),
            ScreenshotSource.ActiveWindow => await _activeWindow.ReadAsync(ct).ConfigureAwait(false),
            _ => Result<ScreenRegion>.Fail($"Unknown screenshot source: {plan.Source.GetType().Name}")
        };
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
        var displayResult = X11DisplayName.Current();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var frame = new ResolvedScreenshotFrame(
            region,
            plan.Pointer,
            file,
            displayResult.GetValueOrThrow());

        var captureResult = await _capture.CaptureAsync(frame, ct).ConfigureAwait(false);
        return captureResult.Match(
            capturedFile => Result<CapturedScreenshot>.Ok(new CapturedScreenshot(capturedFile, region)),
            Result<CapturedScreenshot>.Fail);
    }
}
