namespace GifMaker.Conversion;

/// <summary>
/// Supported recording export formats.
/// </summary>
public enum ConversionFormat
{
    Gif,
    Mp4,
    WebM
}

public static class ConversionFormatExtensions
{
    public static string GetExtension(this ConversionFormat format) => format switch
    {
        ConversionFormat.Gif => ".gif",
        ConversionFormat.Mp4 => ".mp4",
        ConversionFormat.WebM => ".webm",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
