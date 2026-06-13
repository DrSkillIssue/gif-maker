using GifMaker.Conversion;
using GifMaker.Screenshot;

namespace GifMaker.Cli;

/// <summary>
/// Parsed command-line arguments.
/// </summary>
public abstract record CliArgs
{
    private CliArgs() { }

    /// <summary>Launch GUI (default).</summary>
    public sealed record Gui : CliArgs;

    /// <summary>Show help.</summary>
    public sealed record Help : CliArgs;

    /// <summary>Show version.</summary>
    public sealed record Version : CliArgs;

    /// <summary>Take screenshot and exit.</summary>
    public sealed record Screenshot(
        ScreenshotSource Source,
        CliScreenshotPointer Pointer,
        ScreenshotDestination Destination) : CliArgs;

    /// <summary>Start recording (requires stop signal).</summary>
    public sealed record Record(
        ConversionFormat Format,
        int Fps,
        string? OutputDir) : CliArgs;

    /// <summary>Invalid arguments.</summary>
    public sealed record Invalid(string Error) : CliArgs;
}

/// <summary>
/// Parses command-line arguments into structured commands.
/// </summary>
public static class CliParser
{
    private const string HelpText = """
        GifMaker - Screen recorder and screenshot tool

        Usage: gifmaker [command] [options]

        Commands:
          (none)              Launch GUI
          screenshot, ss      Take screenshot and exit
          record, rec         Start recording (select area, then record)

        Screenshot options:
          -m, --mode MODE     Capture mode: selection (default), screen, window
          -p, --pointer       Include mouse pointer
          -o, --output PATH   Output file path (default: auto-generated)

        Record options:
          -f, --format FMT    Output format: gif (default), mp4, webm
          --fps N             Frames per second: 15, 24, 30 (default), 60
          -o, --output DIR    Output directory (default: ~/Videos)

        General:
          -h, --help          Show this help
          -v, --version       Show version

        Examples:
          gifmaker                          # Launch GUI
          gifmaker screenshot               # Screenshot with area selection
          gifmaker ss -m screen             # Full screen screenshot
          gifmaker ss -m window -p          # Window screenshot with pointer
          gifmaker record                   # Record with area selection (GIF)
          gifmaker rec -f mp4 --fps 60      # Record as MP4 at 60fps

        Hotkeys (when running):
          Print               Take screenshot (selection mode)
          Ctrl+Alt+S          Start/show recording
        """;

    private const string VersionText = "GifMaker 1.0.0";

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    public static CliArgs Parse(ReadOnlySpan<string> args)
    {
        if (args.IsEmpty)
            return new CliArgs.Gui();

        var first = args[0];

        // Global flags
        if (first is "-h" or "--help")
            return new CliArgs.Help();

        if (first is "-v" or "--version")
            return new CliArgs.Version();

        // Commands
        return first switch
        {
            "screenshot" or "ss" => ParseScreenshot(args[1..]),
            "record" or "rec" => ParseRecord(args[1..]),
            _ when first.StartsWith('-') => new CliArgs.Invalid($"Unknown option: {first}"),
            _ => new CliArgs.Invalid($"Unknown command: {first}")
        };
    }

    private static CliArgs ParseScreenshot(ReadOnlySpan<string> args)
    {
        ScreenshotSource source = new ScreenshotSource.InteractiveSelection();
        CliScreenshotPointer pointer = new CliScreenshotPointer.Excluded();
        var destination = ScreenshotDestination.Default;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-m" or "--mode":
                    if (i + 1 >= args.Length)
                        return new CliArgs.Invalid("--mode requires a value");
                    var sourceStr = args[++i];
                    var parsedSource = TryParseScreenshotSource(sourceStr);
                    if (parsedSource is null)
                        return new CliArgs.Invalid($"Unknown mode: {sourceStr}");
                    source = parsedSource;
                    break;

                case "-p" or "--pointer":
                    pointer = new CliScreenshotPointer.Included();
                    break;

                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                        return new CliArgs.Invalid("--output requires a path");
                    destination = ScreenshotDestination.FromCliPath(args[++i]);
                    break;

                case "-h" or "--help":
                    return new CliArgs.Help();

                default:
                    if (arg.StartsWith('-'))
                        return new CliArgs.Invalid($"Unknown screenshot option: {arg}");
                    destination = ScreenshotDestination.FromCliPath(arg);
                    break;
            }
        }

        return new CliArgs.Screenshot(source, pointer, destination);
    }

    private static CliArgs ParseRecord(ReadOnlySpan<string> args)
    {
        var format = ConversionFormat.Gif;
        var fps = 30;
        string? outputDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-f" or "--format":
                    if (i + 1 >= args.Length)
                        return new CliArgs.Invalid("--format requires a value");
                    var formatStr = args[++i];
                    var parsedFormat = TryParseFormat(formatStr);
                    if (parsedFormat is null)
                        return new CliArgs.Invalid($"Unknown format: {formatStr}");
                    format = parsedFormat.Value;
                    break;

                case "--fps":
                    if (i + 1 >= args.Length)
                        return new CliArgs.Invalid("--fps requires a value");
                    if (!int.TryParse(args[++i], out fps) || fps is < 1 or > 240)
                        return new CliArgs.Invalid("--fps must be 1-240");
                    break;

                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                        return new CliArgs.Invalid("--output requires a directory");
                    outputDir = args[++i];
                    break;

                case "-h" or "--help":
                    return new CliArgs.Help();

                default:
                    if (arg.StartsWith('-'))
                        return new CliArgs.Invalid($"Unknown record option: {arg}");
                    // Positional arg = output dir
                    outputDir ??= arg;
                    break;
            }
        }

        return new CliArgs.Record(format, fps, outputDir);
    }

    /// <summary>Gets the help text.</summary>
    public static string GetHelpText() => HelpText;

    /// <summary>Gets the version text.</summary>
    public static string GetVersionText() => VersionText;

    private static ScreenshotSource? TryParseScreenshotSource(string value) =>
        value.ToLowerInvariant() switch
        {
            "selection" or "s" => new ScreenshotSource.InteractiveSelection(),
            "screen" or "c" => new ScreenshotSource.FullScreen(),
            "window" or "w" => new ScreenshotSource.ActiveWindow(),
            _ => null
        };

    private static ConversionFormat? TryParseFormat(string value) =>
        value.ToLowerInvariant() switch
        {
            "gif" => ConversionFormat.Gif,
            "mp4" => ConversionFormat.Mp4,
            "webm" => ConversionFormat.WebM,
            _ => null
        };
}

public abstract record CliScreenshotPointer
{
    private CliScreenshotPointer() { }

    public sealed record Excluded : CliScreenshotPointer;

    public sealed record Included : CliScreenshotPointer;
}
