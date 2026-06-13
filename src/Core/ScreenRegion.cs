namespace GifMaker.Core;

/// <summary>
/// Validated screen region with positive dimensions.
/// </summary>
public readonly record struct ScreenRegion
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    private ScreenRegion(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static Result<ScreenRegion> Create(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return Result<ScreenRegion>.Fail("Region must have positive dimensions");

        return Result<ScreenRegion>.Ok(new ScreenRegion(x, y, width, height));
    }
}
