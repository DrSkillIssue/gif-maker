using Gtk;
using GifMaker.Screenshot;
using GifMaker.X11;

namespace GifMaker.App;

/// <summary>
/// Modal overlay for screenshot mode selection.
/// Similar to Ubuntu's screenshot overlay - shows capture mode buttons.
/// </summary>
public sealed class ScreenshotOverlay : Window
{
    #region Configuration

    private const int WindowWidth = 300;
    private const int WindowHeight = 180;
    private const int Margin = 20;
    private const int ButtonSpacing = 10;

    #endregion

    private readonly Action<CaptureMode, bool>? _onModeSelected;
    private readonly Action? _onCancelled;
    private readonly CheckButton _showPointerCheck;
    private bool _modeSelected;

    /// <summary>
    /// Creates a screenshot overlay window.
    /// </summary>
    /// <param name="app">Parent GTK application.</param>
    /// <param name="onModeSelected">Callback when user selects a mode. Receives (mode, showPointer).</param>
    /// <param name="onCancelled">Callback when user cancels (Escape or close).</param>
    public ScreenshotOverlay(
        Application app,
        Action<CaptureMode, bool>? onModeSelected = null,
        Action? onCancelled = null)
    {
        Application = app;
        _onModeSelected = onModeSelected;
        _onCancelled = onCancelled;

        Title = "Screenshot";
        SetDefaultSize(WindowWidth, WindowHeight);
        Resizable = false;
        Modal = true;

        // Center on screen
        OnShow += OnWindowShown;

        var mainBox = Box.New(Orientation.Vertical, 12);
        mainBox.MarginTop = Margin;
        mainBox.MarginBottom = Margin;
        mainBox.MarginStart = Margin;
        mainBox.MarginEnd = Margin;

        // Title
        var titleLabel = Label.New("Take Screenshot");
        titleLabel.AddCssClass("title-3");
        mainBox.Append(titleLabel);

        // Mode buttons
        var buttonBox = Box.New(Orientation.Horizontal, ButtonSpacing);
        buttonBox.Halign = Align.Center;
        buttonBox.MarginTop = 10;

        var selectionBtn = CreateModeButton("Selection", "S", CaptureMode.Selection);
        selectionBtn.AddCssClass("suggested-action");
        buttonBox.Append(selectionBtn);

        var screenBtn = CreateModeButton("Screen", "C", CaptureMode.Screen);
        buttonBox.Append(screenBtn);

        var windowBtn = CreateModeButton("Window", "W", CaptureMode.Window);
        buttonBox.Append(windowBtn);

        mainBox.Append(buttonBox);

        // Show pointer checkbox
        _showPointerCheck = CheckButton.NewWithLabel("Show pointer");
        _showPointerCheck.TooltipText = "Include mouse cursor in screenshot (P to toggle)";
        _showPointerCheck.Halign = Align.Center;
        _showPointerCheck.MarginTop = 10;
        mainBox.Append(_showPointerCheck);

        // Keyboard hint
        var hintLabel = Label.New(null);
        hintLabel.SetMarkup("<span size='small' color='gray'>S=Selection  C=Screen  W=Window  P=Pointer  Esc=Cancel</span>");
        hintLabel.MarginTop = 10;
        mainBox.Append(hintLabel);

        Child = mainBox;

        // Keyboard shortcuts
        var keyController = EventControllerKey.New();
        keyController.OnKeyPressed += OnKeyPressed;
        AddController(keyController);

        OnCloseRequest += (_, _) =>
        {
            if (!_modeSelected)
                _onCancelled?.Invoke();
            return false;
        };
    }

    private Button CreateModeButton(string label, string shortcut, CaptureMode mode)
    {
        var button = Button.NewWithLabel(label);
        button.TooltipText = $"Press {shortcut}";
        button.OnClicked += (_, _) => SelectMode(mode);
        return button;
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        var key = args.Keyval;

        // Check for mode selection keys (case-insensitive)
        return key switch
        {
            's' or 'S' => SelectAndClose(CaptureMode.Selection),
            'c' or 'C' => SelectAndClose(CaptureMode.Screen),
            'w' or 'W' => SelectAndClose(CaptureMode.Window),
            'p' or 'P' => TogglePointer(),
            Gdk.Constants.KEY_Escape => CancelAndClose(),
            Gdk.Constants.KEY_Return or Gdk.Constants.KEY_KP_Enter => SelectAndClose(CaptureMode.Selection),
            _ => false
        };
    }

    private bool TogglePointer()
    {
        _showPointerCheck.Active = !_showPointerCheck.Active;
        return true;
    }

    private void SelectMode(CaptureMode mode)
    {
        _modeSelected = true;
        var showPointer = _showPointerCheck.Active;
        Close();
        _onModeSelected?.Invoke(mode, showPointer);
    }

    private bool SelectAndClose(CaptureMode mode)
    {
        SelectMode(mode);
        return true;
    }

    private bool CancelAndClose()
    {
        Close();
        _onCancelled?.Invoke();
        return true;
    }

    private void OnWindowShown(Widget sender, EventArgs args)
    {
        // Unsubscribe - one-shot positioning
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
}
