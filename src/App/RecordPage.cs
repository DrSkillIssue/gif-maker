using System.Runtime.Versioning;
using Gtk;
using GifMaker.Core;
using GifMaker.Recording;
using GifMaker.Conversion;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Record tab content - handles area selection, recording, conversion, and result display.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class RecordPage : Box
{
    #region State Machine

    private abstract record PageState
    {
        private PageState() { }

        /// <summary>Initial state - ready to select area.</summary>
        public sealed record Idle : PageState;

        /// <summary>User is selecting area with slop.</summary>
        public sealed record Selecting : PageState;

        /// <summary>Area selected, ready to record.</summary>
        public sealed record Ready(Rectangle Region, RegionOverlay Overlay) : PageState;

        /// <summary>Recording in progress.</summary>
        public sealed record Recording(Rectangle Region, RegionOverlay Overlay, FFmpegRecorder Recorder, int Fps) : PageState;

        /// <summary>Stopping recording.</summary>
        public sealed record Stopping(Rectangle Region, RegionOverlay Overlay, FFmpegRecorder Recorder, int Fps) : PageState;

        /// <summary>Converting recorded video.</summary>
        public sealed record Converting(string TempPath, string OutputPath, int Fps, CancellationTokenSource Cts, RegionOverlay Overlay) : PageState;

        /// <summary>Recording saved successfully.</summary>
        public sealed record Saved(string FilePath) : PageState;

        /// <summary>Error occurred.</summary>
        public sealed record Error(string Message) : PageState;
    }

    #endregion

    #region Configuration

    private static readonly (int Value, string Display)[] FpsOptions =
    [
        (15, "15 fps"),
        (24, "24 fps"),
        (30, "30 fps"),
        (60, "60 fps")
    ];

    private const int DefaultFpsIndex = 2;
    private const int MaxStatusLength = 80;

    private static readonly string DefaultOutputDir = GetDefaultOutputDir();

    private static string GetDefaultOutputDir()
    {
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videosDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            videosDir = Path.Combine(home, "Videos");
        }
        return videosDir;
    }

    #endregion

    #region Fields

    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;

    private readonly Label _statusLabel;
    private readonly Button _selectButton;
    private readonly Button _recordButton;
    private readonly Button _openButton;
    private readonly Button _copyButton;
    private readonly Button _cancelButton;
    private readonly ComboBoxText _formatCombo;
    private readonly ComboBoxText _fpsCombo;
    private readonly Entry _outputDirEntry;
    private readonly Box _optionsBox;
    private readonly Box _resultBox;

    private PageState _state = new PageState.Idle();

    #endregion

    public RecordPage(
        ILogger? logger = null,
        IProcessRunner? processRunner = null)
    {
        _logger = logger ?? NullLogger<RecordPage>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;

        SetOrientation(Orientation.Vertical);
        SetSpacing(12);
        MarginTop = 20;
        MarginBottom = 20;
        MarginStart = 20;
        MarginEnd = 20;

        // Status
        _statusLabel = Label.New("Select an area to record");
        _statusLabel.AddCssClass("dim-label");
        _statusLabel.Wrap = true;
        Append(_statusLabel);

        // Select area button
        _selectButton = Button.NewWithLabel("Select Area");
        _selectButton.AddCssClass("suggested-action");
        _selectButton.OnClicked += OnSelectClicked;
        _selectButton.MarginTop = 10;
        Append(_selectButton);

        // Options (format, fps, output dir)
        _optionsBox = CreateOptionsBox(out _formatCombo, out _fpsCombo, out _outputDirEntry);
        _optionsBox.MarginTop = 15;
        Append(_optionsBox);

        // Record/Stop button
        _recordButton = Button.NewWithLabel("Record");
        _recordButton.AddCssClass("suggested-action");
        _recordButton.OnClicked += OnRecordClicked;
        _recordButton.Sensitive = false;
        _recordButton.MarginTop = 10;
        Append(_recordButton);

        // Result buttons (Open, Copy)
        _resultBox = Box.New(Orientation.Horizontal, 10);
        _resultBox.Halign = Align.Center;
        _resultBox.MarginTop = 10;

        _openButton = Button.NewWithLabel("Open");
        _openButton.OnClicked += OnOpenClicked;
        _openButton.Sensitive = false;
        _resultBox.Append(_openButton);

        _copyButton = Button.NewWithLabel("Copy");
        _copyButton.OnClicked += OnCopyClicked;
        _copyButton.TooltipText = "Copy file to clipboard";
        _copyButton.Sensitive = false;
        _resultBox.Append(_copyButton);

        _cancelButton = Button.NewWithLabel("Cancel");
        _cancelButton.OnClicked += OnCancelClicked;
        _cancelButton.Sensitive = false;
        _resultBox.Append(_cancelButton);

        Append(_resultBox);

        ApplyUiFromState();
    }

    private static Box CreateOptionsBox(
        out ComboBoxText formatCombo,
        out ComboBoxText fpsCombo,
        out Entry outputDirEntry)
    {
        var box = Box.New(Orientation.Vertical, 8);

        // Format row
        var formatRow = Box.New(Orientation.Horizontal, 8);
        formatRow.Append(Label.New("Format:"));
        formatCombo = ComboBoxText.New();
        formatCombo.AppendText("GIF");
        formatCombo.AppendText("MP4");
        formatCombo.AppendText("WebM");
        formatCombo.Active = 0;
        formatCombo.Hexpand = true;
        formatRow.Append(formatCombo);
        box.Append(formatRow);

        // FPS row
        var fpsRow = Box.New(Orientation.Horizontal, 8);
        fpsRow.Append(Label.New("FPS:"));
        fpsCombo = ComboBoxText.New();
        foreach (var (_, display) in FpsOptions)
            fpsCombo.AppendText(display);
        fpsCombo.Active = DefaultFpsIndex;
        fpsCombo.Hexpand = true;
        fpsRow.Append(fpsCombo);
        box.Append(fpsRow);

        // Output dir row
        var outputRow = Box.New(Orientation.Horizontal, 8);
        outputRow.Append(Label.New("Save to:"));
        outputDirEntry = Entry.New();
        outputDirEntry.SetText(DefaultOutputDir);
        outputDirEntry.Hexpand = true;
        outputDirEntry.TooltipText = "Directory where recordings will be saved";
        outputRow.Append(outputDirEntry);
        box.Append(outputRow);

        return box;
    }

    #region State Management

    private void TransitionTo(PageState newState)
    {
        _state = newState;
        ApplyUiFromState();
    }

    private void ApplyUiFromState()
    {
        var (status, selectSensitive, recordLabel, recordSensitive, recordDestructive,
             optionsSensitive, openSensitive, copySensitive, cancelSensitive) = _state switch
             {
                 PageState.Idle => (
                     "Select an area to record",
                     true, "Record", false, false,
                     true, false, false, false),

                 PageState.Selecting => (
                     "Click and drag to select area...",
                     false, "Record", false, false,
                     false, false, false, false),

                 PageState.Ready r => (
                     $"Ready: {r.Region.Width}x{r.Region.Height}",
                     true, "Record", true, false,
                     true, false, false, false),

                 PageState.Recording r => (
                     $"Recording at {r.Fps} fps...",
                     false, "Stop", true, true,
                     false, false, false, false),

                 PageState.Stopping s => (
                     $"Stopping ({s.Fps} fps)...",
                     false, "Stop", false, true,
                     false, false, false, false),

                 PageState.Converting => (
                     "Converting...",
                     false, "Converting", false, false,
                     false, false, false, true),

                 PageState.Saved s => (
                     $"Saved: {Path.GetFileName(s.FilePath)}",
                     true, "New Recording", true, false,
                     true, true, true, false),

                 PageState.Error e => (
                     $"Error: {e.Message}",
                     true, "Retry", true, false,
                     true, false, false, false),

                 _ => throw new InvalidOperationException($"Unhandled state: {_state}")
             };

        _statusLabel.SetLabel(status);
        _selectButton.Sensitive = selectSensitive;

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
    }

    #endregion

    #region Event Handlers

    private async void OnSelectClicked(Button sender, EventArgs args)
    {
        // If already have a region selected, dispose the overlay
        if (_state is PageState.Ready ready)
        {
            ready.Overlay.Dispose();
        }

        TransitionTo(new PageState.Selecting());

        // Hide main window during selection
        var mainWindow = GetAncestor(Window.GetGType()) as MainWindow;
        mainWindow?.HideTemporarily();

        // Small delay for window to hide
        await Task.Delay(100);

        try
        {
            using var selector = new AreaSelector(_processRunner);
            var result = await selector.SelectAsync();

            result.Match(
                region =>
                {
                    if (region.IsValid)
                    {
                        var overlay = new RegionOverlay(region, 3);
                        overlay.Show();
                        GLib.Functions.IdleAdd(0, () =>
                        {
                            TransitionTo(new PageState.Ready(region, overlay));
                            mainWindow?.ShowAgain();
                            return false;
                        });
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
            _logger.LogError(ex, "Selection error");
            GLib.Functions.IdleAdd(0, () =>
            {
                TransitionTo(new PageState.Error(Truncate(ex.Message)));
                mainWindow?.ShowAgain();
                return false;
            });
        }
    }

    private async void OnRecordClicked(Button sender, EventArgs args)
    {
        _recordButton.Sensitive = false;

        try
        {
            switch (_state)
            {
                case PageState.Ready ready:
                    StartRecording(ready.Region, ready.Overlay);
                    break;

                case PageState.Recording recording:
                    TransitionTo(new PageState.Stopping(recording.Region, recording.Overlay, recording.Recorder, recording.Fps));
                    await StopRecordingAsync(recording.Recorder, recording.Fps, recording.Overlay);
                    break;

                case PageState.Saved:
                case PageState.Error:
                    TransitionTo(new PageState.Idle());
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording error");
            TransitionTo(new PageState.Error(Truncate(ex.Message)));
        }

        ApplyUiFromState();
    }

    private void StartRecording(Rectangle region, RegionOverlay overlay)
    {
        var fpsIndex = _fpsCombo.Active;
        var fps = fpsIndex >= 0 && fpsIndex < FpsOptions.Length
            ? FpsOptions[fpsIndex].Value
            : FpsOptions[DefaultFpsIndex].Value;

        var recorder = new FFmpegRecorder();
        recorder.Start(region, fps);

        TransitionTo(new PageState.Recording(region, overlay, recorder, fps));
    }

    private async Task StopRecordingAsync(FFmpegRecorder recorder, int fps, RegionOverlay overlay)
    {
        string? tempPath = null;
        CancellationTokenSource? cts = null;

        try
        {
            await recorder.StopAsync();
            tempPath = recorder.TempPath;

            overlay.Hide();

            var format = GetSelectedFormat();
            var outputPath = GenerateOutputPath(format);
            cts = new CancellationTokenSource();

            TransitionTo(new PageState.Converting(tempPath, outputPath, fps, cts, overlay));

            var converter = new FFmpegConverter();
            var settings = FFmpegConverter.ConversionSettings.CreateOrThrow(format, fps);
            await converter.ConvertAsync(tempPath, outputPath, settings, ct: cts.Token);

            cts.Dispose();
            cts = null;
            overlay.Dispose();

            TransitionTo(new PageState.Saved(outputPath));
            _logger.LogInformation("Saved: {OutputPath}", outputPath);
        }
        catch (OperationCanceledException)
        {
            overlay.Dispose();
            TransitionTo(new PageState.Idle());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion failed");
            overlay.Dispose();
            TransitionTo(new PageState.Error(Truncate(ex.Message)));
        }
        finally
        {
            cts?.Dispose();
            if (tempPath is not null)
                CleanupTempFile(tempPath);
            recorder.Dispose();
        }
    }

    private void OnCancelClicked(Button sender, EventArgs args)
    {
        if (_state is PageState.Converting converting)
        {
            converting.Cts.Cancel();
        }
    }

    private OutputFormat GetSelectedFormat() => _formatCombo.Active switch
    {
        0 => OutputFormat.Gif,
        1 => OutputFormat.Mp4,
        2 => OutputFormat.WebM,
        _ => OutputFormat.Gif
    };

    private string GenerateOutputPath(OutputFormat format)
    {
        var outputDir = _outputDirEntry.GetText();

        if (string.IsNullOrWhiteSpace(outputDir))
            outputDir = DefaultOutputDir;

        if (outputDir.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            outputDir = Path.Combine(home, outputDir[1..].TrimStart('/'));
        }

        Directory.CreateDirectory(outputDir);

        var filename = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}{format.GetExtension()}";
        return Path.Combine(outputDir, filename);
    }

    private void CleanupTempFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete temp file: {Path}", path); }
    }

    private void OnOpenClicked(Button sender, EventArgs args)
    {
        if (_state is not PageState.Saved saved)
            return;

        if (!File.Exists(saved.FilePath))
        {
            _logger.LogWarning("File not found: {FilePath}", saved.FilePath);
            TransitionTo(new PageState.Error("File not found"));
            return;
        }

        if (!_processRunner.StartDetached("xdg-open", [saved.FilePath]))
        {
            _logger.LogError("Failed to open file with xdg-open");
            TransitionTo(new PageState.Error("Failed to open file"));
        }
    }

    private void OnCopyClicked(Button sender, EventArgs args)
    {
        if (_state is not PageState.Saved saved)
            return;

        var window = GetAncestor(Window.GetGType()) as Window;
        if (window is null)
            return;

        var result = ClipboardService.CopyFileToClipboard(window, saved.FilePath, _logger);
        if (result.Success)
        {
            _statusLabel.SetLabel("Copied to clipboard!");
        }
        else
        {
            _logger.LogError("Failed to copy to clipboard: {Error}", result.Error);
            TransitionTo(new PageState.Error(result.Error ?? "Copy failed"));
        }
    }

    #endregion

    private static string Truncate(string message, int max = MaxStatusLength) =>
        message.Length <= max ? message : message[..(max - 3)] + "...";

    /// <summary>
    /// Cleans up resources when window closes.
    /// </summary>
    public void Cleanup()
    {
        switch (_state)
        {
            case PageState.Ready ready:
                ready.Overlay.Dispose();
                break;
            case PageState.Recording recording:
                recording.Overlay.Dispose();
                recording.Recorder.Dispose();
                break;
            case PageState.Stopping stopping:
                stopping.Overlay.Dispose();
                stopping.Recorder.Dispose();
                break;
            case PageState.Converting converting:
                converting.Cts.Cancel();
                converting.Cts.Dispose();
                converting.Overlay.Dispose();
                break;
        }

        _state = new PageState.Idle();
    }
}
