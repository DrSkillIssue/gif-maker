using System.Runtime.Versioning;
using GifMaker.Core;
using GifMaker.X11;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.Screenshot;

/// <summary>
/// High-level screenshot service coordinating capture, save, and clipboard.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ScreenshotService
{
    private static readonly string DefaultOutputDir = GetDefaultOutputDir();

    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;
    private readonly ScreenshotCapture _capture;
    private readonly WindowGeometry _windowGeometry;

    /// <summary>
    /// Result of a screenshot operation.
    /// </summary>
    public readonly record struct ScreenshotResult(
        string FilePath,
        Rectangle Region,
        CaptureMode Mode);

    /// <summary>
    /// Creates a ScreenshotService with optional dependencies.
    /// </summary>
    public ScreenshotService(
        ILogger? logger = null,
        IProcessRunner? processRunner = null)
    {
        _logger = logger ?? NullLogger<ScreenshotService>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;
        _capture = new ScreenshotCapture(logger: null, _processRunner);
        _windowGeometry = new WindowGeometry(_processRunner);
    }

    /// <summary>
    /// Gets the region for the specified capture mode.
    /// </summary>
    /// <param name="mode">Capture mode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rectangle to capture, or error.</returns>
    public async Task<Result<Rectangle>> GetRegionAsync(CaptureMode mode, CancellationToken ct = default)
    {
        return mode switch
        {
            CaptureMode.Selection => await GetSelectionRegionAsync(ct).ConfigureAwait(false),
            CaptureMode.Screen => GetFullScreenRegion(),
            CaptureMode.Window => await _windowGeometry.GetActiveWindowAsync(ct).ConfigureAwait(false),
            _ => Result<Rectangle>.Fail($"Unknown capture mode: {mode}")
        };
    }

    /// <summary>
    /// Captures a screenshot with the specified settings.
    /// </summary>
    /// <param name="region">Region to capture.</param>
    /// <param name="mode">Capture mode (for result metadata).</param>
    /// <param name="showPointer">Whether to include mouse pointer.</param>
    /// <param name="fixedCursorPosition">Fixed cursor position for selection mode (captured before selection).</param>
    /// <param name="outputDir">Output directory (defaults to ~/Pictures/Screenshots).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Screenshot result with file path.</returns>
    public async Task<Result<ScreenshotResult>> CaptureAsync(
        Rectangle region,
        CaptureMode mode,
        bool showPointer = false,
        (int X, int Y)? fixedCursorPosition = null,
        string? outputDir = null,
        CancellationToken ct = default)
    {
        var dir = outputDir ?? DefaultOutputDir;
        Directory.CreateDirectory(dir);

        var filename = GenerateFilename();
        var outputPath = Path.Combine(dir, filename);

        var settingsResult = ScreenshotCapture.CaptureSettings.Create(
            region, showPointer, fixedCursorPosition: fixedCursorPosition);

        return await settingsResult.Match<Task<Result<ScreenshotResult>>>(
            async settings =>
            {
                var captureResult = await _capture.CaptureAsync(settings, outputPath, ct)
                    .ConfigureAwait(false);

                return captureResult.Match(
                    path => Result<ScreenshotResult>.Ok(new ScreenshotResult(path, region, mode)),
                    error => Result<ScreenshotResult>.Fail(error));
            },
            error => Task.FromResult(Result<ScreenshotResult>.Fail(error)))
            .ConfigureAwait(false);
    }

    private async Task<Result<Rectangle>> GetSelectionRegionAsync(CancellationToken ct)
    {
        using var selector = new AreaSelector(_processRunner);
        return await selector.SelectAsync(ct).ConfigureAwait(false);
    }

    private static Result<Rectangle> GetFullScreenRegion()
    {
        var bounds = WindowPositioner.GetScreenBounds();
        if (bounds is null)
            return Result<Rectangle>.Fail("Failed to get screen bounds");

        return Result<Rectangle>.Ok(new Rectangle(0, 0, bounds.Value.Width, bounds.Value.Height));
    }

    private static string GenerateFilename() =>
        $"ss_{DateTime.Now:yyyyMMdd_HHmmss}.png";

    private static string GetDefaultOutputDir()
    {
        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(picturesDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            picturesDir = Path.Combine(home, "Pictures");
        }
        return Path.Combine(picturesDir, "Screenshots");
    }
}
