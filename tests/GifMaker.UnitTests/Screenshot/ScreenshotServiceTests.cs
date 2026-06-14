using GifMaker.Core;
using GifMaker.Screenshot;

namespace GifMaker.UnitTests.Screenshot;

public sealed class ScreenshotServiceTests
{
    [Fact]
    public async Task CaptureAsyncRejectsNullCapture()
    {
        var result = await new ScreenshotService().CaptureAsync(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal("Screenshot capture is required", result.Match(_ => "", error => error));
    }

    [Fact]
    public async Task CaptureAsyncReturnsActiveWindowFailure()
    {
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue((_, _) => Task.FromResult(new ProcessResult(1, "", "")));
        var capture = new ScreenshotCapture.ActiveWindow(ScreenPointerCapture.Excluded, ScreenshotDestination.Default);

        var result = await new ScreenshotService(runner).CaptureAsync(capture);

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to get active window (is xdotool installed?)", result.Match(_ => "", error => error));
    }

    [Fact]
    public void CropRejectsNullFrozenScreenshot()
    {
        var region = ScreenRegion.Create(0, 0, 10, 10).GetValueOrThrow();
        var crop = new FrozenScreenshotCrop(null!, region, ScreenshotDestination.Default);

        var result = new ScreenshotService().Crop(crop);

        Assert.False(result.IsSuccess);
        Assert.Equal("Frozen screenshot is required", result.Match(_ => "", error => error));
    }

    [Fact]
    public void CropRejectsRegionOutsideFrozenFrame()
    {
        using var frame = CreateFrozenScreenshot(100, 100);
        var region = ScreenRegion.Create(90, 90, 20, 20).GetValueOrThrow();
        var crop = new FrozenScreenshotCrop(frame, region, ScreenshotDestination.Default);

        var result = new ScreenshotService().Crop(crop);

        Assert.False(result.IsSuccess);
        Assert.Equal("Selected region is outside the frozen screenshot", result.Match(_ => "", error => error));
    }

    [Fact]
    public void CropWritesPngForValidRegion()
    {
        using var directory = new TemporaryDirectory();
        using var frame = CreateFrozenScreenshot(100, 100);
        var region = ScreenRegion.Create(10, 20, 30, 40).GetValueOrThrow();
        var outputFile = Path.Combine(directory.Path, "crop.png");
        var crop = new FrozenScreenshotCrop(frame, region, new ScreenshotDestination.File(outputFile));

        var result = new ScreenshotService().Crop(crop);

        Assert.True(result.IsSuccess, result.Match(_ => "", error => error));
        Assert.Equal(outputFile, result.GetValueOrThrow().File.Path);
        Assert.True(new FileInfo(outputFile).Length > 0);
    }

    [Fact]
    public void CropReturnsFailureForUnwritableDestination()
    {
        using var directory = new TemporaryDirectory();
        using var frame = CreateFrozenScreenshot(100, 100);
        var region = ScreenRegion.Create(10, 20, 30, 40).GetValueOrThrow();
        var crop = new FrozenScreenshotCrop(frame, region, new ScreenshotDestination.File(directory.Path));

        var result = new ScreenshotService().Crop(crop);

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to crop screenshot", result.Match(_ => "", error => error), StringComparison.Ordinal);
    }

    private static FrozenScreenshot CreateFrozenScreenshot(int width, int height)
    {
        Cairo.Module.Initialize();
        GdkPixbuf.Module.Initialize();
        Gdk.Module.Initialize();

        var bounds = ScreenRegion.Create(0, 0, width, height).GetValueOrThrow();
        var surface = new Cairo.ImageSurface(Cairo.Format.Rgb24, width, height);
        return new FrozenScreenshot(bounds, surface);
    }
}
