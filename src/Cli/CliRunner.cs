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
        ScreenshotPointer pointer = args.Pointer switch
        {
            CliScreenshotPointer.Excluded => ScreenshotPointer.Excluded,
            CliScreenshotPointer.Included => ScreenshotPointer.Included,
            _ => throw new InvalidOperationException($"Unhandled screenshot pointer: {args.Pointer}")
        };

        Console.WriteLine($"Screenshot mode: {DescribeMode(args.Mode)}");

        Result<CapturedScreenshot> result;
        if (args.Mode == CliScreenshotMode.Selection)
        {
            var selector = new SlopScreenRegionSelector();
            var regionResult = await selector.SelectAsync(ct).ConfigureAwait(false);
            if (!regionResult.IsSuccess)
            {
                return regionResult.Match(
                    _ => throw new InvalidOperationException("Unreachable result state"),
                    error =>
                    {
                        if (error == SlopScreenRegionSelector.SelectionCancelled)
                        {
                            Console.WriteLine("Selection cancelled.");
                            return 0;
                        }

                        Console.Error.WriteLine($"Error: {error}");
                        return 1;
                    });
            }

            result = await service.CaptureAsync(
                new ScreenshotCapture.Area(regionResult.GetValueOrThrow(), pointer, args.Destination),
                ct).ConfigureAwait(false);
        }
        else
        {
            ScreenshotCapture capture = args.Mode switch
            {
                CliScreenshotMode.Screen => new ScreenshotCapture.FullScreen(pointer, args.Destination),
                CliScreenshotMode.Window => new ScreenshotCapture.ActiveWindow(pointer, args.Destination),
                _ => throw new InvalidOperationException($"Unhandled screenshot mode: {args.Mode}")
            };
            result = await service.CaptureAsync(capture, ct).ConfigureAwait(false);
        }

        return result.Match(
            captured =>
            {
                Console.WriteLine($"Saved: {captured.File.Path}");
                return 0;
            },
            error =>
            {
                if (error == SlopScreenRegionSelector.SelectionCancelled)
                {
                    Console.WriteLine("Selection cancelled.");
                    return 0;
                }

                Console.Error.WriteLine($"Error: {error}");
                return 1;
            });
    }

    private static string DescribeMode(CliScreenshotMode mode) =>
        mode switch
        {
            CliScreenshotMode.Selection => "Selection",
            CliScreenshotMode.Screen => "Screen",
            CliScreenshotMode.Window => "Window",
            _ => throw new InvalidOperationException($"Unhandled screenshot mode: {mode}")
        };

    private static async Task<int> RunRecordAsync(CliArgs.Record args, CancellationToken ct)
    {
        Console.WriteLine("Select area to record...");

        // Select area
        var selector = new SlopScreenRegionSelector();
        var regionResult = await selector.SelectAsync(ct).ConfigureAwait(false);

        return await regionResult.Match(
            async region =>
            {
                Console.WriteLine($"Recording {region.Width}x{region.Height} at {args.Fps}fps");
                Console.WriteLine("Press Enter to stop recording...");

                var recorder = new FFmpegRecorder();
                try
                {
                    var recordingSettings = FFmpegRecorder.RecordingSettings.Create(region, args.Fps)
                        .GetValueOrThrow();
                    recorder.Start(recordingSettings);

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
                    var outputPath = RecordingOutputPaths.GenerateRecordingPath(args.Format, args.OutputDir);

                    var converter = new FFmpegConverter();
                    var profile = ConversionProfile.Create(args.Format, args.Fps).GetValueOrThrow();
                    var job = ConversionJob.Create(recorder.TempPath, outputPath, profile).GetValueOrThrow();
                    var conversionResult = await converter.ConvertAsync(job, ct).ConfigureAwait(false);

                    if (!conversionResult.IsSuccess)
                    {
                        var message = conversionResult.Match(_ => "", error => error);
                        Console.Error.WriteLine($"Error: {message}");
                        return 1;
                    }

                    var converted = conversionResult.GetValueOrThrow();

                    // Cleanup temp
                    try { File.Delete(recorder.TempPath); }
                    catch { /* ignore */ }

                    Console.WriteLine($"Saved: {converted.OutputFile.Path}");
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
                if (error == SlopScreenRegionSelector.SelectionCancelled)
                {
                    Console.WriteLine("Selection cancelled.");
                    return Task.FromResult(0);
                }

                Console.Error.WriteLine($"Error: {error}");
                return Task.FromResult(1);
            }).ConfigureAwait(false);
    }
}
