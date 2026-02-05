using System.Runtime.Versioning;
using GifMaker.Core;
using GifMaker.Conversion;
using GifMaker.Recording;
using GifMaker.Screenshot;
using GifMaker.X11;

namespace GifMaker.Cli;

/// <summary>
/// Executes CLI commands without launching the GUI.
/// </summary>
[SupportedOSPlatform("linux")]
public static class CliRunner
{
    /// <summary>
    /// Runs a CLI command.
    /// </summary>
    /// <returns>Exit code (0 = success).</returns>
    public static async Task<int> RunAsync(CliArgs args, CancellationToken ct = default)
    {
        return args switch
        {
            CliArgs.Help => ShowHelp(),
            CliArgs.Version => ShowVersion(),
            CliArgs.Screenshot ss => await RunScreenshotAsync(ss, ct).ConfigureAwait(false),
            CliArgs.Record rec => await RunRecordAsync(rec, ct).ConfigureAwait(false),
            CliArgs.Invalid inv => ShowError(inv.Error),
            CliArgs.Gui => 1, // Should not reach here
            _ => 1
        };
    }

    private static int ShowHelp()
    {
        Console.WriteLine(CliParser.GetHelpText());
        return 0;
    }

    private static int ShowVersion()
    {
        Console.WriteLine(CliParser.GetVersionText());
        return 0;
    }

    private static int ShowError(string error)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'gifmaker --help' for usage.");
        return 1;
    }

    private static async Task<int> RunScreenshotAsync(CliArgs.Screenshot args, CancellationToken ct)
    {
        var service = new ScreenshotService();

        // Get cursor position before selection (for fixed cursor in selection mode)
        var cursorPos = args.ShowPointer ? WindowPositioner.GetPointerPosition() : null;

        // Get region based on mode
        Console.WriteLine($"Screenshot mode: {args.Mode}");
        var regionResult = await service.GetRegionAsync(args.Mode, ct).ConfigureAwait(false);

        return await regionResult.Match(
            async region =>
            {
                if (!region.IsValid)
                {
                    Console.WriteLine("Selection cancelled.");
                    return 0;
                }

                // Determine output path
                string? outputDir = null;
                string? outputPath = args.OutputPath;

                if (outputPath is not null && Directory.Exists(outputPath))
                {
                    // User provided directory, not file
                    outputDir = outputPath;
                    outputPath = null;
                }

                // Capture
                var result = await service.CaptureAsync(
                    region,
                    args.Mode,
                    args.ShowPointer,
                    cursorPos,
                    outputDir,
                    ct).ConfigureAwait(false);

                return result.Match(
                    r =>
                    {
                        Console.WriteLine($"Saved: {r.FilePath}");
                        return 0;
                    },
                    error =>
                    {
                        Console.Error.WriteLine($"Error: {error}");
                        return 1;
                    });
            },
            error =>
            {
                Console.Error.WriteLine($"Error: {error}");
                return Task.FromResult(1);
            }).ConfigureAwait(false);
    }

    private static async Task<int> RunRecordAsync(CliArgs.Record args, CancellationToken ct)
    {
        Console.WriteLine("Select area to record...");

        // Select area
        using var selector = new AreaSelector();
        var regionResult = await selector.SelectAsync(ct).ConfigureAwait(false);

        return await regionResult.Match(
            async region =>
            {
                if (!region.IsValid)
                {
                    Console.WriteLine("Selection cancelled.");
                    return 0;
                }

                Console.WriteLine($"Recording {region.Width}x{region.Height} at {args.Fps}fps");
                Console.WriteLine("Press Enter to stop recording...");

                // Start recording
                var recorder = new FFmpegRecorder();
                try
                {
                    recorder.Start(region, args.Fps);

                    // Wait for Enter key or cancellation
                    var readTask = Task.Run(Console.ReadLine, ct);

                    try
                    {
                        await readTask.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ctrl+C - fall through to stop recording gracefully
                    }

                    Console.WriteLine("Stopping...");
                    var recordResult = await recorder.StopAsync(ct).ConfigureAwait(false);

                    Console.WriteLine($"Recorded {recordResult.Duration.TotalSeconds:F1}s");

                    // Convert
                    Console.WriteLine($"Converting to {args.Format}...");
                    var outputPath = OutputPaths.GenerateRecordingPath(args.Format, args.OutputDir);

                    var converter = new FFmpegConverter();
                    var settings = FFmpegConverter.ConversionSettings.CreateOrThrow(args.Format, args.Fps);
                    await converter.ConvertAsync(recorder.TempPath, outputPath, settings, ct: ct)
                        .ConfigureAwait(false);

                    // Cleanup temp
                    try { File.Delete(recorder.TempPath); }
                    catch { /* ignore */ }

                    Console.WriteLine($"Saved: {outputPath}");
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    throw; // Let cancellation propagate
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }
                finally
                {
                    recorder.Dispose();
                }
            },
            error =>
            {
                Console.Error.WriteLine($"Error: {error}");
                return Task.FromResult(1);
            }).ConfigureAwait(false);
    }
}
