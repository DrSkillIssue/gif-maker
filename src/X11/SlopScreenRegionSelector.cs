using System.Globalization;
using System.Text.RegularExpressions;
using GifMaker.Core;

namespace GifMaker.X11;

public sealed partial class SlopScreenRegionSelector
{
    public const string SelectionCancelled = "Selection cancelled";

    private readonly IProcessRunner _processRunner;

    [GeneratedRegex(@"(\d+)x(\d+)\+(\d+)\+(\d+)")]
    private static partial Regex GeometryRegex();

    public SlopScreenRegionSelector(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
    }

    public async Task<Result<ScreenRegion>> SelectAsync(CancellationToken ct = default)
    {
        string[] args = ["-f", "%wx%h+%x+%y", "-b", "4", "-c", "1,0.2,0.2,0.8", "-o"];

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                new ProcessCommand("slop", args, ProcessIo.Capture), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<ScreenRegion>.Fail($"Failed to start slop: {ex.Message}");
        }

        if (result.ExitCode == 1)
            return Result<ScreenRegion>.Fail(SelectionCancelled);

        if (result.ExitCode != 0)
            return Result<ScreenRegion>.Fail($"slop failed with exit code {result.ExitCode}: {result.StandardError}");

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            return Result<ScreenRegion>.Fail("slop produced no output");

        var match = GeometryRegex().Match(result.StandardOutput);
        if (!match.Success)
            return Result<ScreenRegion>.Fail($"Invalid slop output format: {result.StandardOutput}");

        if (!int.TryParse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var height) ||
            !int.TryParse(match.Groups[3].ValueSpan, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(match.Groups[4].ValueSpan, CultureInfo.InvariantCulture, out var y))
        {
            return Result<ScreenRegion>.Fail($"Invalid numeric values in slop output: {result.StandardOutput}");
        }

        return ScreenRegion.Create(x, y, width, height).Match(
            Result<ScreenRegion>.Ok,
            _ => Result<ScreenRegion>.Fail("Selected region has zero area"));
    }
}
