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

        var command = BuildCaptureCommand(frame);

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

    private static ProcessCommand BuildCaptureCommand(ResolvedScreenshotFrame frame)
    {
        var region = frame.Region;
        var videoSize = $"{region.Width}x{region.Height}";
        var input = frame.Display.FormatInput(region);

        var drawMouse = frame.Pointer switch
        {
            ScreenshotPointer.Excluded => "0",
            ScreenshotPointer.Included => "1",
            _ => throw new InvalidOperationException($"Unhandled screenshot pointer: {frame.Pointer}")
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

}

internal readonly record struct ResolvedScreenshotFrame(
    ScreenRegion Region,
    ScreenshotPointer Pointer,
    ScreenshotFile OutputFile,
    X11DisplayName Display);
