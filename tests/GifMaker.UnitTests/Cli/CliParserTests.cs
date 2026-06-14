using GifMaker.Cli;
using GifMaker.Conversion;
using GifMaker.Core;
using GifMaker.Screenshot;

namespace GifMaker.UnitTests.Cli;

public sealed class CliParserTests
{
    public static TheoryData<string, CliScreenshotMode> ScreenshotModeAliases => new()
    {
        { "selection", CliScreenshotMode.Selection },
        { "s", CliScreenshotMode.Selection },
        { "screen", CliScreenshotMode.Screen },
        { "c", CliScreenshotMode.Screen },
        { "window", CliScreenshotMode.Window },
        { "w", CliScreenshotMode.Window }
    };

    public static TheoryData<string[], string> InvalidScreenshotArgs => new()
    {
        { ["screenshot", "-m"], "--mode requires a value" },
        { ["screenshot", "--mode", "desk"], "Unknown mode: desk" },
        { ["screenshot", "-o"], "--output requires a path" },
        { ["screenshot", "--bad"], "Unknown screenshot option: --bad" }
    };

    public static TheoryData<string, ConversionFormat> RecordFormats => new()
    {
        { "gif", ConversionFormat.Gif },
        { "mp4", ConversionFormat.Mp4 },
        { "webm", ConversionFormat.WebM }
    };

    public static TheoryData<string[], string> InvalidRecordArgs => new()
    {
        { ["record", "-f"], "--format requires a value" },
        { ["record", "--format", "avi"], "Unknown format: avi" },
        { ["record", "--fps"], "--fps requires a value" },
        { ["record", "--fps", "0"], "--fps must be 1-240" },
        { ["record", "--fps", "241"], "--fps must be 1-240" },
        { ["record", "--fps", "fast"], "--fps must be 1-240" },
        { ["record", "-o"], "--output requires a directory" },
        { ["record", "--bad"], "Unknown record option: --bad" }
    };

    [Fact]
    public void ParseNoArgsReturnsGui()
    {
        Assert.IsType<CliArgs.Gui>(CliParser.Parse([]));
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-v")]
    [InlineData("--version")]
    public void ParseGlobalFlagsReturnsHelpOrVersion(string flag)
    {
        var parsed = CliParser.Parse([flag]);

        if (flag is "-h" or "--help")
            Assert.IsType<CliArgs.Help>(parsed);
        else
            Assert.IsType<CliArgs.Version>(parsed);
    }

    [Fact]
    public void ParseUnknownCommandReturnsInvalid()
    {
        var invalid = Assert.IsType<CliArgs.Invalid>(CliParser.Parse(["wat"]));

        Assert.Equal("Unknown command: wat", invalid.Error);
    }

    [Theory]
    [MemberData(nameof(ScreenshotModeAliases))]
    public void ParseScreenshotAcceptsEveryModeAlias(string mode, CliScreenshotMode expected)
    {
        var parsed = Assert.IsType<CliArgs.Screenshot>(CliParser.Parse(["ss", "-m", mode]));

        Assert.Equal(expected, parsed.Mode);
    }

    [Theory]
    [MemberData(nameof(InvalidScreenshotArgs))]
    public void ParseScreenshotRejectsMissingOrUnknownOption(string[] args, string expectedError)
    {
        var invalid = Assert.IsType<CliArgs.Invalid>(CliParser.Parse(args));

        Assert.Equal(expectedError, invalid.Error);
    }

    [Fact]
    public void ParseScreenshotTreatsPositionalPathAsDestination()
    {
        var parsed = Assert.IsType<CliArgs.Screenshot>(CliParser.Parse(["screenshot", "/tmp/out.png"]));
        var file = Assert.IsType<ScreenshotDestination.File>(parsed.Destination);

        Assert.Equal("/tmp/out.png", file.Path);
    }

    [Fact]
    public void ParseScreenshotPointerFlagIncludesPointer()
    {
        var parsed = Assert.IsType<CliArgs.Screenshot>(CliParser.Parse(["screenshot", "--pointer"]));

        Assert.Equal(ScreenPointerCapture.Included, parsed.Pointer);
    }

    [Fact]
    public void ParseScreenshotHelpReturnsHelp()
    {
        Assert.IsType<CliArgs.Help>(CliParser.Parse(["screenshot", "--help"]));
    }

    [Theory]
    [MemberData(nameof(RecordFormats))]
    public void ParseRecordAcceptsEveryFormat(string format, ConversionFormat expected)
    {
        var parsed = Assert.IsType<CliArgs.Record>(CliParser.Parse(["record", "-f", format]));

        Assert.Equal(expected, parsed.Format);
    }

    [Theory]
    [MemberData(nameof(InvalidRecordArgs))]
    public void ParseRecordRejectsMissingUnknownOrOutOfRangeValues(string[] args, string expectedError)
    {
        var invalid = Assert.IsType<CliArgs.Invalid>(CliParser.Parse(args));

        Assert.Equal(expectedError, invalid.Error);
    }

    [Fact]
    public void ParseRecordTreatsPositionalPathAsOutputDirectory()
    {
        var parsed = Assert.IsType<CliArgs.Record>(CliParser.Parse(["record", "/tmp/videos"]));

        Assert.Equal("/tmp/videos", parsed.OutputDir);
    }

    [Fact]
    public void ParseRecordHelpReturnsHelp()
    {
        Assert.IsType<CliArgs.Help>(CliParser.Parse(["record", "--help"]));
    }

    [Fact]
    public void HelpAndVersionTextAreStable()
    {
        Assert.Contains("Usage: gifmaker [command] [options]", CliParser.GetHelpText(), StringComparison.Ordinal);
        Assert.Contains("screenshot, ss", CliParser.GetHelpText(), StringComparison.Ordinal);
        Assert.Equal("GifMaker 1.0.0", CliParser.GetVersionText());
    }
}
