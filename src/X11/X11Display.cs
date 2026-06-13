namespace GifMaker.X11;

public static class X11Display
{
    public const string DefaultDisplay = ":0";

    public static string GetCurrent() =>
        Environment.GetEnvironmentVariable("DISPLAY") ?? DefaultDisplay;

    public static string Resolve(string? display) =>
        display ?? GetCurrent();
}
