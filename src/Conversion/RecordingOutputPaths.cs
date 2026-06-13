namespace GifMaker.Conversion;

public static class RecordingOutputPaths
{
    public static string GetDefaultVideoDir()
    {
        var videosDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!string.IsNullOrEmpty(videosDir))
            return videosDir;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Videos");
    }

    public static string GenerateRecordingPath(ConversionFormat format, string? outputDir = null)
    {
        var directory = ExpandPath(outputDir ?? GetDefaultVideoDir());
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, GenerateRecordingFilename(format));
    }

    private static string GenerateRecordingFilename(ConversionFormat format) =>
        $"recording_{DateTime.Now:yyyyMMdd_HHmmss}{format.GetExtension()}";

    private static string ExpandPath(string path)
    {
        if (!path.StartsWith('~'))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[1..].TrimStart('/'));
    }
}
