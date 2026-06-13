using System.Runtime.Versioning;
using GifMaker.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Main GTK application composition root.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GifMakerApp : IDisposable
{
    private const string AppId = "com.gifmaker.app";

    private readonly Gtk.Application _app;
    private readonly GifMakerShell _shell;
    private readonly ILogger<GifMakerApp> _logger;
    private int _disposed;

    private GifMakerApp(ILogger<GifMakerApp>? logger)
    {
        _logger = logger ?? NullLogger<GifMakerApp>.Instance;
        var processRunner = ProcessRunner.Default;
        var desktopFiles = new DesktopFileActions(processRunner);
        var services = new AppServices(
            CreateRecordingSession: () => new RecordingSession(processRunner),
            CreateScreenshotSession: () => new ScreenshotSession(processRunner),
            DesktopFiles: desktopFiles);

        _app = Gtk.Application.New(AppId, Gio.ApplicationFlags.FlagsNone);
        _shell = new GifMakerShell(_app, services);
    }

    /// <summary>
    /// Creates a new GifMakerApp instance.
    /// </summary>
    public static GifMakerApp Create(ILogger<GifMakerApp>? logger = null) => new(logger);

    /// <summary>
    /// Runs the application. Blocks until exit.
    /// </summary>
    public int Run()
    {
        ThrowIfDisposed();
        _logger.LogDebug("Starting GifMaker application");
        return _app.RunWithSynchronizationContext(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _logger.LogDebug("Disposing GifMaker application");
        _shell.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
