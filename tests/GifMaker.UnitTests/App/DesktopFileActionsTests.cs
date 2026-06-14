using GifMaker.App;

namespace GifMaker.UnitTests.App;

public sealed class DesktopFileActionsTests
{
    [Fact]
    public void OpenFailsForMissingFile()
    {
        var result = new DesktopFileActions(new FakeProcessRunner()).Open(new SavedMedia("/tmp/gifmaker-missing.png"));

        Assert.False(result.IsSuccess);
        Assert.Equal("File not found", result.Match(_ => "", error => error));
    }

    [Fact]
    public void OpenFailsWhenDetachedStartFails()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "image.png");
        File.WriteAllText(path, "png");
        var runner = new FakeProcessRunner();
        runner.DetachedResults.Enqueue(false);

        var result = new DesktopFileActions(runner).Open(new SavedMedia(path));

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to open file", result.Match(_ => "", error => error));
    }

    [Fact]
    public void OpenStartsXdgOpenForExistingFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "image.png");
        File.WriteAllText(path, "png");
        var runner = new FakeProcessRunner();

        var result = new DesktopFileActions(runner).Open(new SavedMedia(path));

        Assert.True(result.IsSuccess);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal([path], command.Arguments);
    }

    [Fact]
    public void OpenContainingFolderFailsWhenFolderMissing()
    {
        var result = new DesktopFileActions(new FakeProcessRunner()).OpenContainingFolder(
            new SavedMedia("/tmp/gifmaker-missing-folder/file.png"));

        Assert.False(result.IsSuccess);
        Assert.Equal("Containing folder not found", result.Match(_ => "", error => error));
    }

    [Fact]
    public void OpenContainingFolderStartsXdgOpenForExistingFolder()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "image.png");
        var runner = new FakeProcessRunner();

        var result = new DesktopFileActions(runner).OpenContainingFolder(new SavedMedia(path));

        Assert.True(result.IsSuccess);
        var command = Assert.Single(runner.Commands);
        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal([directory.Path], command.Arguments);
    }
}
