using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GifMaker.Core;

namespace GifMaker.Screenshot;

/// <summary>
/// Gets geometry of the currently focused window using xdotool.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class WindowGeometry
{
    private readonly IProcessRunner _processRunner;

    // Match xdotool getwindowgeometry output:
    // Window 12345678
    //   Position: 100,200 (screen: 0)
    //   Geometry: 800x600
    [GeneratedRegex(@"Position:\s*(\d+),(\d+)")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"Geometry:\s*(\d+)x(\d+)")]
    private static partial Regex GeometryRegex();

    /// <summary>
    /// Creates WindowGeometry with default process runner.
    /// </summary>
    public WindowGeometry() : this(ProcessRunner.Default) { }

    /// <summary>
    /// Creates WindowGeometry with custom process runner (for testing).
    /// </summary>
    public WindowGeometry(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }

    /// <summary>
    /// Gets the geometry of the currently focused window.
    /// </summary>
    /// <returns>Region of the focused window, or error if unavailable.</returns>
    public async Task<Result<ScreenRegion>> GetActiveWindowAsync(CancellationToken ct = default)
    {
        // Get active window ID
        var idResult = await _processRunner.RunAsync(
            new ProcessCommand("xdotool", ["getactivewindow"], ProcessIo.Capture), ct).ConfigureAwait(false);

        if (idResult.ExitCode != 0)
            return Result<ScreenRegion>.Fail("Failed to get active window (is xdotool installed?)");

        var windowId = idResult.StandardOutput.Trim();
        if (string.IsNullOrEmpty(windowId))
            return Result<ScreenRegion>.Fail("No active window found");

        // Get window geometry
        var geoResult = await _processRunner.RunAsync(
            new ProcessCommand("xdotool", ["getwindowgeometry", windowId], ProcessIo.Capture), ct).ConfigureAwait(false);

        if (geoResult.ExitCode != 0)
            return Result<ScreenRegion>.Fail($"Failed to get window geometry: {geoResult.StandardError}");

        return ParseGeometry(geoResult.StandardOutput);
    }

    private static Result<ScreenRegion> ParseGeometry(string output)
    {
        var posMatch = PositionRegex().Match(output);
        var geoMatch = GeometryRegex().Match(output);

        if (!posMatch.Success || !geoMatch.Success)
            return Result<ScreenRegion>.Fail($"Failed to parse window geometry: {output}");

        // Use TryParse with ValueSpan to avoid allocation and handle overflow
        if (!int.TryParse(posMatch.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(posMatch.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(geoMatch.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(geoMatch.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var height))
        {
            return Result<ScreenRegion>.Fail($"Invalid numeric values in geometry: {output}");
        }

        return ScreenRegion.Create(x, y, width, height).Match(
            Result<ScreenRegion>.Ok,
            _ => Result<ScreenRegion>.Fail("Window has zero area"));
    }
}
