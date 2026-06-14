using GifMaker.Conversion;

namespace GifMaker.UnitTests.Conversion;

public sealed class RecordingOutputPathsTests
{
    public static TheoryData<ConversionFormat, string> FormatExtensions => new()
    {
        { ConversionFormat.Gif, ".gif" },
        { ConversionFormat.Mp4, ".mp4" },
        { ConversionFormat.WebM, ".webm" }
    };

    [Fact]
    public void GenerateRecordingPathCreatesDirectory()
    {
        using var directory = new TemporaryDirectory();
        var outputDirectory = Path.Combine(directory.Path, "nested");

        var path = RecordingOutputPaths.GenerateRecordingPath(ConversionFormat.Gif, outputDirectory);

        Assert.True(Directory.Exists(outputDirectory));
        Assert.StartsWith(outputDirectory, path, StringComparison.Ordinal);
        Assert.EndsWith(".gif", path, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateRecordingPathExpandsHomeDirectory()
    {
        var relative = "gifmaker-tests-" + Guid.NewGuid().ToString("N");
        var outputDirectory = "~/" + relative;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expectedDirectory = Path.Combine(home, relative);

        try
        {
            var path = RecordingOutputPaths.GenerateRecordingPath(ConversionFormat.Gif, outputDirectory);

            Assert.True(Directory.Exists(expectedDirectory));
            Assert.StartsWith(expectedDirectory, path, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(expectedDirectory))
                Directory.Delete(expectedDirectory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(FormatExtensions))]
    public void GenerateRecordingPathUsesFormatExtension(ConversionFormat format, string extension)
    {
        using var directory = new TemporaryDirectory();

        var path = RecordingOutputPaths.GenerateRecordingPath(format, directory.Path);

        Assert.EndsWith(extension, path, StringComparison.Ordinal);
    }
}
