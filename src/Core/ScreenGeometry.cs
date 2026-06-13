namespace GifMaker.Core;

public readonly record struct ScreenPoint(int X, int Y);

public readonly record struct ScreenSize(int Width, int Height);

public enum ScreenPointerCapture
{
    Excluded,
    Included
}
