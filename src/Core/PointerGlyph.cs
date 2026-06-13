namespace GifMaker.Core;

public sealed class PointerGlyph
{
    private static readonly int[] LeftArrowRowWidths =
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
        6,
        7,
        7,
        8
    ];

    private PointerGlyph(ScreenSize size, ReadOnlyMemory<int> rowWidths)
    {
        Size = size;
        RowWidths = rowWidths;
    }

    public static PointerGlyph LeftArrow { get; } = new(
        new ScreenSize(16, 16),
        LeftArrowRowWidths);

    public ScreenSize Size { get; }

    public ReadOnlyMemory<int> RowWidths { get; }
}
