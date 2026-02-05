using System.Runtime.Versioning;
using GifMaker.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.Screenshot;

/// <summary>
/// Captures screenshots using FFmpeg x11grab (single frame).
/// Thread-safe, testable via process abstraction.
/// </summary>
[SupportedOSPlatform("linux")]
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

        /// <summary>
        /// Fixed cursor position (screen coordinates) when ShowPointer is true.
        /// If null, captures cursor at current position (live).
        /// </summary>
        public (int X, int Y)? FixedCursorPosition { get; }

        private CaptureSettings(Rectangle region, string display, bool showPointer, (int, int)? fixedCursorPosition)
        {
            Region = region;
            Display = display;
            ShowPointer = showPointer;
            FixedCursorPosition = fixedCursorPosition;
        }

        /// <summary>
        /// Creates validated settings.
        /// </summary>
        /// <param name="region">Screen region to capture.</param>
        /// <param name="showPointer">Whether to show cursor.</param>
        /// <param name="display">X11 display (defaults to $DISPLAY or :0).</param>
        /// <param name="fixedCursorPosition">Fixed cursor position; if null and showPointer is true, uses live position.</param>
        public static Result<CaptureSettings> Create(
            Rectangle region,
            bool showPointer = false,
            string? display = null,
            (int X, int Y)? fixedCursorPosition = null)
        {
            if (!region.IsValid)
                return Result<CaptureSettings>.Fail("Region must have positive dimensions");

            if (region.Width < MinDimension || region.Height < MinDimension)
                return Result<CaptureSettings>.Fail($"Region too small (min {MinDimension}x{MinDimension})");

            var resolvedDisplay = display ?? Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
            return Result<CaptureSettings>.Ok(new CaptureSettings(region, resolvedDisplay, showPointer, fixedCursorPosition));
        }

        /// <summary>
        /// Creates settings, throwing if invalid.
        /// </summary>
        public static CaptureSettings CreateOrThrow(
            Rectangle region,
            bool showPointer = false,
            string? display = null,
            (int X, int Y)? fixedCursorPosition = null)
        {
            return Create(region, showPointer, display, fixedCursorPosition).Match(
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
    /// <exception cref="ArgumentException">Thrown when <paramref name="outputPath"/> is null or whitespace.</exception>
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

        // If fixed cursor position specified, we capture without cursor then overlay
        if (settings.ShowPointer && settings.FixedCursorPosition is { } cursorPos)
        {
            // Convert screen coords to region-relative coords
            var relX = cursorPos.X - region.X;
            var relY = cursorPos.Y - region.Y;

            // Only draw cursor if it's within the captured region
            if (relX >= 0 && relX < region.Width && relY >= 0 && relY < region.Height)
            {
                // Draw a simple arrow cursor using FFmpeg drawbox filters
                // Arrow pointing top-left: main body + diagonal line
                var filter = BuildCursorFilter(relX, relY);

                return
                [
                    "-y",
                    "-f", "x11grab",
                    "-draw_mouse", "0",      // Don't draw live cursor
                    "-video_size", videoSize,
                    "-i", input,
                    "-vf", filter,
                    "-frames:v", "1",
                    "-update", "1",
                    outputPath
                ];
            }
        }

        // Standard capture (with or without live cursor)
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

    /// <summary>
    /// Builds FFmpeg filter to draw a simple arrow cursor at the specified position.
    /// </summary>
    /// <remarks>
    /// Draws a standard arrow cursor shape: triangular pointer with black outline and white fill.
    /// The cursor is 11x18 pixels, matching typical system cursor size.
    /// </remarks>
    private static string BuildCursorFilter(int x, int y)
    {
        // Arrow cursor row widths (pixels per row from tip to tail)
        // Standard pointer: grows 1px/row, then has notch for tail
        ReadOnlySpan<int> rowWidths =
        [
            1,  // row 0: tip
            2,  // row 1
            3,  // row 2
            4,  // row 3
            5,  // row 4
            6,  // row 5
            7,  // row 6
            8,  // row 7
            9,  // row 8
            10, // row 9
            11, // row 10: widest
            6,  // row 11: notch (tail starts)
            7,  // row 12
            4,  // row 13: tail
            3,  // row 14
            2,  // row 15
            2,  // row 16
            1,  // row 17: tail end
        ];

        var filters = new List<string>(rowWidths.Length * 3 + 4);

        // Draw cursor row by row: black outline with white fill
        for (var row = 0; row < rowWidths.Length; row++)
        {
            var width = rowWidths[row];
            var rowY = y + row;

            // Black outline on left edge
            filters.Add($"drawbox=x={x}:y={rowY}:w=1:h=1:c=black:t=fill");

            // White fill in the middle
            if (width > 2)
                filters.Add($"drawbox=x={x + 1}:y={rowY}:w={width - 2}:h=1:c=white:t=fill");

            // Black outline on right edge (diagonal)
            if (width > 1)
                filters.Add($"drawbox=x={x + width - 1}:y={rowY}:w=1:h=1:c=black:t=fill");
        }

        // Bottom of main pointer (row 10, before notch) - close the triangle
        filters.Add($"drawbox=x={x + 5}:y={y + 10}:w=6:h=1:c=black:t=fill");

        // Inner edges of the notch (rows 11-12)
        filters.Add($"drawbox=x={x + 5}:y={y + 11}:w=1:h=1:c=black:t=fill");
        filters.Add($"drawbox=x={x + 5}:y={y + 12}:w=1:h=1:c=black:t=fill");

        return string.Join(",", filters);
    }
}
