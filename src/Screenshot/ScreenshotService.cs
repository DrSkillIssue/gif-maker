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
    private readonly ScreenshotFiles _files;
    private readonly FfmpegScreenshotCapture _capture;
    private readonly ActiveWindowRegionReader _activeWindow;
    private readonly X11Desktop _desktop;

    public ScreenshotService(
        IProcessRunner? processRunner = null,
        ScreenshotFiles? files = null)
    {
        var runner = processRunner ?? ProcessRunner.Default;
        _files = files ?? new ScreenshotFiles();
        _capture = new FfmpegScreenshotCapture(runner);
        _activeWindow = new ActiveWindowRegionReader(runner);
        _desktop = new X11Desktop();
    }

    public async Task<Result<CapturedScreenshot>> CaptureAsync(
        ScreenshotCapture capture,
        CancellationToken ct = default)
    {
        if (capture is null)
            return Result<CapturedScreenshot>.Fail("Screenshot capture is required");

        var regionResult = capture switch
        {
            ScreenshotCapture.Area area => Result<ScreenRegion>.Ok(area.Region),
            ScreenshotCapture.FullScreen => _desktop.GetScreenSize().Match(
                size => ScreenRegion.Create(0, 0, size.Width, size.Height),
                Result<ScreenRegion>.Fail),
            ScreenshotCapture.ActiveWindow => await _activeWindow.ReadAsync(ct).ConfigureAwait(false),
            _ => Result<ScreenRegion>.Fail($"Unknown screenshot capture: {capture.GetType().Name}")
        };
        if (!regionResult.IsSuccess)
            return regionResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var destination = capture switch
        {
            ScreenshotCapture.Area area => area.Destination,
            ScreenshotCapture.FullScreen screen => screen.Destination,
            ScreenshotCapture.ActiveWindow window => window.Destination,
            _ => throw new InvalidOperationException($"Unknown screenshot capture: {capture.GetType().Name}")
        };

        var fileResult = _files.Reserve(destination);
        if (!fileResult.IsSuccess)
            return fileResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var pointer = capture switch
        {
            ScreenshotCapture.Area area => area.Pointer,
            ScreenshotCapture.FullScreen screen => screen.Pointer,
            ScreenshotCapture.ActiveWindow window => window.Pointer,
            _ => throw new InvalidOperationException($"Unknown screenshot capture: {capture.GetType().Name}")
        };

        var region = regionResult.GetValueOrThrow();
        var file = fileResult.GetValueOrThrow();
        var displayResult = X11DisplayName.Current();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var frame = new ResolvedScreenshotFrame(
            region,
            pointer,
            file,
            displayResult.GetValueOrThrow());

        var captureResult = await _capture.CaptureAsync(frame, ct).ConfigureAwait(false);
        return captureResult.Match(
            capturedFile => Result<CapturedScreenshot>.Ok(new CapturedScreenshot(capturedFile, region)),
            Result<CapturedScreenshot>.Fail);
    }

    public async Task<Result<FrozenScreenshot>> FreezeScreenAsync(
        ScreenshotPointer pointer,
        CancellationToken ct = default)
    {
        var screenResult = _desktop.GetScreenSize().Match(
            size => ScreenRegion.Create(0, 0, size.Width, size.Height),
            Result<ScreenRegion>.Fail);
        if (!screenResult.IsSuccess)
            return screenResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<FrozenScreenshot>.Fail);

        var fileResult = _files.ReserveTemporary();
        if (!fileResult.IsSuccess)
            return fileResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<FrozenScreenshot>.Fail);

        var displayResult = X11DisplayName.Current();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<FrozenScreenshot>.Fail);

        var bounds = screenResult.GetValueOrThrow();
        var file = fileResult.GetValueOrThrow();
        var frame = new ResolvedScreenshotFrame(
            bounds,
            pointer,
            file,
            displayResult.GetValueOrThrow());

        var captureResult = await _capture.CaptureAsync(frame, ct).ConfigureAwait(false);
        if (!captureResult.IsSuccess)
        {
            try { System.IO.File.Delete(file.Path); }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException) { }

            return captureResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<FrozenScreenshot>.Fail);
        }

        try
        {
            var image = GdkPixbuf.Pixbuf.NewFromFile(file.Path);
            if (image is null)
            {
                try { System.IO.File.Delete(file.Path); }
                catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException) { }

                return Result<FrozenScreenshot>.Fail("Failed to load frozen screenshot");
            }

            return Result<FrozenScreenshot>.Ok(new FrozenScreenshot(file, bounds, image));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { System.IO.File.Delete(file.Path); }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException) { }

            return Result<FrozenScreenshot>.Fail($"Failed to load frozen screenshot: {ex.Message}");
        }
    }

    public Result<CapturedScreenshot> Crop(FrozenScreenshotCrop crop)
    {
        if (crop.Frame is null)
            return Result<CapturedScreenshot>.Fail("Frozen screenshot is required");

        var fileResult = _files.Reserve(crop.Destination);
        if (!fileResult.IsSuccess)
            return fileResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var cropX = crop.Region.X - crop.Frame.Bounds.X;
        var cropY = crop.Region.Y - crop.Frame.Bounds.Y;
        if (cropX < 0 ||
            cropY < 0 ||
            cropX + crop.Region.Width > crop.Frame.Bounds.Width ||
            cropY + crop.Region.Height > crop.Frame.Bounds.Height)
        {
            return Result<CapturedScreenshot>.Fail("Selected region is outside the frozen screenshot");
        }

        var file = fileResult.GetValueOrThrow();
        try
        {
            using var cropped = crop.Frame.Image.NewSubpixbuf(
                cropX,
                cropY,
                crop.Region.Width,
                crop.Region.Height);
            if (!cropped.Savev(file.Path, "png", [], []))
                return Result<CapturedScreenshot>.Fail("Failed to save cropped screenshot");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<CapturedScreenshot>.Fail($"Failed to crop screenshot: {ex.Message}");
        }

        return Result<CapturedScreenshot>.Ok(new CapturedScreenshot(file, crop.Region));
    }
}
