using GifMaker.Core;

namespace GifMaker.Screenshot;

/// <summary>
/// Source of a screenshot region before capture.
/// </summary>
public abstract record ScreenshotSource
{
    private ScreenshotSource() { }

    public sealed record SelectedRegion(ScreenRegion Region) : ScreenshotSource;

    public sealed record InteractiveSelection : ScreenshotSource;

    public sealed record FullScreen : ScreenshotSource;

    public sealed record ActiveWindow : ScreenshotSource;
}

public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// Pointer rendering requested at the screenshot capture boundary.
/// </summary>
public abstract record ScreenshotPointer
{
    private ScreenshotPointer() { }

    public sealed record Excluded : ScreenshotPointer;

    public sealed record Live : ScreenshotPointer;

    public sealed record FrozenAt(ScreenPoint Position) : ScreenshotPointer;
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

public sealed record ScreenshotPlan(
    ScreenshotSource Source,
    ScreenshotPointer Pointer,
    ScreenshotDestination Destination);

public readonly record struct ScreenshotFile(string Path);

public readonly record struct CapturedScreenshot(ScreenshotFile File, ScreenRegion Region);
