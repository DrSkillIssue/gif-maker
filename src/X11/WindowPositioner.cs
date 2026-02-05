using System.Runtime.Versioning;
using static GifMaker.X11.X11Interop;

namespace GifMaker.X11;

/// <summary>
/// X11 native interop for window positioning.
/// GTK4 removed programmatic window positioning, so we use X11 directly.
/// Only works on X11 backend; fails gracefully on Wayland.
/// </summary>
/// <remarks>Not thread-safe. Call only from main/UI thread.</remarks>
[SupportedOSPlatform("linux")]
public static class WindowPositioner
{
    /// <summary>
    /// Screen dimensions from X11.
    /// </summary>
    public readonly record struct ScreenBounds(int Width, int Height);

    /// <summary>
    /// Gets screen dimensions.
    /// </summary>
    /// <returns><see langword="null"/> if X11 unavailable (e.g., Wayland).</returns>
    public static ScreenBounds? GetScreenBounds()
    {
        using var display = DisplayHandle.Open();
        if (!display.IsValid)
            return null;

        var screen = XDefaultScreen(display.Value);
        return new ScreenBounds(
            XDisplayWidth(display.Value, screen),
            XDisplayHeight(display.Value, screen)
        );
    }

    /// <summary>
    /// Moves window by X11 window ID.
    /// </summary>
    /// <param name="xid">X11 window ID (non-zero).</param>
    /// <param name="x">Target X coordinate.</param>
    /// <param name="y">Target Y coordinate.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if X11 unavailable.</returns>
    public static bool TryMove(nuint xid, int x, int y)
    {
        if (xid == 0)
            return false;

        using var display = DisplayHandle.Open();
        if (!display.IsValid)
            return false;

        XMoveWindowByXid(display.Value, xid, x, y);
        XSync(display.Value, discard: false);
        return true;
    }

    /// <summary>
    /// Moves a GTK window to the specified position using X11.
    /// </summary>
    /// <param name="surface">GDK surface (must be X11 backend).</param>
    /// <param name="x">Target X coordinate.</param>
    /// <param name="y">Target Y coordinate.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if not X11 or failed.</returns>
    public static bool MoveWindow(Gdk.Surface surface, int x, int y)
    {
        var handle = surface.Handle.DangerousGetHandle();
        if (handle == nint.Zero)
            return false;

        var xid = GdkX11SurfaceGetXid(handle);
        return TryMove(xid, x, y);
    }

    /// <summary>
    /// Gets the current position of a GTK window using X11.
    /// </summary>
    /// <param name="surface">GDK surface (must be X11 backend).</param>
    /// <returns>Window position, or <see langword="null"/> if not X11 or failed.</returns>
    public static (int X, int Y)? GetWindowPosition(Gdk.Surface surface)
    {
        var handle = surface.Handle.DangerousGetHandle();
        if (handle == nint.Zero)
            return null;

        var xid = GdkX11SurfaceGetXid(handle);
        if (xid == 0)
            return null;

        using var display = DisplayHandle.Open();
        if (!display.IsValid)
            return null;

        var root = (nuint)(nint)XDefaultRootWindow(display.Value);
        if (!XTranslateCoordinates(display.Value, xid, root, 0, 0, out var x, out var y, out _))
            return null;

        return (x, y);
    }

    /// <summary>
    /// Gets the current mouse pointer position (screen coordinates).
    /// </summary>
    /// <returns>Pointer position, or <see langword="null"/> if X11 unavailable.</returns>
    public static (int X, int Y)? GetPointerPosition()
    {
        using var display = DisplayHandle.Open();
        if (!display.IsValid)
            return null;

        var root = XDefaultRootWindow(display.Value);
        if (!XQueryPointer(display.Value, root, out _, out _, out var x, out var y, out _, out _, out _))
            return null;

        return (x, y);
    }
}
