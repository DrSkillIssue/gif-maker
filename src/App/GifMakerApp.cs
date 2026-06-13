using System.Runtime.Versioning;
using GifMaker.Core;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Main GTK application. Single window design with tabbed interface.
/// Runs in background with system tray icon for quick access.
/// Hotkeys for recording and screenshots work even when minimized.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GifMakerApp : IDisposable
{
    private const string AppId = "com.gifmaker.app";

    private readonly Gtk.Application _app;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private readonly ILogger<GifMakerApp> _logger;

    private GlobalHotkey? _hotkey;
    private MainWindow? _mainWindow;
    private StatusNotifierTray? _trayIcon;
    private volatile Thread? _hotkeyThread;
    private int _disposed;

    private GifMakerApp(ILogger<GifMakerApp>? logger)
    {
        _logger = logger ?? NullLogger<GifMakerApp>.Instance;
        _app = Gtk.Application.New(AppId, Gio.ApplicationFlags.FlagsNone);
        _app.OnActivate += OnActivate;
    }

    /// <summary>
    /// Creates a new GifMakerApp instance.
    /// </summary>
    public static GifMakerApp Create(ILogger<GifMakerApp>? logger = null) => new(logger);

    /// <summary>
    /// Runs the application. Blocks until exit.
    /// </summary>
    public int Run()
    {
        ThrowIfDisposed();

        // Initialize hotkeys AFTER GTK to avoid GDK type registration conflicts
        try
        {
            _hotkey = new GlobalHotkey();

            // Register global hotkeys (best effort - may fail if already grabbed)
            if (!_hotkey.Register(HotkeyAction.SelectArea))
                _logger.LogWarning("Failed to register Ctrl+Alt+S hotkey");

            if (!_hotkey.Register(HotkeyAction.Screenshot))
                _logger.LogWarning("Failed to register Print hotkey");

            _hotkeyThread = new Thread(HotkeyLoop) { IsBackground = true, Name = "GifMaker-Hotkey" };
            _hotkeyThread.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize global hotkeys");
        }

        return _app.RunWithSynchronizationContext(null);
    }

    private void OnActivate(object sender, EventArgs e)
    {
        lock (_lock)
        {
            if (IsDisposed) return;

            // Create tray icon on first activation
            if (_trayIcon is null)
            {
                try
                {
                    _trayIcon = new StatusNotifierTray(_app);
                    _trayIcon.Activated += OnTrayShowWindow;
                    _trayIcon.SecondaryActivated += OnTrayScreenshot;
                    _logger.LogDebug("System tray icon created");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create tray icon - app will quit on window close");
                }
            }

            // Create or show main window
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow(_app, logger: null);
                // Override close behavior: minimize to tray if tray available
                _mainWindow.OnCloseRequest += OnWindowCloseRequest;
            }

            _mainWindow.Present();
        }
    }

    private bool OnWindowCloseRequest(Gtk.Window sender, EventArgs args)
    {
        lock (_lock)
        {
            if (_trayIcon is not null)
            {
                // Hide to tray instead of quitting
                _mainWindow?.Hide();
                _logger.LogDebug("Window hidden to tray");
                return true; // Prevent default close
            }

            // No tray - actually quit
            _mainWindow?.Destroy();
            _mainWindow = null;
            _app.Quit();
            return true;
        }
    }

    private void OnTrayShowWindow()
    {
        lock (_lock)
        {
            if (_mainWindow is not null)
            {
                _mainWindow.Present();
            }
            else
            {
                // Re-create window if destroyed
                _app.Activate();
            }
        }
    }

    private void OnTrayScreenshot()
    {
        lock (_lock)
        {
            _mainWindow?.TriggerScreenshotSelection();
        }
    }

    private void HotkeyLoop()
    {
        var ct = _cts.Token;
        var hotkey = _hotkey;
        if (hotkey is null) return;

        while (!ct.IsCancellationRequested)
        {
            var action = hotkey.WaitForHotkey(ct);
            if (ct.IsCancellationRequested) break;

            // Hotkeys bring window to front and switch to appropriate tab
            // The actual selection/capture is done through the UI
            switch (action)
            {
                case HotkeyAction.SelectArea:
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        lock (_lock)
                        {
                            if (_mainWindow is { } win)
                            {
                                win.SwitchToRecord();
                                win.Present();
                            }
                        }
                        return false;
                    });
                    break;

                case HotkeyAction.Screenshot:
                    GLib.Functions.IdleAdd(0, () =>
                    {
                        lock (_lock)
                        {
                            _mainWindow?.TriggerScreenshotSelection();
                        }
                        return false;
                    });
                    break;
            }
        }
    }

    private bool IsDisposed => _disposed != 0;
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();

        // Wait for hotkey thread to exit
        var thread = _hotkeyThread;
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _trayIcon?.Dispose();
        _hotkey?.Dispose();
        _cts.Dispose();
    }
}
