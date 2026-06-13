namespace GifMaker.Screenshot;

public abstract record ScreenshotOutputTarget
{
    public static ScreenshotOutputTarget Default { get; } = new DefaultTarget();

    public static ScreenshotOutputTarget FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Default;

        return Directory.Exists(path)
            ? new DirectoryTarget(path)
            : new FileTarget(path);
    }

    public abstract string ResolvePath();

    private sealed record DefaultTarget : ScreenshotOutputTarget
    {
        public override string ResolvePath()
        {
            var directory = GetDefaultScreenshotDir();
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, GenerateScreenshotFilename());
        }
    }

    public sealed record DirectoryTarget(string Path) : ScreenshotOutputTarget
    {
        public override string ResolvePath()
        {
            var directory = ExpandPath(Path);
            Directory.CreateDirectory(directory);
            return System.IO.Path.Combine(directory, GenerateScreenshotFilename());
        }
    }

    public sealed record FileTarget(string Path) : ScreenshotOutputTarget
    {
        public override string ResolvePath()
        {
            var expandedPath = ExpandPath(Path);
            var directory = System.IO.Path.GetDirectoryName(expandedPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            return expandedPath;
        }
    }

    private static string GetDefaultScreenshotDir()
    {
        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(picturesDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            picturesDir = System.IO.Path.Combine(home, "Pictures");
        }

        return System.IO.Path.Combine(picturesDir, "Screenshots");
    }

    private static string GenerateScreenshotFilename() =>
        $"ss_{DateTime.Now:yyyyMMdd_HHmmss}.png";

    private static string ExpandPath(string path)
    {
        if (!path.StartsWith('~'))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(home, path[1..].TrimStart('/'));
    }
}
