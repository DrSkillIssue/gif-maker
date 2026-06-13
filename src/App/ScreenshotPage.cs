using System.Runtime.Versioning;
using System.Text;
using Gtk;
using GifMaker.Screenshot;
using GifMaker.X11;

namespace GifMaker.App;

/// <summary>
/// Screenshot tab content. Renders screenshot state and emits user intents.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ScreenshotPage : Box
{
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

    private bool _shortcutsEnabled = true;
    private Gdk.Texture? _previewTexture;
    private DragSource? _previewDragSource;

    public event Action<ScreenshotIntent>? IntentRaised;

    public ScreenshotPage()
    {
        SetOrientation(Orientation.Vertical);
        SetSpacing(12);
        MarginTop = 20;
        MarginBottom = 20;
        MarginStart = 20;
        MarginEnd = 20;

        _statusLabel = Label.New("Choose a capture mode");
        _statusLabel.AddCssClass("dim-label");
        _statusLabel.Wrap = true;
        Append(_statusLabel);

        _modeBox = Box.New(Orientation.Horizontal, 10);
        _modeBox.Halign = Align.Center;
        _modeBox.MarginTop = 10;

        _selectionButton = Button.NewWithLabel("Selection");
        _selectionButton.AddCssClass("suggested-action");
        _selectionButton.TooltipText = "Select an area to capture";
        _selectionButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Selection));
        _modeBox.Append(_selectionButton);

        _screenButton = Button.NewWithLabel("Screen");
        _screenButton.TooltipText = "Capture entire screen";
        _screenButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Screen));
        _modeBox.Append(_screenButton);

        _windowButton = Button.NewWithLabel("Window");
        _windowButton.TooltipText = "Capture active window";
        _windowButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Window));
        _modeBox.Append(_windowButton);

        Append(_modeBox);

        _showPointerCheck = CheckButton.NewWithLabel("Show pointer");
        _showPointerCheck.TooltipText = "Include mouse cursor in screenshot";
        _showPointerCheck.Halign = Align.Center;
        _showPointerCheck.MarginTop = 5;
        Append(_showPointerCheck);

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

        _resultBox = Box.New(Orientation.Horizontal, 10);
        _resultBox.Halign = Align.Center;
        _resultBox.MarginTop = 10;

        _openButton = Button.NewWithLabel("Open");
        _openButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.OpenSaved());
        _resultBox.Append(_openButton);

        _openFolderButton = Button.NewWithLabel("Open Folder");
        _openFolderButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.OpenContainingFolder());
        _resultBox.Append(_openFolderButton);

        _copyButton = Button.NewWithLabel("Copy");
        _copyButton.TooltipText = "Copy image to clipboard";
        _copyButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.CopySaved());
        _resultBox.Append(_copyButton);

        _newButton = Button.NewWithLabel("New");
        _newButton.AddCssClass("suggested-action");
        _newButton.OnClicked += (_, _) => IntentRaised?.Invoke(new ScreenshotIntent.Reset());
        _resultBox.Append(_newButton);

        _resultBox.Visible = false;
        Append(_resultBox);

        var hintLabel = Label.New(null);
        hintLabel.SetMarkup("<span size='small' color='gray'>S=Selection  C=Screen  W=Window  P=Pointer toggle</span>");
        hintLabel.MarginTop = 10;
        Append(hintLabel);

        var keyController = EventControllerKey.New();
        keyController.OnKeyPressed += OnKeyPressed;
        AddController(keyController);

        Render(new ScreenshotViewState.Idle());
    }

    public ScreenshotOptions ReadOptions(CaptureMode mode)
    {
        var pointer = _showPointerCheck.Active
            ? mode == CaptureMode.Selection && WindowPositioner.GetPointerPosition() is { } position
                ? new PointerCapture.FrozenAt(position.X, position.Y)
                : new PointerCapture.Live()
            : (PointerCapture)new PointerCapture.Excluded();

        return new ScreenshotOptions(mode, pointer, ScreenshotOutputTarget.Default);
    }

    public void Render(ScreenshotViewState state)
    {
        var (status, modesSensitive, showPreview, showResult) = state switch
        {
            ScreenshotViewState.Idle => ("Choose a capture mode", true, false, false),
            ScreenshotViewState.Selecting => ("Click and drag to select area...", false, false, false),
            ScreenshotViewState.Capturing capturing => ($"Capturing {capturing.Mode switch
            {
                CaptureMode.Selection => "selection",
                CaptureMode.Screen => "screen",
                CaptureMode.Window => "window",
                _ => throw new InvalidOperationException($"Unhandled capture mode: {capturing.Mode}")
            }}...", false, false, false),
            ScreenshotViewState.Saved captured => ($"Saved: {Path.GetFileName(captured.Media.Path)} ({captured.Region.Width}x{captured.Region.Height})", false, true, true),
            ScreenshotViewState.Error error => ($"Error: {error.Message}", true, false, false),
            _ => throw new InvalidOperationException($"Unhandled state: {state.GetType().Name}")
        };

        _shortcutsEnabled = state is ScreenshotViewState.Idle;
        _statusLabel.SetLabel(status);
        _selectionButton.Sensitive = modesSensitive;
        _screenButton.Sensitive = modesSensitive;
        _windowButton.Sensitive = modesSensitive;
        _showPointerCheck.Sensitive = modesSensitive;
        _modeBox.Visible = modesSensitive || state is ScreenshotViewState.Selecting or ScreenshotViewState.Capturing;
        _previewFrame.Visible = showPreview;
        _resultBox.Visible = showResult;

        if (state is ScreenshotViewState.Saved { Preview: not null } saved)
        {
            SetPreview(saved.Preview);
            InstallPreviewDrag(saved.Media, saved.Preview);
        }
        else
        {
            ClearPreview();
        }
    }

    public new void Dispose()
    {
        ClearPreview();
        base.Dispose();
    }

    private bool OnKeyPressed(EventControllerKey sender, EventControllerKey.KeyPressedSignalArgs args)
    {
        if (!_shortcutsEnabled)
            return false;

        switch (args.Keyval)
        {
            case 's':
            case 'S':
                IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Selection));
                return true;

            case 'c':
            case 'C':
                IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Screen));
                return true;

            case 'w':
            case 'W':
                IntentRaised?.Invoke(new ScreenshotIntent.Capture(CaptureMode.Window));
                return true;

            case 'p':
            case 'P':
                _showPointerCheck.Active = !_showPointerCheck.Active;
                return true;

            default:
                return false;
        }
    }

    private void SetPreview(Gdk.Texture texture)
    {
        if (!ReferenceEquals(_previewTexture, texture))
        {
            ClearPreview();
            _previewTexture = texture;
            _previewPicture.SetPaintable(texture);
        }
    }

    private void InstallPreviewDrag(SavedMedia media, Gdk.Texture texture)
    {
        if (_previewDragSource is not null)
            _previewPicture.RemoveController(_previewDragSource);

        var dragSource = DragSource.New();
        dragSource.SetActions(Gdk.DragAction.Copy);
        dragSource.OnPrepare += (_, _) =>
        {
            var file = Gio.FileHelper.NewForPath(media.Path);
            var uri = file.GetUri();
            var bytes = GLib.Bytes.New(Encoding.UTF8.GetBytes(uri + "\r\n"));
            return Gdk.ContentProvider.NewForBytes("text/uri-list", bytes);
        };
        dragSource.OnDragBegin += (source, _) => source.SetIcon(texture, 0, 0);

        _previewDragSource = dragSource;
        _previewPicture.AddController(dragSource);
        _previewPicture.SetCursor(Gdk.Cursor.NewFromName("grab", null));
        _previewPicture.TooltipText = "Drag to other apps";
    }

    private void ClearPreview()
    {
        if (_previewDragSource is not null)
        {
            _previewPicture.RemoveController(_previewDragSource);
            _previewDragSource = null;
        }

        _previewPicture.SetPaintable(null);
        _previewTexture?.Dispose();
        _previewTexture = null;
    }
}
