namespace GifMaker.X11;

public static class CursorShape
{
    public const int Width = 11;
    public const int Height = 18;

    public static ReadOnlySpan<int> RowWidths =>
    [
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
        9,
        10,
        11,
        6,
        7,
        4,
        3,
        2,
        2,
        1,
    ];
}
