namespace GifMaker.Core;

/// <summary>
/// Supported output formats for recordings.
/// </summary>
public enum OutputFormat
{
    /// <summary>Graphics Interchange Format - animated, widely supported, larger file size.</summary>
    Gif,
    
    /// <summary>MPEG-4 Part 14 - video, excellent compression, best for sharing.</summary>
    Mp4,
    
    /// <summary>WebM (VP9) - video, open format, good for web.</summary>
    WebM
}

/// <summary>
/// Extension methods for <see cref="OutputFormat"/>.
/// </summary>
public static class OutputFormatExtensions
{
    /// <summary>
    /// Gets the file extension for this format, including the leading dot.
    /// </summary>
    /// <param name="format">The output format.</param>
    /// <returns>File extension (e.g., ".gif", ".mp4", ".webm").</returns>
    /// <exception cref="ArgumentOutOfRangeException">Unknown format value.</exception>
    public static string GetExtension(this OutputFormat format) => format switch
    {
        OutputFormat.Gif => ".gif",
        OutputFormat.Mp4 => ".mp4",
        OutputFormat.WebM => ".webm",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
