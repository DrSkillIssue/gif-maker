using GifMaker.Core;

namespace GifMaker.Conversion;

public readonly record struct ConversionFrameRate
{
    public const int MinValue = 1;
    public const int MaxValue = 120;

    public int Value { get; }

    private ConversionFrameRate(int value) => Value = value;

    public static Result<ConversionFrameRate> Create(int value)
    {
        if (value is < MinValue or > MaxValue)
            return Result<ConversionFrameRate>.Fail($"FPS must be between {MinValue} and {MaxValue}");

        return Result<ConversionFrameRate>.Ok(new ConversionFrameRate(value));
    }

    public override string ToString() => Value.ToString();
}

public abstract record ConversionProfile
{
    public ConversionFrameRate FrameRate { get; }

    private protected ConversionProfile(ConversionFrameRate frameRate) => FrameRate = frameRate;

    public sealed record GifProfile : ConversionProfile
    {
        public GifProfile(ConversionFrameRate frameRate) : base(frameRate) { }
    }

    public sealed record Mp4Profile : ConversionProfile
    {
        public Mp4Profile(ConversionFrameRate frameRate) : base(frameRate) { }
    }

    public sealed record WebMProfile : ConversionProfile
    {
        public WebMProfile(ConversionFrameRate frameRate) : base(frameRate) { }
    }

    public static Result<ConversionProfile> Create(ConversionFormat format, int fps)
    {
        var frameRateResult = ConversionFrameRate.Create(fps);
        return frameRateResult.Match(
            frameRate => Result<ConversionProfile>.Ok(format switch
            {
                ConversionFormat.Gif => new GifProfile(frameRate),
                ConversionFormat.Mp4 => new Mp4Profile(frameRate),
                ConversionFormat.WebM => new WebMProfile(frameRate),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown conversion format")
            }),
            Result<ConversionProfile>.Fail);
    }
}

public readonly record struct SourceVideoFile
{
    public string Path { get; }

    private SourceVideoFile(string path) => Path = path;

    public static Result<SourceVideoFile> Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return Result<SourceVideoFile>.Fail($"Source video not found: {path}");

        return Result<SourceVideoFile>.Ok(new SourceVideoFile(path));
    }
}

public readonly record struct OutputFile
{
    public string Path { get; }

    private OutputFile(string path) => Path = path;

    public static Result<OutputFile> Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            return Result<OutputFile>.Fail($"Output directory not found: {directory}");

        return Result<OutputFile>.Ok(new OutputFile(path));
    }
}

public sealed record ConversionJob(SourceVideoFile SourceFile, OutputFile OutputFile, ConversionProfile Profile)
{
    public static Result<ConversionJob> Create(string sourcePath, string outputPath, ConversionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return SourceVideoFile.Create(sourcePath).Match(
            source => OutputFile.Create(outputPath).Match(
                output => Result<ConversionJob>.Ok(new ConversionJob(source, output, profile)),
                Result<ConversionJob>.Fail),
            Result<ConversionJob>.Fail);
    }
}

public readonly record struct ConversionResult(OutputFile OutputFile);
