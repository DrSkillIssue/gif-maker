using GifMaker.Conversion;
using GifMaker.Core;
using GifMaker.Screenshot;

namespace GifMaker.App;

internal sealed record AppServices(
    Func<RecordingSession> CreateRecordingSession,
    Func<ScreenshotSession> CreateScreenshotSession,
    DesktopFileActions DesktopFiles);

public abstract record AppAction
{
    private AppAction() { }

    public sealed record ShowWindow : AppAction;
    public sealed record ShowRecord : AppAction;
    public sealed record ShowScreenshot : AppAction;
    public sealed record CaptureSelection : AppAction;
    public sealed record Quit : AppAction;
}

public enum AppTab
{
    Record,
    Screenshot
}

public abstract record AppViewState
{
    private AppViewState() { }

    public sealed record ActiveTab(AppTab Tab) : AppViewState;
}

public readonly struct WindowVisibilityLease : IDisposable
{
    private readonly Action? _restore;

    internal WindowVisibilityLease(Action restore) => _restore = restore;

    public void Dispose() => _restore?.Invoke();
}

public sealed record RecordStartOptions(int Fps);

public sealed record RecordExportTarget(ConversionFormat Format, string? OutputDirectory);

public abstract record RecordIntent
{
    private RecordIntent() { }

    public sealed record ToggleRecording : RecordIntent;
    public sealed record CancelConversion : RecordIntent;
    public sealed record OpenSaved : RecordIntent;
    public sealed record CopySaved : RecordIntent;
}

public abstract record RecordViewState
{
    private RecordViewState() { }

    public sealed record Idle : RecordViewState;
    public sealed record Selecting : RecordViewState;
    public sealed record Recording(int Fps) : RecordViewState;
    public sealed record Stopping(int Fps) : RecordViewState;
    public sealed record Converting : RecordViewState;
    public sealed record Saved(SavedMedia Media) : RecordViewState;
    public sealed record Error(string Message) : RecordViewState;
}

public abstract record ScreenshotIntent
{
    private ScreenshotIntent() { }

    public sealed record CaptureSelection(ScreenPointerCapture Pointer) : ScreenshotIntent;
    public sealed record CaptureScreen(ScreenPointerCapture Pointer) : ScreenshotIntent;
    public sealed record CaptureWindow(ScreenPointerCapture Pointer) : ScreenshotIntent;
    public sealed record OpenSaved : ScreenshotIntent;
    public sealed record OpenContainingFolder : ScreenshotIntent;
    public sealed record CopySaved : ScreenshotIntent;
    public sealed record Reset : ScreenshotIntent;
}

public abstract record ScreenshotViewState
{
    private ScreenshotViewState() { }

    public sealed record Idle : ScreenshotViewState;
    public sealed record Selecting : ScreenshotViewState;
    public sealed record Capturing(ScreenshotCaptureKind Kind) : ScreenshotViewState;
    public sealed record Saved(SavedMedia Media, ScreenRegion Region, Gdk.Texture? Preview) : ScreenshotViewState;
    public sealed record Error(string Message) : ScreenshotViewState;
}

public readonly record struct SavedMedia(string Path);

public enum ScreenshotCaptureKind
{
    Selection,
    Screen,
    Window
}
