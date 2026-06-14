using GifMaker.Screenshot;

namespace GifMaker.UnitTests.Screenshot;

public sealed class ScreenshotFilesTests
{
    [Fact]
    public void ReserveRejectsNullDestination()
    {
        var result = new ScreenshotFiles().Reserve(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal("Screenshot destination is required", result.Match(_ => "", error => error));
    }

    [Fact]
    public void ReserveRejectsBlankDirectory()
    {
        var result = new ScreenshotFiles().Reserve(new ScreenshotDestination.Directory(" "));

        Assert.False(result.IsSuccess);
        Assert.Equal("Screenshot output directory is required", result.Match(_ => "", error => error));
    }

    [Fact]
    public void ReserveRejectsBlankFile()
    {
        var result = new ScreenshotFiles().Reserve(new ScreenshotDestination.File(""));

        Assert.False(result.IsSuccess);
        Assert.Equal("Screenshot output path is required", result.Match(_ => "", error => error));
    }

    [Fact]
    public void ReserveCreatesDirectoryDestination()
    {
        using var directory = new TemporaryDirectory();
        var outputDirectory = Path.Combine(directory.Path, "screens");

        var result = new ScreenshotFiles().Reserve(new ScreenshotDestination.Directory(outputDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(outputDirectory));
        Assert.StartsWith(outputDirectory, result.GetValueOrThrow().Path, StringComparison.Ordinal);
        Assert.EndsWith(".png", result.GetValueOrThrow().Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ReserveCreatesParentDirectoryForFileDestination()
    {
        using var directory = new TemporaryDirectory();
        var outputFile = Path.Combine(directory.Path, "nested", "out.png");

        var result = new ScreenshotFiles().Reserve(new ScreenshotDestination.File(outputFile));

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(Path.GetDirectoryName(outputFile)));
        Assert.Equal(outputFile, result.GetValueOrThrow().Path);
    }

    [Fact]
    public void ReserveExpandsHomeDirectory()
    {
        var relative = "gifmaker-tests-" + Guid.NewGuid().ToString("N");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expectedDirectory = Path.Combine(home, relative);

        try
        {
            var result = new ScreenshotFiles().Reserve(
                new ScreenshotDestination.File("~/" + relative + "/out.png"));

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.Combine(expectedDirectory, "out.png"), result.GetValueOrThrow().Path);
            Assert.True(Directory.Exists(expectedDirectory));
        }
        finally
        {
            if (Directory.Exists(expectedDirectory))
                Directory.Delete(expectedDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReserveReturnsFailureForUnsupportedPath()
    {
        using var directory = new TemporaryDirectory();
        var tooLongComponent = new string('a', 5000);

        var result = new ScreenshotFiles().Reserve(
            new ScreenshotDestination.File(Path.Combine(directory.Path, tooLongComponent, "out.png")));

        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to reserve screenshot file:", result.Match(_ => "", error => error), StringComparison.Ordinal);
    }
}
