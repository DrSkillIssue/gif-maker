using GifMaker.Core;

namespace GifMaker.Screenshot;

public abstract record ScreenshotCapture
{
    private ScreenshotCapture(ScreenPointerCapture pointer, ScreenshotDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Pointer = pointer;
        Destination = destination;
    }

    public ScreenPointerCapture Pointer { get; }

    public ScreenshotDestination Destination { get; }

    public sealed record Area : ScreenshotCapture
    {
        public Area(ScreenRegion region, ScreenPointerCapture pointer, ScreenshotDestination destination)
            : base(pointer, destination)
        {
            Region = region;
        }

        public ScreenRegion Region { get; }
    }

    public sealed record FullScreen : ScreenshotCapture
    {
        public FullScreen(ScreenPointerCapture pointer, ScreenshotDestination destination)
            : base(pointer, destination)
        {
        }
    }

    public sealed record ActiveWindow : ScreenshotCapture
    {
        public ActiveWindow(ScreenPointerCapture pointer, ScreenshotDestination destination)
            : base(pointer, destination)
        {
        }
    }
}

/// <summary>
/// Destination for a captured screenshot.
/// </summary>
public abstract record ScreenshotDestination
{
    private ScreenshotDestination() { }

    public static ScreenshotDestination Default { get; } = new DefaultFile();

    public static ScreenshotDestination FromCliPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Default : new File(path);

    public sealed record DefaultFile : ScreenshotDestination;

    public sealed record Directory(string Path) : ScreenshotDestination;

    public sealed record File(string Path) : ScreenshotDestination;
}

public readonly record struct ScreenshotFile(string Path);

public readonly record struct CapturedScreenshot(ScreenshotFile File, ScreenRegion Region);

public sealed class FrozenScreenshot : IDisposable
{
    public FrozenScreenshot(ScreenRegion bounds, Cairo.ImageSurface image)
    {
        ArgumentNullException.ThrowIfNull(image);
        Bounds = bounds;
        Image = image;
    }

    public ScreenRegion Bounds { get; }

    internal Cairo.ImageSurface Image { get; }

    public void Dispose()
    {
        Image.Dispose();
    }
}

public readonly record struct FrozenScreenshotCrop(
    FrozenScreenshot Frame,
    ScreenRegion Region,
    ScreenshotDestination Destination);
