using GifMaker.Core;

namespace GifMaker.Screenshot;

public abstract record ScreenshotCapture
{
    private ScreenshotCapture() { }

    public sealed record Area(
        ScreenRegion Region,
        ScreenshotPointer Pointer,
        ScreenshotDestination Destination) : ScreenshotCapture;

    public sealed record FullScreen(
        ScreenshotPointer Pointer,
        ScreenshotDestination Destination) : ScreenshotCapture;

    public sealed record ActiveWindow(
        ScreenshotPointer Pointer,
        ScreenshotDestination Destination) : ScreenshotCapture;
}

public enum ScreenshotPointer
{
    Excluded,
    Included
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
    public FrozenScreenshot(ScreenshotFile file, ScreenRegion bounds, GdkPixbuf.Pixbuf image)
    {
        ArgumentNullException.ThrowIfNull(image);
        File = file;
        Bounds = bounds;
        Image = image;
    }

    public ScreenshotFile File { get; }

    public ScreenRegion Bounds { get; }

    internal GdkPixbuf.Pixbuf Image { get; }

    public void Dispose()
    {
        Image.Dispose();

        try
        {
            if (System.IO.File.Exists(File.Path))
                System.IO.File.Delete(File.Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public readonly record struct FrozenScreenshotCrop(
    FrozenScreenshot Frame,
    ScreenRegion Region,
    ScreenshotDestination Destination);
