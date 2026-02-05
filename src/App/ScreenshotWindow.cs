using Gtk;
using GifMaker.Core;
using GifMaker.Screenshot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Window showing screenshot preview after capture.
/// Auto-saves and auto-copies, then shows preview with action buttons.
/// </summary>
public sealed class ScreenshotWindow : Window
{
    #region Configuration

    private const int WindowWidth = 500;
    private const int WindowHeight = 400;
    private const int PreviewMaxWidth = 460;
    private const int PreviewMaxHeight = 280;
    private const int Margin = 20;

    #endregion

    #region Fields

    private readonly string _filePath;
    private readonly Rectangle _region;
    private readonly CaptureMode _mode;
    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;
    private readonly Action? _onClosed;

    private readonly Label _statusLabel;
    private readonly Button _openButton;
    private readonly Button _openFolderButton;
    private readonly Button _copyButton;
    private readonly Button _closeButton;

    #endregion

    /// <summary>
    /// Creates a ScreenshotWindow displaying the captured screenshot.
    /// </summary>
    /// <param name="app">Parent GTK application.</param>
    /// <param name="result">Screenshot capture result.</param>
    /// <param name="clipboardCopied">Whether clipboard copy succeeded.</param>
    /// <param name="onClosed">Callback when window closes.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="processRunner">Optional process runner.</param>
    public ScreenshotWindow(
        Application app,
        ScreenshotService.ScreenshotResult result,
        bool clipboardCopied,
        Action? onClosed = null,
        ILogger? logger = null,
        IProcessRunner? processRunner = null)
    {
        Application = app;
        _filePath = result.FilePath;
        _region = result.Region;
        _mode = result.Mode;
        _onClosed = onClosed;
        _logger = logger ?? NullLogger<ScreenshotWindow>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;

        Title = "Screenshot";
        SetDefaultSize(WindowWidth, WindowHeight);
        Resizable = true;

        var mainBox = Box.New(Orientation.Vertical, 12);
        mainBox.MarginTop = Margin;
        mainBox.MarginBottom = Margin;
        mainBox.MarginStart = Margin;
        mainBox.MarginEnd = Margin;

        // Status label
        var statusText = BuildStatusText(clipboardCopied);
        _statusLabel = Label.New(statusText);
        _statusLabel.Wrap = true;
        _statusLabel.Xalign = 0;
        mainBox.Append(_statusLabel);

        // Preview image
        var previewWidget = CreatePreviewWidget();
        mainBox.Append(previewWidget);

        // File path label (clickable style)
        var pathLabel = Label.New(null);
        pathLabel.SetMarkup($"<span size='small' color='gray'>{GLib.Functions.MarkupEscapeText(_filePath, -1)}</span>");
        pathLabel.Wrap = true;
        pathLabel.Xalign = 0;
        pathLabel.Selectable = true;
        mainBox.Append(pathLabel);

        // Button row
        var buttonBox = Box.New(Orientation.Horizontal, 10);
        buttonBox.Halign = Align.Center;
        buttonBox.MarginTop = 10;

        _openButton = Button.NewWithLabel("Open");
        _openButton.OnClicked += OnOpenClicked;
        _openButton.AddCssClass("suggested-action");
        buttonBox.Append(_openButton);

        _openFolderButton = Button.NewWithLabel("Open Folder");
        _openFolderButton.OnClicked += OnOpenFolderClicked;
        buttonBox.Append(_openFolderButton);

        _copyButton = Button.NewWithLabel("Copy");
        _copyButton.OnClicked += OnCopyClicked;
        _copyButton.TooltipText = "Copy image to clipboard";
        buttonBox.Append(_copyButton);

        _closeButton = Button.NewWithLabel("Close");
        _closeButton.OnClicked += OnCloseClicked;
        buttonBox.Append(_closeButton);

        mainBox.Append(buttonBox);

        Child = mainBox;

        OnCloseRequest += (_, _) =>
        {
            _onClosed?.Invoke();
            return false;
        };
    }

    private string BuildStatusText(bool clipboardCopied)
    {
        var modeText = _mode switch
        {
            CaptureMode.Selection => "Area",
            CaptureMode.Screen => "Screen",
            CaptureMode.Window => "Window",
            _ => "Screenshot"
        };

        var clipboardText = clipboardCopied ? " and copied to clipboard" : "";
        return $"{modeText} screenshot saved ({_region.Width}x{_region.Height}){clipboardText}";
    }

    private Widget CreatePreviewWidget()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return CreateErrorLabel("Screenshot file not found");
            }

            // Load texture from file
            var texture = Gdk.Texture.NewFromFilename(_filePath);

            // Calculate scaled size maintaining aspect ratio
            var (scaledWidth, scaledHeight) = CalculateScaledSize(
                texture.Width, texture.Height,
                PreviewMaxWidth, PreviewMaxHeight);

            // Create picture widget
            var picture = Picture.NewForPaintable(texture);
            picture.SetSizeRequest(scaledWidth, scaledHeight);
            picture.CanShrink = true;
            picture.ContentFit = ContentFit.Contain;

            // Wrap in frame for visual boundary
            var frame = Frame.New(null);
            frame.Child = picture;
            frame.Halign = Align.Center;
            frame.Valign = Align.Center;
            frame.Vexpand = true;

            return frame;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load screenshot preview");
            return CreateErrorLabel("Failed to load preview");
        }
    }

    private static (int Width, int Height) CalculateScaledSize(
        int originalWidth, int originalHeight,
        int maxWidth, int maxHeight)
    {
        if (originalWidth <= maxWidth && originalHeight <= maxHeight)
            return (originalWidth, originalHeight);

        var widthRatio = (double)maxWidth / originalWidth;
        var heightRatio = (double)maxHeight / originalHeight;
        var ratio = Math.Min(widthRatio, heightRatio);

        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }

    private static Label CreateErrorLabel(string message)
    {
        var label = Label.New(message);
        label.AddCssClass("dim-label");
        label.Vexpand = true;
        label.Valign = Align.Center;
        return label;
    }

    #region Event Handlers

    private void OnOpenClicked(Button sender, EventArgs args)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogWarning("Screenshot file not found: {FilePath}", _filePath);
            return;
        }

        if (!_processRunner.StartDetached("xdg-open", [_filePath]))
        {
            _logger.LogError("Failed to open screenshot with xdg-open");
        }
    }

    private void OnOpenFolderClicked(Button sender, EventArgs args)
    {
        var folder = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            _logger.LogWarning("Screenshot folder not found");
            return;
        }

        if (!_processRunner.StartDetached("xdg-open", [folder]))
        {
            _logger.LogError("Failed to open folder with xdg-open");
        }
    }

    private void OnCopyClicked(Button sender, EventArgs args)
    {
        var result = ClipboardService.CopyFileToClipboard(this, _filePath, _logger);

        if (result.Success)
        {
            _statusLabel.SetLabel("Copied to clipboard!");
            _logger.LogInformation("Screenshot copied to clipboard");
        }
        else
        {
            _statusLabel.SetLabel($"Copy failed: {result.Error}");
            _logger.LogError("Failed to copy screenshot: {Error}", result.Error);
        }
    }

    private void OnCloseClicked(Button sender, EventArgs args)
    {
        Close();
    }

    #endregion
}
