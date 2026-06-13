using System.Diagnostics;
using GifMaker.Core;

namespace GifMaker.Conversion;

/// <summary>
/// Converts recorded video using FFmpeg.
/// </summary>
public sealed class FFmpegConverter(IProcessRunner? processRunner = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly IProcessRunner _processRunner = processRunner ?? ProcessRunner.Default;

    public TimeSpan Timeout
    {
        get => field;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultTimeout;

    public async Task<Result<ConversionResult>> ConvertAsync(ConversionJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        var command = BuildCommand(job);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner.RunAsync(command, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<ConversionResult>.Fail($"FFmpeg conversion timed out after {Timeout.TotalMinutes:F1} minutes");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<ConversionResult>.Fail($"Failed to start FFmpeg: {ex.Message}");
        }

        if (processResult.ExitCode != 0)
            return Result<ConversionResult>.Fail($"FFmpeg conversion failed: {processResult.StandardError}");

        if (!File.Exists(job.OutputFile.Path))
            return Result<ConversionResult>.Fail("FFmpeg completed but output file was not created");

        return Result<ConversionResult>.Ok(new ConversionResult(job.OutputFile));
    }

    internal static ProcessCommand BuildCommand(ConversionJob job)
    {
        var args = new List<string>(18)
        {
            "-y",
            "-i",
            job.SourceFile.Path
        };

        switch (job.Profile)
        {
            case ConversionProfile.GifProfile gif:
                args.Add("-filter_complex");
                args.Add(BuildGifFilter(gif.FrameRate));
                break;

            case ConversionProfile.Mp4Profile mp4:
                args.Add("-c:v");
                args.Add("libx264");
                args.Add("-preset");
                args.Add("medium");
                args.Add("-crf");
                args.Add("23");
                args.Add("-r");
                args.Add(mp4.FrameRate.ToString());
                args.Add("-pix_fmt");
                args.Add("yuv420p");
                args.Add("-movflags");
                args.Add("+faststart");
                break;

            case ConversionProfile.WebMProfile webM:
                args.Add("-c:v");
                args.Add("libvpx-vp9");
                args.Add("-crf");
                args.Add("30");
                args.Add("-b:v");
                args.Add("0");
                args.Add("-r");
                args.Add(webM.FrameRate.ToString());
                args.Add("-pix_fmt");
                args.Add("yuv420p");
                break;

            default:
                throw new UnreachableException($"Unhandled conversion profile: {job.Profile.GetType().Name}");
        }

        args.Add(job.OutputFile.Path);
        return new ProcessCommand("ffmpeg", args, ProcessIo.CaptureError);
    }

    private static string BuildGifFilter(ConversionFrameRate frameRate) =>
        $"fps={frameRate},split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=floyd_steinberg";
}
