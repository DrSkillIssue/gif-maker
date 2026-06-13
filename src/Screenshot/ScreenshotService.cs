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
    private static readonly string[] PngOptionKeys = ["compression"];
    private static readonly string[] PngOptionValues = ["1"];

    private readonly ScreenshotFiles _files;
    private readonly ActiveWindowRegionReader _activeWindow;
    private readonly X11Desktop _desktop;

    public ScreenshotService(
        IProcessRunner? processRunner = null,
        ScreenshotFiles? files = null)
    {
        var runner = processRunner ?? ProcessRunner.Default;
        _files = files ?? new ScreenshotFiles();
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

        var fileResult = _files.Reserve(capture.Destination);
        if (!fileResult.IsSuccess)
            return fileResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        var region = regionResult.GetValueOrThrow();
        var file = fileResult.GetValueOrThrow();
        var imageResult = await Task.Run(
            () => _desktop.CaptureRootImage(region, capture.Pointer),
            ct).ConfigureAwait(false);
        if (!imageResult.IsSuccess)
            return imageResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<CapturedScreenshot>.Fail);

        using var image = imageResult.GetValueOrThrow();
        try
        {
            image.Flush();
            using var pixbuf = Gdk.Functions.PixbufGetFromSurface(image, 0, 0, image.Width, image.Height);
            if (pixbuf is null || !pixbuf.Savev(file.Path, "png", PngOptionKeys, PngOptionValues))
                return Result<CapturedScreenshot>.Fail("Failed to save screenshot");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<CapturedScreenshot>.Fail($"Failed to save screenshot: {ex.Message}");
        }

        return Result<CapturedScreenshot>.Ok(new CapturedScreenshot(file, region));
    }

    public async Task<Result<FrozenScreenshot>> FreezeScreenAsync(
        ScreenPointerCapture pointer,
        CancellationToken ct = default)
    {
        var screenResult = _desktop.GetScreenSize().Match(
            size => ScreenRegion.Create(0, 0, size.Width, size.Height),
            Result<ScreenRegion>.Fail);
        if (!screenResult.IsSuccess)
            return screenResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<FrozenScreenshot>.Fail);

        var bounds = screenResult.GetValueOrThrow();
        var imageResult = await Task.Run(
            () => _desktop.CaptureRootImage(bounds, pointer),
            ct).ConfigureAwait(false);
        return imageResult.Match(
            image => Result<FrozenScreenshot>.Ok(new FrozenScreenshot(bounds, image)),
            Result<FrozenScreenshot>.Fail);
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
            crop.Frame.Image.Flush();
            using var pixbuf = Gdk.Functions.PixbufGetFromSurface(
                crop.Frame.Image,
                cropX,
                cropY,
                crop.Region.Width,
                crop.Region.Height);
            if (pixbuf is null || !pixbuf.Savev(file.Path, "png", PngOptionKeys, PngOptionValues))
                return Result<CapturedScreenshot>.Fail("Failed to crop screenshot");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<CapturedScreenshot>.Fail($"Failed to crop screenshot: {ex.Message}");
        }

        return Result<CapturedScreenshot>.Ok(new CapturedScreenshot(file, crop.Region));
    }
}
