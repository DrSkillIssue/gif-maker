namespace GifMaker.Core;

/// <summary>
/// Screen region defined by position and dimensions.
/// Immutable value type for representing rectangular areas.
/// </summary>
/// <param name="X">Horizontal position of the left edge in pixels.</param>
/// <param name="Y">Vertical position of the top edge in pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct Rectangle(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Whether this rectangle has positive dimensions (non-zero area).
    /// </summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>
    /// Creates a rectangle from two corner points, normalizing to ensure positive dimensions.
    /// </summary>
    /// <param name="x1">X coordinate of first corner.</param>
    /// <param name="y1">Y coordinate of first corner.</param>
    /// <param name="x2">X coordinate of opposite corner.</param>
    /// <param name="y2">Y coordinate of opposite corner.</param>
    /// <returns>Normalized rectangle with positive width and height.</returns>
    public static Rectangle FromPoints(int x1, int y1, int x2, int y2)
    {
        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// Creates a validated rectangle, returning error if dimensions are invalid.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="errorMessage">Error message if validation fails.</param>
    /// <returns>Result with rectangle on success, error on failure.</returns>
    public static Result<Rectangle> CreateValidated(int x, int y, int width, int height, string errorMessage)
    {
        var rect = new Rectangle(x, y, width, height);
        return rect.IsValid ? Result<Rectangle>.Ok(rect) : Result<Rectangle>.Fail(errorMessage);
    }
}
