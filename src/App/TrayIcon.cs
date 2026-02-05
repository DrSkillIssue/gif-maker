using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static GifMaker.X11.AppIndicatorInterop;

namespace GifMaker.App;

/// <summary>
/// System tray icon using AppIndicator3 with GTK3 menu.
/// Provides menu for quick access to recording/screenshot.
/// </summary>
/// <remarks>
/// Uses GTK3 menu (via P/Invoke) because AppIndicator3 requires GTK3 menus.
/// GTK4's Gio.Menu is not compatible with AppIndicator.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class TrayIcon : IDisposable
{
    private const string AppId = "gifmaker";
    private const string DefaultIcon = "camera-video";

    private readonly nint _indicator;
    private readonly nint _menu;

    // Must prevent GC of delegates while in use
    private readonly ActivateCallback _showCallback;
    private readonly ActivateCallback _screenshotCallback;
    private readonly ActivateCallback _recordCallback;
    private readonly ActivateCallback _quitCallback;

    private int _disposed;

    /// <summary>Fired when user clicks "Show Window".</summary>
    public event Action? ShowWindowRequested;

    /// <summary>Fired when user clicks "Take Screenshot".</summary>
    public event Action? ScreenshotRequested;

    /// <summary>Fired when user clicks "Start Recording".</summary>
    public event Action? RecordRequested;

    /// <summary>Fired when user clicks "Quit".</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Creates and shows the system tray icon.
    /// </summary>
    /// <exception cref="InvalidOperationException">Failed to create AppIndicator.</exception>
    public TrayIcon()
    {
        _indicator = AppIndicatorNew(AppId, DefaultIcon, CategoryApplicationStatus);
        if (_indicator == nint.Zero)
            throw new InvalidOperationException(
                "Failed to create AppIndicator. Install: sudo apt install libayatana-appindicator3-1");

        // Create delegates and prevent GC
        _showCallback = OnShowActivated;
        _screenshotCallback = OnScreenshotActivated;
        _recordCallback = OnRecordActivated;
        _quitCallback = OnQuitActivated;

        _menu = CreateMenu();

        AppIndicatorSetMenu(_indicator, _menu);
        AppIndicatorSetTitle(_indicator, "GifMaker - Screen Recorder");
        AppIndicatorSetStatus(_indicator, StatusActive);
    }

    private nint CreateMenu()
    {
        var menu = GtkMenuNew();

        // Show Window
        var showItem = GtkMenuItemNewWithLabel("Show Window");
        ConnectActivate(showItem, _showCallback);
        GtkMenuShellAppend(menu, showItem);

        // Separator
        GtkMenuShellAppend(menu, GtkSeparatorMenuItemNew());

        // Screenshot (Print)
        var screenshotItem = GtkMenuItemNewWithLabel("Take Screenshot (Print)");
        ConnectActivate(screenshotItem, _screenshotCallback);
        GtkMenuShellAppend(menu, screenshotItem);

        // Record (Ctrl+Alt+S)
        var recordItem = GtkMenuItemNewWithLabel("Start Recording (Ctrl+Alt+S)");
        ConnectActivate(recordItem, _recordCallback);
        GtkMenuShellAppend(menu, recordItem);

        // Separator
        GtkMenuShellAppend(menu, GtkSeparatorMenuItemNew());

        // Quit
        var quitItem = GtkMenuItemNewWithLabel("Quit");
        ConnectActivate(quitItem, _quitCallback);
        GtkMenuShellAppend(menu, quitItem);

        GtkWidgetShowAll(menu);
        return menu;
    }

    private static void ConnectActivate(nint widget, ActivateCallback callback)
    {
        var ptr = Marshal.GetFunctionPointerForDelegate(callback);
        GSignalConnectData(widget, "activate", ptr, nint.Zero, nint.Zero, 0);
    }

    private void OnShowActivated(nint widget, nint userData) =>
        UiThread.Run(() => ShowWindowRequested?.Invoke());

    private void OnScreenshotActivated(nint widget, nint userData) =>
        UiThread.Run(() => ScreenshotRequested?.Invoke());

    private void OnRecordActivated(nint widget, nint userData) =>
        UiThread.Run(() => RecordRequested?.Invoke());

    private void OnQuitActivated(nint widget, nint userData) =>
        UiThread.Run(() => QuitRequested?.Invoke());

    /// <summary>
    /// Sets the tray icon to indicate recording is in progress.
    /// </summary>
    public void SetRecording(bool isRecording)
    {
        if (_disposed != 0) return;

        if (isRecording)
        {
            AppIndicatorSetIcon(_indicator, "media-record");
            AppIndicatorSetStatus(_indicator, StatusAttention);
        }
        else
        {
            AppIndicatorSetIcon(_indicator, DefaultIcon);
            AppIndicatorSetStatus(_indicator, StatusActive);
        }
    }

    /// <summary>
    /// Hides the tray icon.
    /// </summary>
    public void Hide()
    {
        if (_disposed != 0) return;
        AppIndicatorSetStatus(_indicator, StatusPassive);
    }

    /// <summary>
    /// Shows the tray icon.
    /// </summary>
    public void Show()
    {
        if (_disposed != 0) return;
        AppIndicatorSetStatus(_indicator, StatusActive);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        AppIndicatorSetStatus(_indicator, StatusPassive);
        // AppIndicator/GTK3 objects are reference counted and cleaned up by GLib
    }
}
