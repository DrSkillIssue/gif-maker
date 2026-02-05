using System.Runtime.Versioning;
using Gtk;
using GifMaker.Core;
using GifMaker.Screenshot;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Screenshot tab content - handles mode selection, capture, and result display.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ScreenshotPage : Box
{
    #region State Machine

    private abstract record PageState
    {
        private PageState() { }

        /// <summary>Ready to take screenshot.</summary>
        public sealed record Idle : PageState;

        /// <summary>Selecting area.</summary>
        public sealed record Selecting : PageState;

        /// <summary>Capturing screenshot.</summary>
        public sealed record Capturing(CaptureMode Mode) : PageState;

        /// <summary>Screenshot saved successfully.</summary>
        public sealed record Saved(string FilePath, Rectangle Region, CaptureMode Mode, Gdk.Texture? Texture) : PageState;

        /// <summary>Error occurred.</summary>
        public sealed record Error(string Message) : PageState;
    }

    #endregion

    #region Constants

    private const int WindowHideDelayMs = 100;

    #endregion

    #region Fields

    private readonly ILogger<ScreenshotPage> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly ScreenshotService _screenshotService;

    private readonly Label _statusLabel;
    private readonly Button _selectionButton;
    private readonly Button _screenButton;
    private readonly Button _windowButton;
    private readonly CheckButton _showPointerCheck;
    private readonly Box _modeBox;

    private readonly Frame _previewFrame;
    private readonly Picture _previewPicture;
    private readonly Button _openButton;
    private readonly Button _openFolderButton;
    private readonly Button _copyButton;
    private readonly Button _newButton;
    private readonly Box _resultBox;

    private PageState _state = new PageState.Idle();

    #endregion

    public ScreenshotPage(
        ILogger<ScreenshotPage>? logger = null,
        IProcessRunner? processRunner = null)
    {
        _logger = logger ?? NullLogger<ScreenshotPage>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;
        _screenshotService = new ScreenshotService(logger: null, _processRunner);

        SetOrientation(Orientation.Vertical);
        SetSpacing(12);
        MarginTop = 20;
        MarginBottom = 20;
        MarginStart = 20;
        MarginEnd = 20;

        // Status
        _statusLabel = Label.New("Choose a capture mode");
        _statusLabel.AddCssClass("dim-label");
        _statusLabel.Wrap = true;
        Append(_statusLabel);

        // Mode buttons
        _modeBox = Box.New(Orientation.Horizontal, 10);
        _modeBox.Halign = Align.Center;
        _modeBox.MarginTop = 10;

        _selectionButton = Button.NewWithLabel("Selection");
        _selectionButton.AddCssClass("suggested-action");
        _selectionButton.TooltipText = "Select an area to capture";
        _selectionButton.OnClicked += (_, _) => CaptureAsync(CaptureMode.Selection);
        _modeBox.Append(_selectionButton);

        _screenButton = Button.NewWithLabel("Screen");
        _screenButton.TooltipText = "Capture entire screen";
        _screenButton.OnClicked += (_, _) => CaptureAsync(CaptureMode.Screen);
        _modeBox.Append(_screenButton);

        _windowButton = Button.NewWithLabel("Window");
        _windowButton.TooltipText = "Capture active window";
        _windowButton.OnClicked += (_, _) => CaptureAsync(CaptureMode.Window);
        _modeBox.Append(_windowButton);

        Append(_modeBox);

        // Show pointer checkbox
        _showPointerCheck = CheckButton.NewWithLabel("Show pointer");
        _showPointerCheck.TooltipText = "Include mouse cursor in screenshot";
        _showPointerCheck.Halign = Align.Center;
        _showPointerCheck.MarginTop = 5;
        Append(_showPointerCheck);

        // Preview (hidden initially)
        _previewPicture = Picture.New();
        _previewPicture.SetSizeRequest(360, 200);
        _previewPicture.CanShrink = true;
        _previewPicture.ContentFit = ContentFit.Contain;

        _previewFrame = Frame.New(null);
        _previewFrame.Child = _previewPicture;
        _previewFrame.Halign = Align.Center;
        _previewFrame.MarginTop = 15;
        _previewFrame.Visible = false;
        Append(_previewFrame);

        // Result buttons
        _resultBox = Box.New(Orientation.Horizontal, 10);
        _resultBox.Halign = Align.Center;
        _resultBox.MarginTop = 10;

        _openButton = Button.NewWithLabel("Open");
        _openButton.OnClicked += OnOpenClicked;
        _resultBox.Append(_openButton);

        _openFolderButton = Button.NewWithLabel("Open Folder");
        _openFolderButton.OnClicked += OnOpenFolderClicked;
        _resultBox.Append(_openFolderButton);

        _copyButton = Button.NewWithLabel("Copy");
        _copyButton.TooltipText = "Copy image to clipboard";
        _copyButton.OnClicked += OnCopyClicked;
        _resultBox.Append(_copyButton);

        _newButton = Button.NewWithLabel("New");
        _newButton.AddCssClass("suggested-action");
        _newButton.OnClicked += OnNewClicked;
        _resultBox.Append(_newButton);

        _resultBox.Visible = false;
        Append(_resultBox);

        // Keyboard shortcuts hint
        var hintLabel = Label.New(null);
        hintLabel.SetMarkup("<span size='small' color='gray'>S=Selection  C=Screen  W=Window  P=Pointer toggle</span>");
        hintLabel.MarginTop = 10;
        Append(hintLabel);

        // Keyboard controller
        var keyController = EventControllerKey.New();
        keyController.OnKeyPressed += OnKeyPressed;
        AddController(keyController);

        ApplyUiFromState();
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        // Only handle shortcuts when in Idle state
        if (_state is not PageState.Idle)
            return false;

        return args.Keyval switch
        {
            's' or 'S' => CaptureWithKey(CaptureMode.Selection),
            'c' or 'C' => CaptureWithKey(CaptureMode.Screen),
            'w' or 'W' => CaptureWithKey(CaptureMode.Window),
            'p' or 'P' => TogglePointer(),
            _ => false
        };
    }

    private bool CaptureWithKey(CaptureMode mode)
    {
        CaptureAsync(mode);
        return true;
    }

    private bool TogglePointer()
    {
        _showPointerCheck.Active = !_showPointerCheck.Active;
        return true;
    }

    #region State Management

    private void TransitionTo(PageState newState)
    {
        // Dispose texture when leaving Saved state
        if (_state is PageState.Saved { Texture: not null } saved)
        {
            _previewPicture.SetPaintable(null);
            saved.Texture.Dispose();
        }

        _state = newState;
        ApplyUiFromState();
    }

    private void ApplyUiFromState()
    {
        var (status, modesSensitive, showPreview, showResult) = _state switch
        {
            PageState.Idle => ("Choose a capture mode", true, false, false),
            PageState.Selecting => ("Click and drag to select area...", false, false, false),
            PageState.Capturing c => ($"Capturing {CaptureModeToLower(c.Mode)}...", false, false, false),
            PageState.Saved s => ($"Saved: {Path.GetFileName(s.FilePath)} ({s.Region.Width}x{s.Region.Height})", false, true, true),
            PageState.Error e => ($"Error: {e.Message}", true, false, false),
            _ => throw new InvalidOperationException("Unreachable: unknown PageState")
        };

        _statusLabel.SetLabel(status);

        _selectionButton.Sensitive = modesSensitive;
        _screenButton.Sensitive = modesSensitive;
        _windowButton.Sensitive = modesSensitive;
        _showPointerCheck.Sensitive = modesSensitive;
        _modeBox.Visible = modesSensitive || _state is PageState.Selecting or PageState.Capturing;

        _previewFrame.Visible = showPreview;
        _resultBox.Visible = showResult;

        // Update preview image
        if (_state is PageState.Saved saved && saved.Texture is not null)
        {
            _previewPicture.SetPaintable(saved.Texture);
            SetupDragSource(saved.FilePath, saved.Texture);
        }
    }

    private static string CaptureModeToLower(CaptureMode mode) => mode switch
    {
        CaptureMode.Selection => "selection",
        CaptureMode.Screen => "screen",
        CaptureMode.Window => "window",
        _ => mode.ToString().ToLowerInvariant()
    };

    private void SetupDragSource(string filePath, Gdk.Texture texture)
    {
        // Remove existing controllers by collecting then removing
        var controllers = _previewPicture.ObserveControllers();
        var toRemove = new List<EventController>();

        for (uint i = 0; i < controllers.GetNItems(); i++)
        {
            if (controllers.GetObject(i) is EventController controller)
                toRemove.Add(controller);
        }

        foreach (var controller in toRemove)
            _previewPicture.RemoveController(controller);

        // Add drag source
        var dragSource = DragSource.New();
        dragSource.SetActions(Gdk.DragAction.Copy);
        dragSource.OnPrepare += (source, args) =>
        {
            var file = Gio.FileHelper.NewForPath(filePath);
            var uri = file.GetUri();
            var bytes = GLib.Bytes.New(System.Text.Encoding.UTF8.GetBytes(uri + "\r\n"));
            return Gdk.ContentProvider.NewForBytes("text/uri-list", bytes);
        };
        dragSource.OnDragBegin += (source, drag) => source.SetIcon(texture, 0, 0);

        _previewPicture.AddController(dragSource);
        _previewPicture.SetCursor(Gdk.Cursor.NewFromName("grab", null));
        _previewPicture.TooltipText = "Drag to other apps";
    }

    #endregion

    #region Capture

    /// <summary>
    /// Triggers selection capture (for hotkey use).
    /// </summary>
    public void TriggerSelectionCapture() => CaptureAsync(CaptureMode.Selection);

    private async void CaptureAsync(CaptureMode mode)
    {
        var showPointer = _showPointerCheck.Active;
        var mainWindow = GetAncestor(Window.GetGType()) as MainWindow;

        // Capture cursor position before any selection (for "frozen" cursor in selection mode)
        var cursorPosition = showPointer ? WindowPositioner.GetPointerPosition() : null;

        if (mode == CaptureMode.Selection)
        {
            TransitionTo(new PageState.Selecting());
            mainWindow?.HideTemporarily();

            // Show frozen cursor overlay during selection if "Show Pointer" is enabled
            CursorOverlay? cursorOverlay = null;
            if (cursorPosition is { } pos)
            {
                try
                {
                    cursorOverlay = new CursorOverlay(pos.X, pos.Y);
                    cursorOverlay.Show();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create cursor overlay");
                }
            }

            await Task.Delay(WindowHideDelayMs);

            try
            {
                using var selector = new AreaSelector(_processRunner);
                var result = await selector.SelectAsync();

                // Hide cursor overlay after selection
                cursorOverlay?.Hide();
                cursorOverlay?.Dispose();

                result.Match(
                    region =>
                    {
                        if (region.IsValid)
                        {
                            _ = CaptureRegionAsync(region, mode, showPointer, cursorPosition, mainWindow);
                        }
                        else
                        {
                            GLib.Functions.IdleAdd(0, () =>
                            {
                                TransitionTo(new PageState.Idle());
                                mainWindow?.ShowAgain();
                                return false;
                            });
                        }
                    },
                    error =>
                    {
                        GLib.Functions.IdleAdd(0, () =>
                        {
                            if (error != "Selection cancelled")
                                _logger.LogWarning("Selection failed: {Error}", error);
                            TransitionTo(new PageState.Idle());
                            mainWindow?.ShowAgain();
                            return false;
                        });
                    });
            }
            catch (Exception ex)
            {
                cursorOverlay?.Dispose();
                _logger.LogError(ex, "Selection error");
                GLib.Functions.IdleAdd(0, () =>
                {
                    TransitionTo(new PageState.Error(ex.Message));
                    mainWindow?.ShowAgain();
                    return false;
                });
            }
        }
        else
        {
            TransitionTo(new PageState.Capturing(mode));

            try
            {
                var regionResult = await _screenshotService.GetRegionAsync(mode);

                regionResult.Match(
                    region => _ = CaptureRegionAsync(region, mode, showPointer, fixedCursorPosition: null, mainWindow),
                    error =>
                    {
                        GLib.Functions.IdleAdd(0, () =>
                        {
                            TransitionTo(new PageState.Error(error));
                            return false;
                        });
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen/Window capture error");
                GLib.Functions.IdleAdd(0, () =>
                {
                    TransitionTo(new PageState.Error(ex.Message));
                    return false;
                });
            }
        }
    }

    private async Task CaptureRegionAsync(
        Rectangle region,
        CaptureMode mode,
        bool showPointer,
        (int X, int Y)? fixedCursorPosition,
        MainWindow? mainWindow)
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            TransitionTo(new PageState.Capturing(mode));
            return false;
        });

        var captureResult = await _screenshotService.CaptureAsync(region, mode, showPointer, fixedCursorPosition);

        captureResult.Match(
            result =>
            {
                GLib.Functions.IdleAdd(0, () =>
                {
                    // Load texture for preview
                    Gdk.Texture? texture = null;
                    try
                    {
                        texture = Gdk.Texture.NewFromFilename(result.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load preview");
                    }

                    // Copy to clipboard
                    if (mainWindow is not null)
                    {
                        var copyResult = ClipboardService.CopyFileToClipboard(mainWindow, result.FilePath, _logger);
                        if (copyResult.Success)
                            _logger.LogInformation("Screenshot copied to clipboard");
                    }

                    TransitionTo(new PageState.Saved(result.FilePath, result.Region, result.Mode, texture));
                    mainWindow?.ShowAgain();
                    return false;
                });
            },
            error =>
            {
                GLib.Functions.IdleAdd(0, () =>
                {
                    TransitionTo(new PageState.Error(error));
                    mainWindow?.ShowAgain();
                    return false;
                });
            });
    }

    #endregion

    #region Event Handlers

    private void OnOpenClicked(Button sender, EventArgs args)
    {
        if (_state is not PageState.Saved saved)
            return;

        if (File.Exists(saved.FilePath))
            _processRunner.StartDetached("xdg-open", [saved.FilePath]);
    }

    private void OnOpenFolderClicked(Button sender, EventArgs args)
    {
        if (_state is not PageState.Saved saved)
            return;

        var folder = Path.GetDirectoryName(saved.FilePath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            _processRunner.StartDetached("xdg-open", [folder]);
    }

    private void OnCopyClicked(Button sender, EventArgs args)
    {
        if (_state is not PageState.Saved saved)
            return;

        var window = GetAncestor(Window.GetGType()) as Window;
        if (window is null)
            return;

        var result = ClipboardService.CopyFileToClipboard(window, saved.FilePath, _logger);
        _statusLabel.SetLabel(result.Success ? "Copied to clipboard!" : $"Copy failed: {result.Error}");
    }

    private void OnNewClicked(Button sender, EventArgs args)
    {
        TransitionTo(new PageState.Idle());
    }

    #endregion

    /// <summary>
    /// Cleans up resources when window closes.
    /// </summary>
    public void Cleanup()
    {
        _state = new PageState.Idle();
    }
}
