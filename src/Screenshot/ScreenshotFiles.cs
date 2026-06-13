using GifMaker.Core;

namespace GifMaker.Screenshot;

/// <summary>
/// Reserves filesystem paths for screenshots.
/// </summary>
public sealed class ScreenshotFiles
{
    private const string TemporaryPrefix = "gifmaker-screenshot-";

    public Result<ScreenshotFile> Reserve(ScreenshotDestination destination)
    {
        if (destination is null)
            return Result<ScreenshotFile>.Fail("Screenshot destination is required");

        try
        {
            return destination switch
            {
                ScreenshotDestination.DefaultFile => ReserveDefault(),
                ScreenshotDestination.Directory directory => ReserveDirectory(directory.Path),
                ScreenshotDestination.File file => ReserveFile(file.Path),
                _ => Result<ScreenshotFile>.Fail($"Unknown screenshot destination: {destination.GetType().Name}")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Result<ScreenshotFile>.Fail($"Failed to reserve screenshot file: {ex.Message}");
        }
    }

    private static Result<ScreenshotFile> ReserveDefault() =>
        ReserveDirectory(GetDefaultScreenshotDirectory());

    public Result<ScreenshotFile> ReserveTemporary()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"{TemporaryPrefix}{Guid.NewGuid():N}.png");
            return Result<ScreenshotFile>.Ok(new ScreenshotFile(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Result<ScreenshotFile>.Fail($"Failed to reserve temporary screenshot file: {ex.Message}");
        }
    }

    private static Result<ScreenshotFile> ReserveDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<ScreenshotFile>.Fail("Screenshot output directory is required");

        var directory = ExpandPath(path);
        System.IO.Directory.CreateDirectory(directory);
        return Result<ScreenshotFile>.Ok(new ScreenshotFile(Path.Combine(directory, GenerateScreenshotFilename())));
    }

    private static Result<ScreenshotFile> ReserveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<ScreenshotFile>.Fail("Screenshot output path is required");

        var expandedPath = ExpandPath(path);
        var directory = Path.GetDirectoryName(expandedPath);
        if (!string.IsNullOrEmpty(directory))
            System.IO.Directory.CreateDirectory(directory);

        return Result<ScreenshotFile>.Ok(new ScreenshotFile(expandedPath));
    }

    private static string GetDefaultScreenshotDirectory()
    {
        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(picturesDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            picturesDir = Path.Combine(home, "Pictures");
        }

        return Path.Combine(picturesDir, "Screenshots");
    }

    private static string GenerateScreenshotFilename() =>
        $"ss_{DateTime.Now:yyyyMMdd_HHmmss}.png";

    private static string ExpandPath(string path)
    {
        if (!path.StartsWith('~'))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[1..].TrimStart('/'));
    }
}
