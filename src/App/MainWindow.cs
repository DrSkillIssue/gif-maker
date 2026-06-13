using System.Runtime.Versioning;
using Gtk;
using GifMaker.Core;
using GifMaker.Screenshot;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Main application window with tabbed Record and Screenshot surfaces.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class MainWindow : Window
{
    private const int WindowWidth = 420;
    private const int WindowHeight = 350;
    private const int MaxStatusLength = 80;

    private readonly AppServices _services;
    private readonly ILogger<MainWindow> _logger;
    private readonly Stack _stack;
    private readonly RecordPage _recordPage;
    private readonly ScreenshotPage _screenshotPage;
    private readonly X11Desktop _desktop = new();

    private RecordingSession? _recordingSession;
    private ScreenshotSession? _screenshotSession;
    private RecordViewState _recordState = new RecordViewState.Idle();
    private ScreenshotViewState _screenshotState = new ScreenshotViewState.Idle();
    private ScreenPoint? _savedPosition;

    internal MainWindow(
        Application app,
        AppServices services,
        ILogger<MainWindow>? logger = null)
    {
        _services = services;
        _logger = logger ?? NullLogger<MainWindow>.Instance;

        Application = app;
        Title = "GIF Maker";
        SetDefaultSize(WindowWidth, WindowHeight);
        Resizable = true;

        var headerBar = HeaderBar.New();
        var stackSwitcher = StackSwitcher.New();
        headerBar.SetTitleWidget(stackSwitcher);
        SetTitlebar(headerBar);

        _stack = Stack.New();
        _stack.SetTransitionType(StackTransitionType.SlideLeftRight);
        _stack.SetTransitionDuration(200);
        stackSwitcher.SetStack(_stack);

        _recordPage = new RecordPage();
        _screenshotPage = new ScreenshotPage();
        _recordPage.IntentRaised += intent => _ = HandleRecordIntentAsync(intent);
        _screenshotPage.IntentRaised += intent => _ = HandleScreenshotIntentAsync(intent);

        _stack.AddTitled(_recordPage, "record", "Record");
        _stack.AddTitled(_screenshotPage, "screenshot", "Screenshot");
        Child = _stack;

        OnShow += OnWindowShown;
        OnDestroy += (_, _) => DisposeOwnedState();
    }

    public void Render(AppViewState state)
    {
        if (state is not AppViewState.ActiveTab active)
            throw new InvalidOperationException($"Unhandled app view state: {state.GetType().Name}");

        _stack.SetVisibleChildName(active.Tab switch
        {
            AppTab.Record => "record",
            AppTab.Screenshot => "screenshot",
            _ => throw new InvalidOperationException($"Unhandled app tab: {active.Tab}")
        });
    }

    public void CaptureSelection() =>
        _ = HandleScreenshotIntentAsync(new ScreenshotIntent.CaptureSelection(_screenshotPage.PointerCapture));

    public WindowVisibilityLease HideForExternalSelection()
    {
        var surface = GetSurface();
        if (surface is not null)
        {
            _desktop.GetWindowLocation(surface).Match(
                position => _savedPosition = position,
                error => _logger.LogDebug("Failed to save window position: {Error}", error));
        }

        Hide();
        return new WindowVisibilityLease(RestoreAfterExternalSelection);
    }

    private async Task HandleRecordIntentAsync(RecordIntent intent)
    {
        try
        {
            switch (intent)
            {
                case RecordIntent.ToggleRecording:
                    await ToggleRecordingAsync();
                    break;

                case RecordIntent.CancelConversion:
                    _recordingSession?.CancelConversion();
                    break;

                case RecordIntent.OpenSaved:
                    if (_recordState is RecordViewState.Saved openRecording)
                    {
                        _services.DesktopFiles.Open(openRecording.Media).Match(
                            _ => { },
                            error => RenderRecord(new RecordViewState.Error(error)));
                    }
                    break;

                case RecordIntent.CopySaved:
                    if (_recordState is RecordViewState.Saved copiedRecording)
                    {
                        _services.DesktopFiles.CopyToClipboard(this, copiedRecording.Media).Match(
                            _ => { },
                            error => RenderRecord(new RecordViewState.Error(error)));
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled record intent: {intent.GetType().Name}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Record workflow failed");
            RenderRecord(new RecordViewState.Error(Truncate(ex.Message)));
        }
    }

    private async Task ToggleRecordingAsync()
    {
        switch (_recordState)
        {
            case RecordViewState.Idle:
                RenderRecord(new RecordViewState.Selecting());
                var startSession = _recordingSession ??= _services.CreateRecordingSession();
                var startResult = await startSession.StartFromSelectionAsync(
                    _recordPage.ReadStartOptions(),
                    HideForExternalSelection());
                startResult.Match(
                    RenderRecord,
                    error => RenderRecord(new RecordViewState.Error(Truncate(error))));
                break;

            case RecordViewState.Recording recording:
                if (_recordingSession is null)
                {
                    RenderRecord(new RecordViewState.Error("No recording in progress"));
                    break;
                }

                RenderRecord(new RecordViewState.Stopping(recording.Fps));
                RenderRecord(new RecordViewState.Converting());
                var result = await _recordingSession.StopAndConvertAsync(_recordPage.ReadExportTarget());
                result.Match(
                    RenderRecord,
                    error => RenderRecord(new RecordViewState.Error(Truncate(error))));
                break;

            case RecordViewState.Saved:
            case RecordViewState.Error:
                ResetRecording();
                break;
        }
    }

    private async Task HandleScreenshotIntentAsync(ScreenshotIntent intent)
    {
        try
        {
            switch (intent)
            {
                case ScreenshotIntent.CaptureSelection selection:
                    await CaptureScreenshotSelectionAsync(selection.Pointer);
                    break;

                case ScreenshotIntent.CaptureScreen screen:
                    await CaptureScreenAsync(screen.Pointer);
                    break;

                case ScreenshotIntent.CaptureWindow window:
                    await CaptureWindowAsync(window.Pointer);
                    break;

                case ScreenshotIntent.OpenSaved:
                    if (_screenshotState is ScreenshotViewState.Saved openScreenshot)
                    {
                        _services.DesktopFiles.Open(openScreenshot.Media).Match(
                            _ => { },
                            error => RenderScreenshot(new ScreenshotViewState.Error(error)));
                    }
                    break;

                case ScreenshotIntent.OpenContainingFolder:
                    if (_screenshotState is ScreenshotViewState.Saved screenshotFolder)
                    {
                        _services.DesktopFiles.OpenContainingFolder(screenshotFolder.Media).Match(
                            _ => { },
                            error => RenderScreenshot(new ScreenshotViewState.Error(error)));
                    }
                    break;

                case ScreenshotIntent.CopySaved:
                    if (_screenshotState is ScreenshotViewState.Saved copiedScreenshot)
                    {
                        _services.DesktopFiles.CopyToClipboard(this, copiedScreenshot.Media).Match(
                            _ => { },
                            error => RenderScreenshot(new ScreenshotViewState.Error(error)));
                    }
                    break;

                case ScreenshotIntent.Reset:
                    RenderScreenshot(new ScreenshotViewState.Idle());
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled screenshot intent: {intent.GetType().Name}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Screenshot workflow failed");
            RenderScreenshot(new ScreenshotViewState.Error(Truncate(ex.Message)));
        }
    }

    private async Task CaptureScreenshotSelectionAsync(ScreenPointerCapture pointer)
    {
        if (_screenshotState is ScreenshotViewState.Selecting or ScreenshotViewState.Capturing)
            return;

        RenderScreenshot(new ScreenshotViewState.Selecting());

        var session = _screenshotSession ??= _services.CreateScreenshotSession();
        var result = await session.CaptureSelectionAsync(
            pointer,
            HideForExternalSelection());
        result.Match(
            RenderScreenshot,
            error => RenderScreenshot(new ScreenshotViewState.Error(Truncate(error))));

        if (_screenshotState is ScreenshotViewState.Saved saved)
        {
            var copyResult = _services.DesktopFiles.CopyToClipboard(this, saved.Media);
            if (copyResult.IsSuccess)
                _logger.LogInformation("Screenshot copied to clipboard");
        }
    }

    private async Task CaptureScreenAsync(ScreenPointerCapture pointer)
    {
        if (_screenshotState is ScreenshotViewState.Selecting or ScreenshotViewState.Capturing)
            return;

        RenderScreenshot(new ScreenshotViewState.Capturing(ScreenshotCaptureKind.Screen));

        var session = _screenshotSession ??= _services.CreateScreenshotSession();
        var capture = new ScreenshotCapture.FullScreen(
            pointer,
            ScreenshotDestination.Default);
        var result = await session.CaptureAsync(capture);
        result.Match(
            RenderScreenshot,
            error => RenderScreenshot(new ScreenshotViewState.Error(Truncate(error))));

        if (_screenshotState is ScreenshotViewState.Saved saved)
        {
            var copyResult = _services.DesktopFiles.CopyToClipboard(this, saved.Media);
            if (copyResult.IsSuccess)
                _logger.LogInformation("Screenshot copied to clipboard");
        }
    }

    private async Task CaptureWindowAsync(ScreenPointerCapture pointer)
    {
        if (_screenshotState is ScreenshotViewState.Selecting or ScreenshotViewState.Capturing)
            return;

        RenderScreenshot(new ScreenshotViewState.Capturing(ScreenshotCaptureKind.Window));

        var session = _screenshotSession ??= _services.CreateScreenshotSession();
        var capture = new ScreenshotCapture.ActiveWindow(
            pointer,
            ScreenshotDestination.Default);
        var result = await session.CaptureAsync(capture);
        result.Match(
            RenderScreenshot,
            error => RenderScreenshot(new ScreenshotViewState.Error(Truncate(error))));

        if (_screenshotState is ScreenshotViewState.Saved saved)
        {
            var copyResult = _services.DesktopFiles.CopyToClipboard(this, saved.Media);
            if (copyResult.IsSuccess)
                _logger.LogInformation("Screenshot copied to clipboard");
        }
    }

    private void RenderRecord(RecordViewState state)
    {
        _recordState = state;
        _recordPage.Render(state);
    }

    private void RenderScreenshot(ScreenshotViewState state)
    {
        _screenshotState = state;
        _screenshotPage.Render(state);
    }

    private void ResetRecording()
    {
        _recordingSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _recordingSession = null;
        RenderRecord(new RecordViewState.Idle());
    }

    private void RestoreAfterExternalSelection()
    {
        Present();

        if (_savedPosition is { } position)
        {
            var surface = GetSurface();
            if (surface is not null)
            {
                _desktop.MoveWindow(surface, position).Match(
                    () => { },
                    error => _logger.LogDebug("Failed to restore window position: {Error}", error));
            }
        }
    }

    private void OnWindowShown(Widget sender, EventArgs args)
    {
        OnShow -= OnWindowShown;

        _desktop.GetScreenSize().Match(
            screen =>
            {
                var surface = GetSurface();
                if (surface is null)
                    return;

                var point = new ScreenPoint(
                    (screen.Width - WindowWidth) / 2,
                    (screen.Height - WindowHeight) / 2);

                _desktop.MoveWindow(surface, point).Match(
                    () => { },
                    error => _logger.LogDebug("Failed to center window: {Error}", error));
            },
            error => _logger.LogDebug("Failed to read screen size: {Error}", error));
    }

    private void DisposeOwnedState()
    {
        _recordingSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _recordingSession = null;
        _screenshotSession = null;
        _recordPage.Dispose();
        _screenshotPage.Dispose();
    }

    private static string Truncate(string message) =>
        message.Length <= MaxStatusLength ? message : message[..(MaxStatusLength - 3)] + "...";
}
