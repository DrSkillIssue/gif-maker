using GifMaker.Core;
using GifMaker.Screenshot;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Main GTK application. Runs listening for hotkeys.
/// Pressing hotkey triggers X11 area selection, then shows GTK record window.
/// </summary>
public sealed class GifMakerApp : IDisposable
{
    private const string AppId = "com.gifmaker.app";
    private const uint WindowHideDelayMs = 100;

    private readonly Gtk.Application _app;
    private readonly GlobalHotkey _hotkey;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private readonly ILogger<GifMakerApp> _logger;

    private Gtk.ApplicationWindow? _mainWindow;
    private volatile Thread? _hotkeyThread; // volatile: written in Run(), read in Dispose() from different threads
    private int _disposed; // 0 = not disposed, 1 = disposed (for Interlocked)

    private GifMakerApp(ILogger<GifMakerApp>? logger)
    {
        _logger = logger ?? NullLogger<GifMakerApp>.Instance;
        _app = Gtk.Application.New(AppId, Gio.ApplicationFlags.FlagsNone);
        _hotkey = new GlobalHotkey();
        _app.OnActivate += OnActivate;
    }

    /// <summary>
    /// Creates a new GifMakerApp instance.
    /// </summary>
    public static GifMakerApp Create(ILogger<GifMakerApp>? logger = null) => new(logger);

    /// <summary>
    /// Runs the application. Blocks until the application exits.
    /// Caller must dispose after Run() returns.
    /// </summary>
    /// <returns>Exit code from GTK application.</returns>
    public int Run()
    {
        ThrowIfDisposed();

        if (!_hotkey.Register(HotkeyAction.SelectArea))
        {
            _logger.LogWarning("Failed to register recording hotkey - another instance may be running");
        }

        if (!_hotkey.Register(HotkeyAction.Screenshot))
        {
            _logger.LogWarning("Failed to register screenshot hotkey (Print key)");
        }

        _hotkeyThread = new Thread(HotkeyLoop) { IsBackground = true, Name = "GifMaker-Hotkey" };
        _hotkeyThread.Start();

        return _app.RunWithSynchronizationContext(null);
    }

    private void OnActivate(object sender, EventArgs e)
    {
        lock (_lock)
        {
            if (IsDisposed || _mainWindow is not null) return;

            _mainWindow = CreateMainWindow();
            _mainWindow.Present();
        }
    }

    private Gtk.ApplicationWindow CreateMainWindow()
    {
        var window = Gtk.ApplicationWindow.New(_app);
        window.Title = "GIF Maker";
        window.SetDefaultSize(280, 180);

        // Quit app when window is closed
        window.OnCloseRequest += (_, _) =>
        {
            _app.Quit();
            return true; // handled
        };

        // Center on primary monitor
        CenterWindowOnPrimaryMonitor(window, 280, 180);

        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        box.MarginTop = 16;
        box.MarginBottom = 16;
        box.MarginStart = 16;
        box.MarginEnd = 16;

        var label = Gtk.Label.New("Ctrl+Alt+S to record | Print to screenshot");
        label.AddCssClass("dim-label");
        label.Wrap = true;

        var recordBtn = Gtk.Button.NewWithLabel("Record Area");
        recordBtn.AddCssClass("suggested-action");
        recordBtn.OnClicked += (_, _) => TriggerCaptureFromUi();

        var screenshotBtn = Gtk.Button.NewWithLabel("Screenshot");
        screenshotBtn.OnClicked += (_, _) => TriggerScreenshotFromUi();

        var quitBtn = Gtk.Button.NewWithLabel("Quit");
        quitBtn.OnClicked += (_, _) => _app.Quit();

        box.Append(label);
        box.Append(recordBtn);
        box.Append(screenshotBtn);
        box.Append(quitBtn);

        window.Child = box;
        return window;
    }

    /// <summary>
    /// Triggers screenshot from UI button - hides window first.
    /// </summary>
    private void TriggerScreenshotFromUi()
    {
        lock (_lock)
        {
            _mainWindow?.Hide();
        }

        GLib.Functions.TimeoutAdd(0, WindowHideDelayMs, () =>
        {
            ShowScreenshotOverlay();
            return false;
        });
    }

    /// <summary>
    /// Triggers capture from UI button - hides window first, re-shows on cancel.
    /// </summary>
    private void TriggerCaptureFromUi()
    {
        // Capture window reference under lock and hide atomically
        // to prevent race if window is nulled between capture and hide
        lock (_lock)
        {
            _mainWindow?.Hide();
        }

        // Delay to let window hide before selection starts
        GLib.Functions.TimeoutAdd(0, WindowHideDelayMs, () =>
        {
            PerformAreaSelection(showWindowOnCancel: true);
            return false;
        });
    }

    /// <summary>
    /// Performs area selection and handles the result.
    /// Called from background thread (HotkeyLoop) or GTK timeout callback.
    /// </summary>
    /// <param name="showWindowOnCancel">Whether to re-present the main window if selection is cancelled.</param>
    private async void PerformAreaSelection(bool showWindowOnCancel)
    {
        SelectionResult result;

        try
        {
            using var selector = new AreaSelector();
            var selectResult = await selector.SelectAsync().ConfigureAwait(false);

            result = selectResult.Match<SelectionResult>(
                rect => rect.IsValid ? new SelectionResult.Success(rect) : new SelectionResult.Cancelled(),
                error => error == "Selection cancelled"
                    ? new SelectionResult.Cancelled()
                    : new SelectionResult.Failed(new SelectionError(error))
            );
        }
        catch (Exception ex)
        {
            // Catch ALL exceptions in async void to prevent crashes
            var error = SelectionError.FromException(ex);
            _logger.LogError(ex, "Area selection failed: {DiagnosticDetails}", error.DiagnosticDetails);
            result = new SelectionResult.Failed(error);
        }

        // Schedule UI update on GTK main thread
        switch (result)
        {
            case SelectionResult.Success success:
                RunOnUiThread(() => ShowRecordWindow(success.Region));
                break;

            case SelectionResult.Cancelled when showWindowOnCancel:
            case SelectionResult.Failed when showWindowOnCancel:
                RunOnUiThread(PresentMainWindow);
                break;
        }

        if (result is SelectionResult.Failed failed)
        {
            _logger.LogWarning("Selection failed: {UserMessage}", failed.Error.UserMessage);
        }
    }

    private void HotkeyLoop()
    {
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            var action = _hotkey.WaitForHotkey(ct);

            if (ct.IsCancellationRequested) break;

            switch (action)
            {
                case HotkeyAction.SelectArea:
                    PerformAreaSelection(showWindowOnCancel: false);
                    break;

                case HotkeyAction.Screenshot:
                    RunOnUiThread(ShowScreenshotOverlay);
                    break;
            }
        }
    }

    /// <summary>
    /// Schedules an action to run on the GTK UI thread.
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            action();
            return false;
        });
    }

    private void PresentMainWindow()
    {
        Gtk.ApplicationWindow? window;
        lock (_lock)
        {
            window = _mainWindow;
        }
        window?.Present();
    }

    private void ShowRecordWindow(Rectangle region)
    {
        var recordWindow = new RecordWindow(_app, region, onClosed: PresentMainWindow);
        recordWindow.Present();
    }

    #region Screenshot

    private void ShowScreenshotOverlay()
    {
        var overlay = new ScreenshotOverlay(
            _app,
            onModeSelected: OnScreenshotModeSelected,
            onCancelled: PresentMainWindow);
        overlay.Present();
    }

    private void OnScreenshotModeSelected(CaptureMode mode, bool showPointer)
    {
        // Selection mode needs delay for overlay to fully close before slop starts
        if (mode == CaptureMode.Selection)
        {
            GLib.Functions.TimeoutAdd(0, WindowHideDelayMs, () =>
            {
                PerformScreenshotSelectionAsync(showPointer);
                return false;
            });
        }
        else
        {
            PerformScreenshotAsync(mode, showPointer);
        }
    }

    private async void PerformScreenshotSelectionAsync(bool showPointer)
    {
        // Reuse same area selection logic as recording
        SelectionResult result;

        try
        {
            using var selector = new AreaSelector();
            var selectResult = await selector.SelectAsync().ConfigureAwait(false);

            result = selectResult.Match<SelectionResult>(
                rect => rect.IsValid ? new SelectionResult.Success(rect) : new SelectionResult.Cancelled(),
                error => error == "Selection cancelled"
                    ? new SelectionResult.Cancelled()
                    : new SelectionResult.Failed(new SelectionError(error))
            );
        }
        catch (Exception ex)
        {
            var error = SelectionError.FromException(ex);
            _logger.LogError(ex, "Screenshot selection failed: {DiagnosticDetails}", error.DiagnosticDetails);
            result = new SelectionResult.Failed(error);
        }

        switch (result)
        {
            case SelectionResult.Success success:
                RunOnUiThread(() => CaptureAndShowScreenshot(success.Region, CaptureMode.Selection, showPointer));
                break;

            case SelectionResult.Cancelled:
            case SelectionResult.Failed:
                RunOnUiThread(PresentMainWindow);
                break;
        }
    }

    private async void PerformScreenshotAsync(CaptureMode mode, bool showPointer)
    {
        var service = new ScreenshotService(_logger);

        // Get region for the mode
        var regionResult = await service.GetRegionAsync(mode).ConfigureAwait(false);

        regionResult.Match(
            region => RunOnUiThread(() => CaptureAndShowScreenshot(region, mode, showPointer)),
            error =>
            {
                _logger.LogError("Failed to get region for {Mode}: {Error}", mode, error);
                RunOnUiThread(PresentMainWindow);
            });
    }

    private async void CaptureAndShowScreenshot(Rectangle region, CaptureMode mode, bool showPointer)
    {
        var service = new ScreenshotService(_logger);

        // Capture screenshot
        var captureResult = await service.CaptureAsync(region, mode, showPointer).ConfigureAwait(false);

        captureResult.Match(
            result =>
            {
                // Copy to clipboard
                var clipboardCopied = false;

                RunOnUiThread(() =>
                {
                    // Need a widget to get clipboard - create temp window or use main window
                    Gtk.ApplicationWindow? tempWindow = null;
                    lock (_lock)
                    {
                        if (_mainWindow is not null)
                        {
                            var copyResult = ClipboardService.CopyFileToClipboard(_mainWindow, result.FilePath, _logger);
                            clipboardCopied = copyResult.Success;
                        }
                        else
                        {
                            // Create temp hidden window for clipboard access
                            tempWindow = Gtk.ApplicationWindow.New(_app);
                            tempWindow.Hide();
                            var copyResult = ClipboardService.CopyFileToClipboard(tempWindow, result.FilePath, _logger);
                            clipboardCopied = copyResult.Success;
                        }
                    }

                    // Show preview window
                    var screenshotWindow = new ScreenshotWindow(
                        _app,
                        result,
                        clipboardCopied,
                        onClosed: PresentMainWindow,
                        _logger);
                    screenshotWindow.Present();

                    tempWindow?.Close();
                });
            },
            error =>
            {
                _logger.LogError("Screenshot capture failed: {Error}", error);
                RunOnUiThread(PresentMainWindow);
            });
    }

    #endregion

    /// <summary>
    /// Configures window to center on primary monitor when shown.
    /// Uses explicit handler references for proper cleanup and lifetime management.
    /// </summary>
    private void CenterWindowOnPrimaryMonitor(Gtk.Window window, int width, int height)
    {
        var display = Gdk.Display.GetDefault();
        if (display is null) return;

        var monitors = display.GetMonitors();
        if (monitors.GetNItems() == 0) return;

        // Get primary monitor (first one) - use pattern matching instead of `as` cast
        if (monitors.GetObject(0) is not Gdk.Monitor monitor) return;

        monitor.GetGeometry(out var geometry);
        var x = geometry.X + (geometry.Width - width) / 2;
        var y = geometry.Y + (geometry.Height - height) / 2;

        // Use named handlers to enable unsubscription and prevent leaks.
        // Capture logger to avoid capturing `this` (which would prevent GC of entire app).
        var logger = _logger;

        // OnShow is defined on Widget, not Window, so handler receives Widget.
        // We know the sender is the window we're subscribing to.
        GObject.SignalHandler<Gtk.Widget>? showHandler = null;
        showHandler = (sender, _) =>
        {
            // Unsubscribe immediately - positioning is one-shot
            if (showHandler is not null)
            {
                sender.OnShow -= showHandler;
            }

            // Position window on X11 (no-op on Wayland)
            // sender is guaranteed to be the Gtk.Window we subscribed to
            if (sender is Gtk.Window senderWindow)
            {
                TrySetWindowPositionX11(senderWindow, x, y, logger);
            }
        };

        window.OnShow += showHandler;
    }

    /// <summary>
    /// Attempts X11 window positioning. Pure function - no instance state dependency.
    /// </summary>
    private static void TrySetWindowPositionX11(Gtk.Window window, int x, int y, ILogger logger)
    {
        try
        {
            var surface = window.GetSurface();
            if (surface is null) return;

            var display = surface.GetDisplay();
            // Display name starting with ":" indicates X11 (e.g., ":0", ":1")
            if (display is null || !display.GetName().StartsWith(":")) return;

            GifMaker.X11.WindowPositioner.MoveWindow(surface, x, y);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Positioning is best-effort - log at debug level as it's expected to fail on Wayland
            logger.LogDebug(ex, "X11 window positioning failed (expected on Wayland)");
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();

        // Wait for hotkey thread to exit (bounded wait to avoid deadlock)
        _hotkeyThread?.Join(TimeSpan.FromSeconds(1));

        _cts.Dispose();
        _hotkey.Dispose();
        _app.Dispose();
    }
}
