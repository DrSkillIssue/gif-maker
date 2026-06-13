using System.Runtime.Versioning;
using Gtk;
using GifMaker.Conversion;

namespace GifMaker.App;

/// <summary>
/// Record tab content. Renders recording state and emits user intents.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class RecordPage : Box
{
    private static readonly (int Value, string Display)[] FpsOptions =
    [
        (15, "15 fps"),
        (24, "24 fps"),
        (30, "30 fps"),
        (60, "60 fps")
    ];

    private const int DefaultFpsIndex = 2;

    private readonly Label _statusLabel;
    private readonly Button _recordButton;
    private readonly Button _openButton;
    private readonly Button _copyButton;
    private readonly Button _cancelButton;
    private readonly ComboBoxText _formatCombo;
    private readonly ComboBoxText _fpsCombo;
    private readonly Entry _outputDirEntry;
    private readonly Box _resultBox;

    public event Action<RecordIntent>? IntentRaised;

    public RecordPage()
    {
        SetOrientation(Orientation.Vertical);
        SetSpacing(12);
        MarginTop = 20;
        MarginBottom = 20;
        MarginStart = 20;
        MarginEnd = 20;

        _statusLabel = Label.New("Ready to record");
        _statusLabel.AddCssClass("dim-label");
        _statusLabel.Wrap = true;
        Append(_statusLabel);

        var optionsBox = Box.New(Orientation.Vertical, 8);
        optionsBox.MarginTop = 15;

        var formatRow = Box.New(Orientation.Horizontal, 8);
        formatRow.Append(Label.New("Format:"));
        _formatCombo = ComboBoxText.New();
        _formatCombo.AppendText("GIF");
        _formatCombo.AppendText("MP4");
        _formatCombo.AppendText("WebM");
        _formatCombo.Active = 0;
        _formatCombo.Hexpand = true;
        formatRow.Append(_formatCombo);
        optionsBox.Append(formatRow);

        var fpsRow = Box.New(Orientation.Horizontal, 8);
        fpsRow.Append(Label.New("FPS:"));
        _fpsCombo = ComboBoxText.New();
        foreach (var (_, display) in FpsOptions)
            _fpsCombo.AppendText(display);
        _fpsCombo.Active = DefaultFpsIndex;
        _fpsCombo.Hexpand = true;
        fpsRow.Append(_fpsCombo);
        optionsBox.Append(fpsRow);

        var outputRow = Box.New(Orientation.Horizontal, 8);
        outputRow.Append(Label.New("Save to:"));
        _outputDirEntry = Entry.New();
        _outputDirEntry.SetText(RecordingOutputPaths.GetDefaultVideoDir());
        _outputDirEntry.Hexpand = true;
        _outputDirEntry.TooltipText = "Directory where recordings will be saved";
        outputRow.Append(_outputDirEntry);
        optionsBox.Append(outputRow);

        Append(optionsBox);

        _recordButton = Button.NewWithLabel("Record");
        _recordButton.AddCssClass("suggested-action");
        _recordButton.Sensitive = true;
        _recordButton.MarginTop = 10;
        _recordButton.OnClicked += (_, _) => IntentRaised?.Invoke(new RecordIntent.ToggleRecording());
        Append(_recordButton);

        _resultBox = Box.New(Orientation.Horizontal, 10);
        _resultBox.Halign = Align.Center;
        _resultBox.MarginTop = 10;

        _openButton = Button.NewWithLabel("Open");
        _openButton.Sensitive = false;
        _openButton.OnClicked += (_, _) => IntentRaised?.Invoke(new RecordIntent.OpenSaved());
        _resultBox.Append(_openButton);

        _copyButton = Button.NewWithLabel("Copy");
        _copyButton.TooltipText = "Copy file to clipboard";
        _copyButton.Sensitive = false;
        _copyButton.OnClicked += (_, _) => IntentRaised?.Invoke(new RecordIntent.CopySaved());
        _resultBox.Append(_copyButton);

        _cancelButton = Button.NewWithLabel("Cancel");
        _cancelButton.Sensitive = false;
        _cancelButton.OnClicked += (_, _) => IntentRaised?.Invoke(new RecordIntent.CancelConversion());
        _resultBox.Append(_cancelButton);

        Append(_resultBox);
        Render(new RecordViewState.Idle());
    }

    public RecordStartOptions ReadStartOptions()
    {
        var fpsIndex = _fpsCombo.Active;
        var fps = fpsIndex >= 0 && fpsIndex < FpsOptions.Length
            ? FpsOptions[fpsIndex].Value
            : FpsOptions[DefaultFpsIndex].Value;

        return new RecordStartOptions(fps);
    }

    public RecordExportTarget ReadExportTarget()
    {
        var outputDir = _outputDirEntry.GetText();
        var directory = string.IsNullOrWhiteSpace(outputDir) ? null : outputDir;
        var format = _formatCombo.Active switch
        {
            0 => ConversionFormat.Gif,
            1 => ConversionFormat.Mp4,
            2 => ConversionFormat.WebM,
            _ => throw new InvalidOperationException($"Unhandled recording format index: {_formatCombo.Active}")
        };

        return new RecordExportTarget(format, directory);
    }

    public void Render(RecordViewState state)
    {
        var (status, recordLabel, recordSensitive, recordDestructive,
            optionsSensitive, openSensitive, copySensitive, cancelSensitive) = state switch
            {
                RecordViewState.Idle => (
                    "Ready to record",
                    "Record", true, false,
                    true, false, false, false),

                RecordViewState.Selecting => (
                    "Click and drag to select area...",
                    "Record", false, false,
                    false, false, false, false),

                RecordViewState.Recording recording => (
                    $"Recording at {recording.Fps} fps...",
                    "Stop", true, true,
                    false, false, false, false),

                RecordViewState.Stopping stopping => (
                    $"Stopping ({stopping.Fps} fps)...",
                    "Stop", false, true,
                    false, false, false, false),

                RecordViewState.Converting => (
                    "Converting...",
                    "Converting", false, false,
                    false, false, false, true),

                RecordViewState.Saved saved => (
                    $"Saved: {Path.GetFileName(saved.Media.Path)}",
                    "New Recording", true, false,
                    true, true, true, false),

                RecordViewState.Error error => (
                    $"Error: {error.Message}",
                    "Retry", true, false,
                    true, false, false, false),

                _ => throw new InvalidOperationException($"Unhandled state: {state.GetType().Name}")
            };

        _statusLabel.SetLabel(status);
        _recordButton.SetLabel(recordLabel);
        _recordButton.Sensitive = recordSensitive;

        if (recordDestructive)
        {
            _recordButton.RemoveCssClass("suggested-action");
            _recordButton.AddCssClass("destructive-action");
        }
        else
        {
            _recordButton.RemoveCssClass("destructive-action");
            _recordButton.AddCssClass("suggested-action");
        }

        _formatCombo.Sensitive = optionsSensitive;
        _fpsCombo.Sensitive = optionsSensitive;
        _outputDirEntry.Sensitive = optionsSensitive;
        _openButton.Sensitive = openSensitive;
        _copyButton.Sensitive = copySensitive;
        _cancelButton.Sensitive = cancelSensitive;
        _resultBox.Visible = openSensitive || copySensitive || cancelSensitive;
    }

}
