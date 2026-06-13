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
    private readonly ILogger<ScreenshotService> _logger;
    private readonly IProcessRunner _processRunner;
    private readonly ScreenshotCapture _capture;
    private readonly WindowGeometry _windowGeometry;

    /// <summary>
    /// Result of a screenshot operation.
    /// </summary>
    public readonly record struct ScreenshotResult(
        string FilePath,
        ScreenRegion Region,
        CaptureMode Mode);

    /// <summary>
    /// Creates a ScreenshotService with optional dependencies.
    /// </summary>
    public ScreenshotService(
        ILogger<ScreenshotService>? logger = null,
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
    /// <returns>Region to capture, or error.</returns>
    public async Task<Result<ScreenRegion>> GetRegionAsync(CaptureMode mode, CancellationToken ct = default)
    {
        return mode switch
        {
            CaptureMode.Selection => await GetSelectionRegionAsync(ct).ConfigureAwait(false),
            CaptureMode.Screen => GetFullScreenRegion(),
            CaptureMode.Window => await _windowGeometry.GetActiveWindowAsync(ct).ConfigureAwait(false),
            _ => Result<ScreenRegion>.Fail($"Unknown capture mode: {mode}")
        };
    }

    public async Task<Result<ScreenshotResult>> CaptureAsync(ScreenshotRequest request, CancellationToken ct = default)
    {
        var outputPath = request.OutputTarget.ResolvePath();

        var settingsResult = ScreenshotCapture.CaptureSettings.Create(
            request.Region,
            request.ShowPointer,
            fixedCursorPosition: request.FixedCursorPosition);

        return await settingsResult.Match<Task<Result<ScreenshotResult>>>(
            async settings =>
            {
                var captureResult = await _capture.CaptureAsync(settings, outputPath, ct)
                    .ConfigureAwait(false);

                return captureResult.Match(
                    path => Result<ScreenshotResult>.Ok(new ScreenshotResult(path, request.Region, request.Mode)),
                    error => Result<ScreenshotResult>.Fail(error));
            },
            error => Task.FromResult(Result<ScreenshotResult>.Fail(error)))
            .ConfigureAwait(false);
    }

    private async Task<Result<ScreenRegion>> GetSelectionRegionAsync(CancellationToken ct)
    {
        using var selector = new AreaSelector(_processRunner);
        return await selector.SelectAsync(ct).ConfigureAwait(false);
    }

    private static Result<ScreenRegion> GetFullScreenRegion()
    {
        var bounds = WindowPositioner.GetScreenBounds();
        if (bounds is null)
            return Result<ScreenRegion>.Fail("Failed to get screen bounds");

        return ScreenRegion.Create(0, 0, bounds.Value.Width, bounds.Value.Height);
    }
}
