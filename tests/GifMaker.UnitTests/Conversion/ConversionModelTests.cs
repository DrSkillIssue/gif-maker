using GifMaker.Conversion;

namespace GifMaker.UnitTests.Conversion;

public sealed class ConversionModelTests
{
    public static TheoryData<ConversionFormat, Type> ProfileTypes => new()
    {
        { ConversionFormat.Gif, typeof(ConversionProfile.GifProfile) },
        { ConversionFormat.Mp4, typeof(ConversionProfile.Mp4Profile) },
        { ConversionFormat.WebM, typeof(ConversionProfile.WebMProfile) }
    };

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public void FrameRateAcceptsBoundaryValues(int fps)
    {
        var result = ConversionFrameRate.Create(fps);

        Assert.True(result.IsSuccess);
        Assert.Equal(fps, result.GetValueOrThrow().Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void FrameRateRejectsOutOfRangeValues(int fps)
    {
        var result = ConversionFrameRate.Create(fps);

        Assert.False(result.IsSuccess);
        Assert.Equal("FPS must be between 1 and 120", result.Match(_ => "", error => error));
    }

    [Theory]
    [MemberData(nameof(ProfileTypes))]
    public void ProfileCreateMapsFormatToProfile(ConversionFormat format, Type expectedType)
    {
        var result = ConversionProfile.Create(format, 30);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedType, result.GetValueOrThrow().GetType());
    }

    [Fact]
    public void ProfileCreateRejectsBadFps()
    {
        var result = ConversionProfile.Create(ConversionFormat.Gif, 121);

        Assert.False(result.IsSuccess);
        Assert.Equal("FPS must be between 1 and 120", result.Match(_ => "", error => error));
    }

    [Fact]
    public void SourceVideoFileRejectsMissingOrWhitespaceSource()
    {
        var missing = SourceVideoFile.Create("/tmp/gifmaker-definitely-missing-input.mkv");

        Assert.False(missing.IsSuccess);
        Assert.Equal(
            "Source video not found: /tmp/gifmaker-definitely-missing-input.mkv",
            missing.Match(_ => "", error => error));
        Assert.Throws<ArgumentException>(() => SourceVideoFile.Create(" "));
    }

    [Fact]
    public void OutputFileRejectsMissingDirectoryOrWhitespacePath()
    {
        var missingDirectory = OutputFile.Create("/tmp/gifmaker-definitely-missing-dir/out.gif");

        Assert.False(missingDirectory.IsSuccess);
        Assert.Equal(
            "Output directory not found: /tmp/gifmaker-definitely-missing-dir",
            missingDirectory.Match(_ => "", error => error));
        Assert.Throws<ArgumentException>(() => OutputFile.Create(""));
    }

    [Fact]
    public void ConversionJobStopsAtFirstInvalidInput()
    {
        using var directory = new TemporaryDirectory();
        var outputPath = Path.Combine(directory.Path, "out.gif");
        var profile = ConversionProfile.Create(ConversionFormat.Gif, 30).GetValueOrThrow();

        var result = ConversionJob.Create("/tmp/gifmaker-missing-source.mkv", outputPath, profile);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Source video not found: /tmp/gifmaker-missing-source.mkv",
            result.Match(_ => "", error => error));
    }
}
