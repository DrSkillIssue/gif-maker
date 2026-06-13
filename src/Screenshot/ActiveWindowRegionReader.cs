using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GifMaker.Core;

namespace GifMaker.Screenshot;

/// <summary>
/// Reads the active X11 window region through xdotool.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class ActiveWindowRegionReader
{
    private readonly IProcessRunner _processRunner;

    [GeneratedRegex(@"Position:\s*(\d+),(\d+)")]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"Geometry:\s*(\d+)x(\d+)")]
    private static partial Regex GeometryRegex();

    public ActiveWindowRegionReader(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }

    public async Task<Result<ScreenRegion>> ReadAsync(CancellationToken ct = default)
    {
        var idResult = await _processRunner.RunAsync(
            new ProcessCommand("xdotool", ["getactivewindow"], ProcessIo.Capture), ct).ConfigureAwait(false);

        if (idResult.ExitCode != 0)
            return Result<ScreenRegion>.Fail("Failed to get active window (is xdotool installed?)");

        var windowId = idResult.StandardOutput.Trim();
        if (string.IsNullOrEmpty(windowId))
            return Result<ScreenRegion>.Fail("No active window found");

        var geometryResult = await _processRunner.RunAsync(
            new ProcessCommand("xdotool", ["getwindowgeometry", windowId], ProcessIo.Capture), ct).ConfigureAwait(false);

        if (geometryResult.ExitCode != 0)
            return Result<ScreenRegion>.Fail($"Failed to get window geometry: {geometryResult.StandardError}");

        return ParseGeometry(geometryResult.StandardOutput);
    }

    private static Result<ScreenRegion> ParseGeometry(string output)
    {
        var position = PositionRegex().Match(output);
        var geometry = GeometryRegex().Match(output);

        if (!position.Success || !geometry.Success)
            return Result<ScreenRegion>.Fail($"Failed to parse window geometry: {output}");

        if (!int.TryParse(position.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(position.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var y) ||
            !int.TryParse(geometry.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(geometry.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var height))
        {
            return Result<ScreenRegion>.Fail($"Invalid numeric values in geometry: {output}");
        }

        return ScreenRegion.Create(x, y, width, height).Match(
            Result<ScreenRegion>.Ok,
            _ => Result<ScreenRegion>.Fail("Window has zero area"));
    }
}
