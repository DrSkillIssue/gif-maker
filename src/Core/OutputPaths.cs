namespace GifMaker.Core;

/// <summary>
/// Centralized output path generation for recordings and screenshots.
/// </summary>
public static class OutputPaths
{
    /// <summary>
    /// Gets the default directory for video recordings.
    /// </summary>
    /// <returns>Path to ~/Videos or fallback.</returns>
    public static string GetDefaultVideoDir()
    {
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(videosDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            videosDir = Path.Combine(home, "Videos");
        }
        return videosDir;
    }

    /// <summary>
    /// Gets the default directory for screenshots.
    /// </summary>
    /// <returns>Path to ~/Pictures/Screenshots or fallback.</returns>
    public static string GetDefaultScreenshotDir()
    {
        var picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(picturesDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            picturesDir = Path.Combine(home, "Pictures");
        }
        return Path.Combine(picturesDir, "Screenshots");
    }

    /// <summary>
    /// Generates a recording filename with timestamp.
    /// </summary>
    /// <param name="format">Output format for extension.</param>
    /// <returns>Filename like "recording_20260205_143022.gif".</returns>
    public static string GenerateRecordingFilename(OutputFormat format) =>
        $"recording_{DateTime.Now:yyyyMMdd_HHmmss}{format.GetExtension()}";

    /// <summary>
    /// Generates a screenshot filename with timestamp.
    /// </summary>
    /// <returns>Filename like "ss_20260205_143022.png".</returns>
    public static string GenerateScreenshotFilename() =>
        $"ss_{DateTime.Now:yyyyMMdd_HHmmss}.png";

    /// <summary>
    /// Generates a full output path for a recording.
    /// </summary>
    /// <param name="format">Output format.</param>
    /// <param name="outputDir">Optional directory (defaults to ~/Videos).</param>
    /// <returns>Full path to output file.</returns>
    public static string GenerateRecordingPath(OutputFormat format, string? outputDir = null)
    {
        var dir = ExpandPath(outputDir ?? GetDefaultVideoDir());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, GenerateRecordingFilename(format));
    }

    /// <summary>
    /// Generates a full output path for a screenshot.
    /// </summary>
    /// <param name="outputDir">Optional directory (defaults to ~/Pictures/Screenshots).</param>
    /// <returns>Full path to output file.</returns>
    public static string GenerateScreenshotPath(string? outputDir = null)
    {
        var dir = ExpandPath(outputDir ?? GetDefaultScreenshotDir());
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, GenerateScreenshotFilename());
    }

    /// <summary>
    /// Expands ~ to home directory.
    /// </summary>
    private static string ExpandPath(string path)
    {
        if (!path.StartsWith('~'))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[1..].TrimStart('/'));
    }
}
