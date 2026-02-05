namespace GifMaker.Core;

/// <summary>
/// X11 display helper.
/// </summary>
public static class X11Display
{
    /// <summary>Default X11 display when DISPLAY env var is not set.</summary>
    public const string DefaultDisplay = ":0";

    /// <summary>
    /// Gets the current X11 display identifier from environment.
    /// </summary>
    /// <returns>DISPLAY env var value, or ":0" as fallback.</returns>
    public static string GetCurrent() =>
        Environment.GetEnvironmentVariable("DISPLAY") ?? DefaultDisplay;

    /// <summary>
    /// Resolves display identifier, using provided value or falling back to current.
    /// </summary>
    /// <param name="display">Optional display override.</param>
    /// <returns>Resolved display identifier.</returns>
    public static string Resolve(string? display) =>
        display ?? GetCurrent();
}
