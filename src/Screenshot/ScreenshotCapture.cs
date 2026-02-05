using System.Globalization;
using GifMaker.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.Screenshot;

/// <summary>
/// Captures screenshots using FFmpeg x11grab (single frame).
/// Thread-safe, testable via process abstraction.
/// </summary>
public sealed class ScreenshotCapture
{
    private const string FFmpegPath = "ffmpeg";
    private const int MinDimension = 1;

    private readonly ILogger<ScreenshotCapture> _logger;
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// Creates a ScreenshotCapture with optional dependencies.
    /// </summary>
    public ScreenshotCapture(
        ILogger<ScreenshotCapture>? logger = null,
        IProcessRunner? processRunner = null)
    {
        _logger = logger ?? NullLogger<ScreenshotCapture>.Instance;
        _processRunner = processRunner ?? ProcessRunner.Default;
    }

    /// <summary>
    /// Validated screenshot settings.
    /// </summary>
    public readonly record struct CaptureSettings
    {
        /// <summary>Screen region to capture.</summary>
        public Rectangle Region { get; }

        /// <summary>X11 display identifier.</summary>
        public string Display { get; }

        /// <summary>Whether to include mouse pointer in capture.</summary>
        public bool ShowPointer { get; }

        private CaptureSettings(Rectangle region, string display, bool showPointer)
        {
            Region = region;
            Display = display;
            ShowPointer = showPointer;
        }

        /// <summary>
        /// Creates validated settings.
        /// </summary>
        public static Result<CaptureSettings> Create(
            Rectangle region,
            bool showPointer = false,
            string? display = null)
        {
            if (!region.IsValid)
                return Result<CaptureSettings>.Fail("Region must have positive dimensions");

            if (region.Width < MinDimension || region.Height < MinDimension)
                return Result<CaptureSettings>.Fail($"Region too small (min {MinDimension}x{MinDimension})");

            var resolvedDisplay = display ?? Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
            return Result<CaptureSettings>.Ok(new CaptureSettings(region, resolvedDisplay, showPointer));
        }

        /// <summary>
        /// Creates settings, throwing if invalid.
        /// </summary>
        public static CaptureSettings CreateOrThrow(
            Rectangle region,
            bool showPointer = false,
            string? display = null)
        {
            return Create(region, showPointer, display).Match(
                s => s,
                error => throw new ArgumentException(error));
        }
    }

    /// <summary>
    /// Captures a screenshot to the specified output path.
    /// </summary>
    /// <param name="settings">Capture settings.</param>
    /// <param name="outputPath">Path to save PNG file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with file path on success, error on failure.</returns>
    public async Task<Result<string>> CaptureAsync(
        CaptureSettings settings,
        string outputPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = BuildArguments(settings, outputPath);

        _logger.LogDebug(
            "Capturing screenshot: {Width}x{Height} at ({X},{Y}) on {Display}",
            settings.Region.Width, settings.Region.Height,
            settings.Region.X, settings.Region.Y, settings.Display);

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(FFmpegPath, args, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start FFmpeg for screenshot");
            return Result<string>.Fail($"Failed to start FFmpeg: {ex.Message}");
        }

        if (result.ExitCode != 0)
        {
            _logger.LogError("FFmpeg screenshot failed with exit {ExitCode}: {Stderr}",
                result.ExitCode, result.StandardError);
            return Result<string>.Fail($"Screenshot capture failed: {result.StandardError}");
        }

        if (!File.Exists(outputPath))
        {
            _logger.LogError("FFmpeg did not produce output file");
            return Result<string>.Fail("Screenshot file was not created");
        }

        _logger.LogInformation("Screenshot saved: {OutputPath}", outputPath);
        return Result<string>.Ok(outputPath);
    }

    /// <summary>
    /// Convenience overload - captures a region to specified path.
    /// </summary>
    public Task<Result<string>> CaptureAsync(
        Rectangle region,
        string outputPath,
        bool showPointer = false,
        CancellationToken ct = default)
    {
        var settingsResult = CaptureSettings.Create(region, showPointer);
        return settingsResult.Match(
            settings => CaptureAsync(settings, outputPath, ct),
            error => Task.FromResult(Result<string>.Fail(error)));
    }

    private static string[] BuildArguments(CaptureSettings settings, string outputPath)
    {
        var region = settings.Region;
        var videoSize = $"{region.Width}x{region.Height}";
        var input = $"{settings.Display}+{region.X},{region.Y}";

        // -draw_mouse: 1 = show pointer, 0 = hide
        var drawMouse = settings.ShowPointer ? "1" : "0";

        return
        [
            "-y",                    // Overwrite output
            "-f", "x11grab",         // X11 screen capture
            "-draw_mouse", drawMouse,
            "-video_size", videoSize,
            "-i", input,
            "-frames:v", "1",        // Capture single frame
            "-update", "1",          // Single image output mode
            outputPath
        ];
    }
}
