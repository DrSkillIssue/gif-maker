using System.Runtime.Versioning;
using GifMaker.Core;
using GifMaker.X11;

namespace GifMaker.Screenshot;

/// <summary>
/// Captures screenshots using FFmpeg x11grab.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class FfmpegScreenshotCapture
{
    private const string FFmpegPath = "ffmpeg";

    private readonly IProcessRunner _processRunner;

    public FfmpegScreenshotCapture(IProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
    }

    public async Task<Result<ScreenshotFile>> CaptureAsync(
        ResolvedScreenshotFrame frame,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(frame.OutputFile.Path))
            return Result<ScreenshotFile>.Fail("Screenshot output path is required");

        var command = BuildCommand(frame);

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(command, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<ScreenshotFile>.Fail($"Failed to start FFmpeg: {ex.Message}");
        }

        if (result.ExitCode != 0)
            return Result<ScreenshotFile>.Fail($"Screenshot capture failed: {result.StandardError}");

        if (!System.IO.File.Exists(frame.OutputFile.Path))
            return Result<ScreenshotFile>.Fail("Screenshot file was not created");

        return Result<ScreenshotFile>.Ok(frame.OutputFile);
    }

    private static ProcessCommand BuildCommand(ResolvedScreenshotFrame frame)
    {
        var region = frame.Region;
        var videoSize = $"{region.Width}x{region.Height}";
        var input = frame.Display.FormatInput(region);

        if (frame.Pointer is ScreenshotPointer.FrozenAt frozen)
        {
            var relX = frozen.Position.X - region.X;
            var relY = frozen.Position.Y - region.Y;

            if (relX >= 0 && relX < region.Width && relY >= 0 && relY < region.Height)
            {
                return new ProcessCommand(
                    FFmpegPath,
                    [
                        "-y",
                        "-f", "x11grab",
                        "-draw_mouse", "0",
                        "-video_size", videoSize,
                        "-i", input,
                        "-vf", BuildCursorFilter(relX, relY),
                        "-frames:v", "1",
                        "-update", "1",
                        frame.OutputFile.Path
                    ],
                    ProcessIo.CaptureError);
            }
        }

        var drawMouse = frame.Pointer switch
        {
            ScreenshotPointer.Excluded => "0",
            ScreenshotPointer.Live => "1",
            ScreenshotPointer.FrozenAt => "0",
            _ => throw new InvalidOperationException($"Unhandled screenshot pointer: {frame.Pointer.GetType().Name}")
        };

        return new ProcessCommand(
            FFmpegPath,
            [
                "-y",
                "-f", "x11grab",
                "-draw_mouse", drawMouse,
                "-video_size", videoSize,
                "-i", input,
                "-frames:v", "1",
                "-update", "1",
                frame.OutputFile.Path
            ],
            ProcessIo.CaptureError);
    }

    private static string BuildCursorFilter(int x, int y)
    {
        var rowWidths = PointerGlyph.LeftArrow.RowWidths.Span;
        var filters = new List<string>(rowWidths.Length * 3 + 4);

        for (var row = 0; row < rowWidths.Length; row++)
        {
            var width = rowWidths[row];
            var rowY = y + row;

            filters.Add($"drawbox=x={x}:y={rowY}:w=1:h=1:c=black:t=fill");

            if (width > 2)
                filters.Add($"drawbox=x={x + 1}:y={rowY}:w={width - 2}:h=1:c=white:t=fill");

            if (width > 1)
                filters.Add($"drawbox=x={x + width - 1}:y={rowY}:w=1:h=1:c=black:t=fill");
        }

        filters.Add($"drawbox=x={x + 5}:y={y + 10}:w=6:h=1:c=black:t=fill");
        filters.Add($"drawbox=x={x + 5}:y={y + 11}:w=1:h=1:c=black:t=fill");
        filters.Add($"drawbox=x={x + 5}:y={y + 12}:w=1:h=1:c=black:t=fill");

        return string.Join(",", filters);
    }
}

internal readonly record struct ResolvedScreenshotFrame(
    ScreenRegion Region,
    ScreenshotPointer Pointer,
    ScreenshotFile OutputFile,
    X11DisplayName Display);
