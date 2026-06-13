using System.Runtime.Versioning;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

[SupportedOSPlatform("linux")]
internal sealed class GifMakerShell : IDisposable
{
    private readonly Gtk.Application _app;
    private readonly AppServices _services;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _lock = new();
    private readonly ILogger<GifMakerShell> _logger;

    private MainWindow? _mainWindow;
    private StatusNotifierTray? _tray;
    private GlobalHotkey? _hotkey;
    private Thread? _hotkeyThread;
    private int _disposed;

    public GifMakerShell(
        Gtk.Application app,
        AppServices services,
        ILogger<GifMakerShell>? logger = null)
    {
        _app = app;
        _services = services;
        _logger = logger ?? NullLogger<GifMakerShell>.Instance;
        _app.OnActivate += OnActivate;
        RegisterActions(_app);
        RegisterGlobalHotkeys(_cts.Token);
    }

    public void Dispatch(AppAction action)
    {
        lock (_lock)
        {
            if (_disposed != 0)
                return;

            switch (action)
            {
                case AppAction.ShowWindow:
                    EnsureWindow().Present();
                    break;

                case AppAction.ShowRecord:
                    {
                        var window = EnsureWindow();
                        window.Render(new AppViewState.ActiveTab(AppTab.Record));
                        window.Present();
                    }
                    break;

                case AppAction.ShowScreenshot:
                    {
                        var window = EnsureWindow();
                        window.Render(new AppViewState.ActiveTab(AppTab.Screenshot));
                        window.Present();
                    }
                    break;

                case AppAction.CaptureSelection:
                    {
                        var window = EnsureWindow();
                        window.Render(new AppViewState.ActiveTab(AppTab.Screenshot));
                        window.Present();
                        window.CaptureSelection();
                    }
                    break;

                case AppAction.Quit:
                    _tray?.Dispose();
                    _tray = null;
                    _mainWindow?.Destroy();
                    _mainWindow = null;
                    _app.Quit();
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled app action: {action.GetType().Name}");
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();

        var hotkeyThread = _hotkeyThread;
        if (hotkeyThread is not null && hotkeyThread != Thread.CurrentThread)
            hotkeyThread.Join(TimeSpan.FromSeconds(2));

        _tray?.Dispose();
        _hotkey?.Dispose();
        _cts.Dispose();
    }

    private MainWindow EnsureWindow()
    {
        if (_mainWindow is not null)
            return _mainWindow;

        _mainWindow = new MainWindow(_app, _services);
        _mainWindow.OnCloseRequest += OnWindowCloseRequest;
        return _mainWindow;
    }

    private void OnActivate(object sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_disposed != 0)
                return;

            if (_tray is null)
            {
                try
                {
                    _tray = new StatusNotifierTray(_app, Dispatch);
                    _logger.LogDebug("System tray icon created");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogWarning(ex, "Failed to create tray icon - app will quit on window close");
                }
            }

            EnsureWindow().Present();
        }
    }

    private bool OnWindowCloseRequest(Gtk.Window sender, EventArgs args)
    {
        lock (_lock)
        {
            if (_tray is not null)
            {
                _mainWindow?.Hide();
                _logger.LogDebug("Window hidden to tray");
                return true;
            }

            _mainWindow?.Destroy();
            _mainWindow = null;
            _app.Quit();
            return true;
        }
    }

    private void RegisterActions(Gtk.Application app)
    {
        try
        {
            AddAction(app, "show-window", new AppAction.ShowWindow());
            AddAction(app, "show-record", new AppAction.ShowRecord());
            AddAction(app, "show-screenshot", new AppAction.ShowScreenshot());
            AddAction(app, "capture-selection", new AppAction.CaptureSelection());
            AddAction(app, "quit", new AppAction.Quit());
            app.SetAccelsForAction("app.capture-selection", ["Print"]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to register GTK application actions");
        }
    }

    private void AddAction(Gtk.Application app, string name, AppAction appAction)
    {
        var action = Gio.SimpleAction.New(name, null);
        action.OnActivate += (_, _) => Dispatch(appAction);
        app.AddAction(action);
    }

    private void RegisterGlobalHotkeys(CancellationToken ct)
    {
        try
        {
            _hotkey = new GlobalHotkey();

            if (!_hotkey.Register(HotkeyAction.SelectArea))
                _logger.LogWarning("Failed to register Ctrl+Alt+S hotkey");

            if (!_hotkey.Register(HotkeyAction.Screenshot))
                _logger.LogWarning("Failed to register Print hotkey");

            _hotkeyThread = new Thread(() => ReadGlobalHotkeys(ct))
            {
                IsBackground = true,
                Name = "GifMaker-Hotkey"
            };
            _hotkeyThread.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to initialize global hotkeys");
        }
    }

    private void ReadGlobalHotkeys(CancellationToken ct)
    {
        var hotkey = _hotkey;
        if (hotkey is null)
            return;

        while (!ct.IsCancellationRequested)
        {
            var action = hotkey.WaitForHotkey(ct);
            if (ct.IsCancellationRequested)
                break;

            AppAction? appAction = action switch
            {
                HotkeyAction.SelectArea => new AppAction.ShowRecord(),
                HotkeyAction.Screenshot => new AppAction.CaptureSelection(),
                _ => null
            };

            if (appAction is null)
                continue;

            GLib.Functions.IdleAdd(0, () =>
            {
                Dispatch(appAction);
                return false;
            });
        }
    }

}
