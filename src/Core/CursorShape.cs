namespace GifMaker.Core;

/// <summary>
/// Standard arrow cursor shape data.
/// Matches X11 left_ptr cursor appearance.
/// </summary>
public static class CursorShape
{
    /// <summary>Arrow cursor width in pixels.</summary>
    public const int Width = 11;

    /// <summary>Arrow cursor height in pixels.</summary>
    public const int Height = 18;

    /// <summary>
    /// Arrow cursor pixel data: each row's width from tip down.
    /// Standard pointer: grows 1px/row, then has notch for tail.
    /// </summary>
    /// <remarks>
    /// Row widths describe a left-pointing arrow cursor:
    /// - Rows 0-10: diagonal edge grows from 1 to 11px
    /// - Row 11: notch (tail starts at width 6)
    /// - Rows 12-17: tail narrows from 7 to 1px
    /// </remarks>
    public static ReadOnlySpan<int> RowWidths =>
    [
        1,  // row 0: tip
        2,  // row 1
        3,  // row 2
        4,  // row 3
        5,  // row 4
        6,  // row 5
        7,  // row 6
        8,  // row 7
        9,  // row 8
        10, // row 9
        11, // row 10: widest
        6,  // row 11: notch (tail starts)
        7,  // row 12
        4,  // row 13: tail
        3,  // row 14
        2,  // row 15
        2,  // row 16
        1,  // row 17: tail end
    ];
}
