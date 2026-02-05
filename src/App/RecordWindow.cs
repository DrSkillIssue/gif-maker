using Gtk;
using GifMaker.Core;
using GifMaker.Recording;
using GifMaker.Conversion;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static GifMaker.X11.X11Interop;

namespace GifMaker.App;

/// <summary>
/// Small floating window with record/stop controls.
/// Shows after area is selected.
/// </summary>
public sealed partial class RecordWindow : Window
{
    #region State Machine

    /// <summary>
    /// Discriminated union representing all valid window states.
    /// Makes illegal states unrepresentable.
    /// </summary>
    private abstract record WindowState
    {
        private WindowState() { }

        /// <summary>Ready to start recording. No active recorder.</summary>
        public sealed record Ready : WindowState;

        /// <summary>Actively recording. Recorder is running.</summary>
        public sealed record Recording(FFmpegRecorder Recorder, int Fps) : WindowState;

        /// <summary>Stopping recording. Recorder stopping, awaiting completion.</summary>
        public sealed record Stopping(FFmpegRecorder Recorder, int Fps) : WindowState;

        /// <summary>Converting recorded video. Recorder disposed, conversion in progress.</summary>
        public sealed record Converting(string TempPath, string OutputPath, int Fps, CancellationTokenSource Cts) : WindowState;

        /// <summary>Recording saved. File available for opening.</summary>
        public sealed record Saved(string FilePath) : WindowState;

        /// <summary>An error occurred. Shows message and allows retry.</summary>
        public sealed record Error(string Message) : WindowState;
    }

    /// <summary>
    /// UI configuration derived from state. Single source of truth for UI appearance.
    /// </summary>
    private readonly record struct UiConfig(
        string RecordButtonLabel,
        bool RecordButtonSensitive,
        bool RecordButtonIsSuggested,
        bool RecordButtonIsDestructive,
        string StatusText,
        bool FormatComboSensitive,
        bool FpsComboSensitive,
        bool OutputDirSensitive,
        bool OpenButtonSensitive,
        bool CopyButtonSensitive,
        string CancelButtonLabel,
        bool CancelButtonSensitive,
        bool OverlayVisible
    )
    {
        public static UiConfig FromState(WindowState state, Rectangle region) => state switch
        {
            WindowState.Ready => new UiConfig(
                RecordButtonLabel: "Record",
                RecordButtonSensitive: true,
                RecordButtonIsSuggested: true,
                RecordButtonIsDestructive: false,
                StatusText: $"Region: {region.Width}x{region.Height}",
                FormatComboSensitive: true,
                FpsComboSensitive: true,
                OutputDirSensitive: true,
                OpenButtonSensitive: false,
                CopyButtonSensitive: false,
                CancelButtonLabel: "Cancel",
                CancelButtonSensitive: true,
                OverlayVisible: true
            ),

            WindowState.Recording r => new UiConfig(
                RecordButtonLabel: "Stop",
                RecordButtonSensitive: true,
                RecordButtonIsSuggested: false,
                RecordButtonIsDestructive: true,
                StatusText: $"Recording at {r.Fps} fps...",
                FormatComboSensitive: false,
                FpsComboSensitive: false,
                OutputDirSensitive: false,
                OpenButtonSensitive: false,
                CopyButtonSensitive: false,
                CancelButtonLabel: "Cancel",
                CancelButtonSensitive: false,
                OverlayVisible: true
            ),

            WindowState.Stopping s => new UiConfig(
                RecordButtonLabel: "Stop",
                RecordButtonSensitive: false,
                RecordButtonIsSuggested: false,
                RecordButtonIsDestructive: true,
                StatusText: $"Stopping ({s.Fps} fps)...",
                FormatComboSensitive: false,
                FpsComboSensitive: false,
                OutputDirSensitive: false,
                OpenButtonSensitive: false,
                CopyButtonSensitive: false,
                CancelButtonLabel: "Cancel",
                CancelButtonSensitive: false,
                OverlayVisible: true
            ),

            WindowState.Converting => new UiConfig(
                RecordButtonLabel: "Stop",
                RecordButtonSensitive: false,
                RecordButtonIsSuggested: false,
                RecordButtonIsDestructive: true,
                StatusText: "Converting...",
                FormatComboSensitive: false,
                FpsComboSensitive: false,
                OutputDirSensitive: false,
                OpenButtonSensitive: false,
                CopyButtonSensitive: false,
                CancelButtonLabel: "Cancel",
                CancelButtonSensitive: true,
                OverlayVisible: false
            ),

            WindowState.Saved s => new UiConfig(
                RecordButtonLabel: "New",
                RecordButtonSensitive: true,
                RecordButtonIsSuggested: true,
                RecordButtonIsDestructive: false,
                StatusText: $"Saved: {Path.GetFileName(s.FilePath)}",
                FormatComboSensitive: true,
                FpsComboSensitive: true,
                OutputDirSensitive: true,
                OpenButtonSensitive: true,
                CopyButtonSensitive: true,
                CancelButtonLabel: "Close",
                CancelButtonSensitive: true,
                OverlayVisible: false
            ),

            WindowState.Error e => new UiConfig(
                RecordButtonLabel: "Retry",
                RecordButtonSensitive: true,
                RecordButtonIsSuggested: true,
                RecordButtonIsDestructive: false,
                StatusText: $"Error: {e.Message}",
                FormatComboSensitive: true,
                FpsComboSensitive: true,
                OutputDirSensitive: true,
                OpenButtonSensitive: false,
                CopyButtonSensitive: false,
                CancelButtonLabel: "Close",
                CancelButtonSensitive: true,
                OverlayVisible: false
            ),

            _ => throw new InvalidOperationException($"Unknown state: {state}")
        };
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Supported FPS values with display strings.
    /// </summary>
    private static readonly (int Value, string Display)[] FpsOptions =
    [
        (15, "15"),
        (24, "24"),
        (30, "30"),
        (60, "60")
    ];

    private const int DefaultFpsIndex = 2; // 30 fps
    private const int MaxStatusMessageLength = 60;

    private const int WindowWidth = 400;
    private const int WindowHeight = 260;
    private const int WindowMargin = 10;
    private const int WindowGap = 20;

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

    private readonly Rectangle _region;
    private readonly Button _recordButton;
    private readonly Button _openButton;
    private readonly Button _copyButton;
    private readonly Button _cancelButton;
    private readonly Label _statusLabel;
    private readonly ComboBoxText _formatCombo;
    private readonly ComboBoxText _fpsCombo;
    private readonly Entry _outputDirEntry;
    private readonly RegionOverlay _overlay;
    private readonly int _targetX;
    private readonly int _targetY;
    private readonly ILogger<RecordWindow> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly Action? _onClosed;

    private WindowState _state = new WindowState.Ready();
    private int _cleanedUp;

    #endregion

    /// <summary>
    /// Creates a new RecordWindow for the specified region.
    /// </summary>
    /// <param name="app">Parent GTK application.</param>
    /// <param name="region">Screen region to record.</param>
    /// <param name="onClosed">Callback invoked when window is closed.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="processRunner">Optional process runner for testing.</param>
    public RecordWindow(
        Application app,
        Rectangle region,
        Action? onClosed = null,
        ILogger<RecordWindow>? logger = null,
        IProcessRunner? processRunner = null)
    {
        Application = app;
        _region = region;
        _onClosed = onClosed;
        _logger = logger ?? NullLogger<RecordWindow>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;

        _overlay = new RegionOverlay(region, 3);
        _overlay.Show();

        Title = "GifMaker";
        SetDefaultSize(WindowWidth, WindowHeight);
        Resizable = false;

        (_targetX, _targetY) = CalculateWindowPosition(region);

        OnRealize += OnWindowRealized;

        var box = Box.New(Orientation.Vertical, 10);
        box.MarginTop = 15;
        box.MarginBottom = 15;
        box.MarginStart = 15;
        box.MarginEnd = 15;

        _statusLabel = Label.New(string.Empty);
        box.Append(_statusLabel);

        (_formatCombo, var formatBox) = CreateFormatSelector();
        box.Append(formatBox);

        (_fpsCombo, var fpsBox) = CreateFpsSelector();
        box.Append(fpsBox);

        (_outputDirEntry, var outputBox) = CreateOutputDirSelector();
        box.Append(outputBox);

        (_recordButton, _openButton, _copyButton, var primaryBox) = CreatePrimaryButtons();
        box.Append(primaryBox);

        (_cancelButton, var secondaryBox) = CreateSecondaryButtons();
        box.Append(secondaryBox);

        Child = box;

        OnCloseRequest += (_, _) =>
        {
            Cleanup();
            _onClosed?.Invoke();
            return false;
        };

        ApplyUiFromState();
    }

    #region Initialization Helpers

    private static (int X, int Y) CalculateWindowPosition(Rectangle region)
    {
        var x = region.X + (region.Width - WindowWidth) / 2;
        var y = region.Y + region.Height + WindowGap;

        var bounds = WindowPositioner.GetScreenBounds();
        if (bounds is { } screen)
        {
            // If doesn't fit below, put above
            if (y + WindowHeight > screen.Height)
                y = region.Y - WindowHeight - WindowGap;

            x = Math.Clamp(x, WindowMargin, screen.Width - WindowWidth - WindowMargin);
            y = Math.Max(WindowMargin, y);
        }

        return (x, y);
    }

    private static (ComboBoxText Combo, Box Container) CreateFormatSelector()
    {
        var box = Box.New(Orientation.Horizontal, 8);
        box.Append(Label.New("Format:"));

        var combo = ComboBoxText.New();
        combo.AppendText("GIF");
        combo.AppendText("MP4");
        combo.AppendText("WebM");
        combo.Active = 0;
        combo.Hexpand = true;
        box.Append(combo);

        return (combo, box);
    }

    private static (ComboBoxText Combo, Box Container) CreateFpsSelector()
    {
        var box = Box.New(Orientation.Horizontal, 8);
        box.Append(Label.New("FPS:"));

        var combo = ComboBoxText.New();
        foreach (var (_, display) in FpsOptions)
            combo.AppendText(display);
        combo.Active = DefaultFpsIndex;
        combo.Hexpand = true;
        box.Append(combo);

        return (combo, box);
    }

    private static (Entry Entry, Box Container) CreateOutputDirSelector()
    {
        var box = Box.New(Orientation.Horizontal, 8);
        box.Append(Label.New("Save to:"));

        var entry = Entry.New();
        entry.SetText(DefaultOutputDir);
        entry.Hexpand = true;
        entry.TooltipText = "Directory where recordings will be saved";
        box.Append(entry);

        return (entry, box);
    }

    private (Button Record, Button Open, Button Copy, Box Container) CreatePrimaryButtons()
    {
        var box = Box.New(Orientation.Horizontal, 10);
        box.Halign = Align.Center;

        var recordButton = Button.NewWithLabel(string.Empty);
        recordButton.OnClicked += OnRecordClicked;
        box.Append(recordButton);

        var openButton = Button.NewWithLabel("Open");
        openButton.OnClicked += OnOpenClicked;
        box.Append(openButton);

        var copyButton = Button.NewWithLabel("Copy");
        copyButton.OnClicked += OnCopyClicked;
        copyButton.TooltipText = "Copy file to clipboard";
        box.Append(copyButton);

        return (recordButton, openButton, copyButton, box);
    }

    private (Button Cancel, Box Container) CreateSecondaryButtons()
    {
        var box = Box.New(Orientation.Horizontal, 10);
        box.Halign = Align.Center;

        var cancelButton = Button.NewWithLabel(string.Empty);
        cancelButton.OnClicked += OnCancelClicked;
        box.Append(cancelButton);

        return (cancelButton, box);
    }

    #endregion

    #region State Management

    /// <summary>
    /// Transitions to new state and updates UI atomically.
    /// </summary>
    private void TransitionTo(WindowState newState)
    {
        _state = newState;
        ApplyUiFromState();
    }

    /// <summary>
    /// Applies UI configuration derived from current state.
    /// Single place where all UI updates happen.
    /// </summary>
    private void ApplyUiFromState()
    {
        var config = UiConfig.FromState(_state, _region);

        _recordButton.SetLabel(config.RecordButtonLabel);
        _recordButton.Sensitive = config.RecordButtonSensitive;

        if (config.RecordButtonIsSuggested)
        {
            _recordButton.RemoveCssClass("destructive-action");
            _recordButton.AddCssClass("suggested-action");
        }
        else if (config.RecordButtonIsDestructive)
        {
            _recordButton.RemoveCssClass("suggested-action");
            _recordButton.AddCssClass("destructive-action");
        }
        else
        {
            _recordButton.RemoveCssClass("suggested-action");
            _recordButton.RemoveCssClass("destructive-action");
        }

        _statusLabel.SetLabel(config.StatusText);
        _formatCombo.Sensitive = config.FormatComboSensitive;
        _fpsCombo.Sensitive = config.FpsComboSensitive;
        _outputDirEntry.Sensitive = config.OutputDirSensitive;
        _openButton.Sensitive = config.OpenButtonSensitive;
        _copyButton.Sensitive = config.CopyButtonSensitive;
        _cancelButton.SetLabel(config.CancelButtonLabel);
        _cancelButton.Sensitive = config.CancelButtonSensitive;

        if (config.OverlayVisible)
            _overlay.Show();
        else
            _overlay.Hide();
    }

    #endregion

    #region Window Positioning

    private void OnWindowRealized(Widget sender, EventArgs args)
    {
        const int maxAttempts = 10;
        const uint intervalMs = 50;
        var attempts = 0;

        GLib.Functions.TimeoutAdd(0, intervalMs, () =>
        {
            attempts++;
            return !TryMoveToTarget() && attempts < maxAttempts;
        });
    }

    private bool TryMoveToTarget()
    {
        var surface = GetSurface();
        if (surface is null)
            return false;

        // Get the native surface handle via the Gdk.Surface's internal handle.
        // This is fragile but necessary: GTK4 removed window positioning APIs,
        // and GirCore doesn't expose the X11 surface directly.
        var xid = GetX11WindowId(surface);
        if (xid is null)
            return false;

        return WindowPositioner.TryMove(xid.Value, _targetX, _targetY);
    }

    /// <summary>
    /// Extracts X11 window ID from GDK surface.
    /// Returns null on non-X11 backends (e.g., Wayland) or if surface not ready.
    /// 
    /// Note: Uses reflection because GirCore doesn't expose the SafeHandle directly.
    /// This is the one unavoidable reflection point for X11 interop in GTK4.
    /// </summary>
    private static nuint? GetX11WindowId(Gdk.Surface surface)
    {
        try
        {
            // GirCore wraps the native handle in a SafeHandle accessible via Handle property
            var handleProperty = surface.GetType().GetProperty("Handle");
            if (handleProperty?.GetValue(surface) is not System.Runtime.InteropServices.SafeHandle safeHandle)
                return null;

            var surfacePtr = safeHandle.DangerousGetHandle();
            if (surfacePtr == 0)
                return null;

            var xid = GdkX11SurfaceGetXid(surfacePtr);
            return xid == 0 ? null : xid;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Reflection.TargetInvocationException)
        {
            // Expected on Wayland or other non-X11 backends
            return null;
        }
    }

    #endregion

    #region Event Handlers

    private async void OnRecordClicked(Button sender, EventArgs args)
    {
        // Disable button immediately to prevent double-click race conditions
        // before any state transitions or async work
        _recordButton.Sensitive = false;

        // Capture state atomically
        var currentState = _state;

        try
        {
            switch (currentState)
            {
                case WindowState.Ready:
                    StartRecording();
                    break;

                case WindowState.Recording recording:
                    // Transition to Stopping BEFORE await to prevent re-entry
                    TransitionTo(new WindowState.Stopping(recording.Recorder, recording.Fps));
                    await StopRecordingAsync(recording.Recorder, recording.Fps);
                    break;

                case WindowState.Saved:
                case WindowState.Error:
                    TransitionTo(new WindowState.Ready());
                    break;

                case WindowState.Stopping:
                case WindowState.Converting:
                    // Button should already be disabled; ignore
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording error");
            TransitionTo(new WindowState.Error(TruncateMessage(ex.Message)));
        }

        // ApplyUiFromState will set the correct sensitivity based on the new state,
        // but if we didn't transition (e.g., ignored click), restore based on current state
        ApplyUiFromState();
    }

    private static string TruncateMessage(string message, int maxLength = MaxStatusMessageLength)
    {
        if (message.Length <= maxLength)
            return message;
        return message[..(maxLength - 3)] + "...";
    }

    private void StartRecording()
    {
        var fpsIndex = _fpsCombo.Active;
        var fps = fpsIndex >= 0 && fpsIndex < FpsOptions.Length
            ? FpsOptions[fpsIndex].Value
            : FpsOptions[DefaultFpsIndex].Value;

        var recorder = new FFmpegRecorder();
        recorder.Start(_region, fps);

        TransitionTo(new WindowState.Recording(recorder, fps));
    }

    private async Task StopRecordingAsync(FFmpegRecorder recorder, int fps)
    {
        string? tempPath = null;
        CancellationTokenSource? cts = null;

        try
        {
            await recorder.StopAsync();
            tempPath = recorder.TempPath;

            var format = GetSelectedFormat();
            var outputPath = GenerateOutputPath(format);
            cts = new CancellationTokenSource();

            TransitionTo(new WindowState.Converting(tempPath, outputPath, fps, cts));

            var converter = new FFmpegConverter();
            var settings = FFmpegConverter.ConversionSettings.CreateOrThrow(format, fps);
            await converter.ConvertAsync(tempPath, outputPath, settings, ct: cts.Token);

            // Dispose CTS before transition (no longer needed after conversion)
            cts.Dispose();
            cts = null;

            TransitionTo(new WindowState.Saved(outputPath));
            _logger.LogInformation("Saved: {OutputPath}", outputPath);
        }
        catch (OperationCanceledException)
        {
            TransitionTo(new WindowState.Ready());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion failed");
            TransitionTo(new WindowState.Error(TruncateMessage(ex.Message)));
        }
        finally
        {
            cts?.Dispose();
            if (tempPath is not null)
                CleanupTempFile(tempPath);
            recorder.Dispose();
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

        // Fallback to default if empty
        if (string.IsNullOrWhiteSpace(outputDir))
            outputDir = DefaultOutputDir;

        // Expand ~ to home directory
        if (outputDir.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            outputDir = Path.Combine(home, outputDir[1..].TrimStart('/'));
        }

        // Ensure directory exists
        Directory.CreateDirectory(outputDir);

        var filename = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}{format.GetExtension()}";
        return Path.Combine(outputDir, filename);
    }

    private void CleanupTempFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
        }
    }

    private void OnOpenClicked(Button sender, EventArgs args)
    {
        if (_state is not WindowState.Saved saved)
            return;

        if (!File.Exists(saved.FilePath))
        {
            _logger.LogWarning("File not found: {FilePath}", saved.FilePath);
            return;
        }

        if (!_processRunner.StartDetached("xdg-open", [saved.FilePath]))
        {
            _logger.LogError("Failed to open file with xdg-open");
            TransitionTo(new WindowState.Error("Failed to open file"));
        }
    }

    private void OnCopyClicked(Button sender, EventArgs args)
    {
        if (_state is not WindowState.Saved saved)
            return;

        var result = ClipboardService.CopyFileToClipboard(this, saved.FilePath, _logger);

        if (result.Success)
        {
            _logger.LogInformation("Copied to clipboard: {FilePath}", saved.FilePath);
        }
        else
        {
            _logger.LogError("Failed to copy to clipboard: {Error}", result.Error);
            TransitionTo(new WindowState.Error(result.Error ?? "Copy failed"));
        }
    }

    private void OnCancelClicked(Button sender, EventArgs args)
    {
        // If converting, cancel the operation instead of closing
        if (_state is WindowState.Converting converting)
        {
            converting.Cts.Cancel();
            return;
        }

        Cleanup();
        Close();
    }

    #endregion

    #region Cleanup

    private void Cleanup()
    {
        if (Interlocked.Exchange(ref _cleanedUp, 1) != 0)
            return;

        _overlay.Dispose();

        switch (_state)
        {
            case WindowState.Recording recording:
                recording.Recorder.Dispose();
                break;
            case WindowState.Stopping stopping:
                stopping.Recorder.Dispose();
                break;
            case WindowState.Converting converting:
                converting.Cts.Cancel();
                converting.Cts.Dispose();
                break;
        }

        _state = new WindowState.Ready();
    }

    #endregion
}
