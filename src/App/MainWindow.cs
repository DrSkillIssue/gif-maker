using System.Runtime.Versioning;
using Gtk;
using GifMaker.Core;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Main application window with tabbed interface for Record and Screenshot.
/// Single window design - no popup windows, all results shown inline.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class MainWindow : Window
{
    private const int WindowWidth = 420;
    private const int WindowHeight = 350;

    private readonly Application _app;
    private readonly ILogger<MainWindow> _logger;
    private readonly IProcessRunner _processRunner;

    private readonly Stack _stack;
    private readonly RecordPage _recordPage;
    private readonly ScreenshotPage _screenshotPage;

    private (int X, int Y)? _savedPosition;

    /// <summary>
    /// Creates the main application window.
    /// </summary>
    public MainWindow(
        Application app,
        ILogger<MainWindow>? logger = null,
        IProcessRunner? processRunner = null)
    {
        _app = app;
        _logger = logger ?? NullLogger<MainWindow>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;

        Application = app;
        Title = "GIF Maker";
        SetDefaultSize(WindowWidth, WindowHeight);
        Resizable = true;

        // Header bar with tab switcher
        var headerBar = HeaderBar.New();
        var stackSwitcher = StackSwitcher.New();
        headerBar.SetTitleWidget(stackSwitcher);
        SetTitlebar(headerBar);

        // Stack for tab content
        _stack = Stack.New();
        _stack.SetTransitionType(StackTransitionType.SlideLeftRight);
        _stack.SetTransitionDuration(200);
        stackSwitcher.SetStack(_stack);

        // Create pages (each manages its own typed logger)
        _recordPage = new RecordPage(logger: null, _processRunner);
        _screenshotPage = new ScreenshotPage(logger: null, _processRunner);

        _stack.AddTitled(_recordPage, "record", "Record");
        _stack.AddTitled(_screenshotPage, "screenshot", "Screenshot");

        Child = _stack;

        // Center on screen when shown
        OnShow += OnWindowShown;

        // Cleanup pages when window is destroyed
        OnDestroy += (_, _) =>
        {
            _recordPage.Cleanup();
            _screenshotPage.Cleanup();
        };
    }

    private void OnWindowShown(Widget sender, EventArgs args)
    {
        OnShow -= OnWindowShown;

        var bounds = WindowPositioner.GetScreenBounds();
        if (bounds is not { } screen)
            return;

        var x = (screen.Width - WindowWidth) / 2;
        var y = (screen.Height - WindowHeight) / 2;

        var surface = GetSurface();
        if (surface is not null)
            WindowPositioner.MoveWindow(surface, x, y);
    }

    /// <summary>
    /// Hides window temporarily (e.g., during area selection).
    /// Saves position for restoration.
    /// </summary>
    public void HideTemporarily()
    {
        // Save current position before hiding
        var surface = GetSurface();
        if (surface is not null)
        {
            var pos = WindowPositioner.GetWindowPosition(surface);
            if (pos.HasValue)
                _savedPosition = pos.Value;
        }
        Hide();
    }

    /// <summary>
    /// Shows window after temporary hide.
    /// Restores to saved position.
    /// </summary>
    public void ShowAgain()
    {
        Present();

        // Restore position after showing
        if (_savedPosition is { } pos)
        {
            var surface = GetSurface();
            if (surface is not null)
                WindowPositioner.MoveWindow(surface, pos.X, pos.Y);
        }
    }

    /// <summary>
    /// Switches to the Record tab.
    /// </summary>
    public void SwitchToRecord() => _stack.SetVisibleChildName("record");

    /// <summary>
    /// Switches to the Screenshot tab.
    /// </summary>
    public void SwitchToScreenshot() => _stack.SetVisibleChildName("screenshot");

    /// <summary>
    /// Switches to Screenshot tab and triggers selection capture.
    /// </summary>
    public void TriggerScreenshotSelection()
    {
        _stack.SetVisibleChildName("screenshot");
        _screenshotPage.TriggerSelectionCapture();
    }
}
