using GifMaker.Core;
using static GifMaker.X11.X11Interop;

namespace GifMaker.X11;

/// <summary>
/// Draws a persistent border around the recording region.
/// Uses X11 shape extension to create a transparent frame.
/// </summary>
/// <remarks>
/// Not thread-safe. All methods (Show, Hide, Dispose) must be called from the same thread.
/// This is consistent with X11's threading model which requires external synchronization.
/// </remarks>
public sealed class RegionOverlay : IDisposable
{
    private nint _display;
    private nint _window;
    private nint _shapeMask;
    private nint _gc;
    private readonly Rectangle _region;
    private readonly int _borderWidth;
    private int _disposed;

    /// <summary>
    /// Creates an overlay window around the specified region.
    /// </summary>
    /// <param name="region">Screen region to outline.</param>
    /// <param name="borderWidth">Width of the border in pixels.</param>
    /// <exception cref="InvalidOperationException">Failed to connect to X11 display.</exception>
    public RegionOverlay(Rectangle region, int borderWidth = 3)
    {
        _region = region;
        _borderWidth = borderWidth;

        _display = XOpenDisplay(0);
        if (_display == nint.Zero)
            throw new InvalidOperationException("Failed to open X11 display");

        CreateOverlayWindow();
    }

    private void CreateOverlayWindow()
    {
        var root = XDefaultRootWindow(_display);

        // Create window covering the region plus border
        // Clamp coordinates to avoid negative values at screen edges
        var totalWidth = (uint)(_region.Width + _borderWidth * 2);
        var totalHeight = (uint)(_region.Height + _borderWidth * 2);
        var x = Math.Max(0, _region.X - _borderWidth);
        var y = Math.Max(0, _region.Y - _borderWidth);

        _window = XCreateSimpleWindow(
            _display, root,
            x, y, totalWidth, totalHeight,
            0, 0, 0);

        if (_window == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 overlay window");

        // Set border
        XSetWindowBorderWidth(_display, _window, (uint)_borderWidth);
        XSetWindowBorder(_display, _window, BorderColorRed);

        // Create shape mask to make interior transparent (only show border)
        _shapeMask = XCreatePixmap(_display, _window, totalWidth, totalHeight, 1);
        if (_shapeMask == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 pixmap for shape mask");

        _gc = XCreateGC(_display, _shapeMask, 0, 0);
        if (_gc == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 graphics context");

        // Fill with 0 (transparent)
        XSetForeground(_display, _gc, 0);
        XFillRectangle(_display, _shapeMask, _gc, 0, 0, totalWidth, totalHeight);

        // Draw border region as 1 (visible)
        XSetForeground(_display, _gc, 1);
        // Top border
        XFillRectangle(_display, _shapeMask, _gc, 0, 0, totalWidth, (uint)_borderWidth);
        // Bottom border
        XFillRectangle(_display, _shapeMask, _gc, 0, (int)(totalHeight - _borderWidth), totalWidth, (uint)_borderWidth);
        // Left border
        XFillRectangle(_display, _shapeMask, _gc, 0, 0, (uint)_borderWidth, totalHeight);
        // Right border
        XFillRectangle(_display, _shapeMask, _gc, (int)(totalWidth - _borderWidth), 0, (uint)_borderWidth, totalHeight);

        // Apply shape
        XShapeCombineMask(_display, _window, ShapeBounding, 0, 0, _shapeMask, ShapeSet);

        // Make window click-through and stay on top
        SetWindowProperties();
    }

    private void SetWindowProperties()
    {
        // _NET_WM_WINDOW_TYPE_DOCK makes it stay on top
        var typeAtom = XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
        var dockAtom = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);
        XChangeProperty(_display, _window, typeAtom, XA_ATOM, 32, PropModeReplace, ref dockAtom, 1);

        // _NET_WM_STATE_ABOVE
        var stateAtom = XInternAtom(_display, "_NET_WM_STATE", false);
        var aboveAtom = XInternAtom(_display, "_NET_WM_STATE_ABOVE", false);
        XChangeProperty(_display, _window, stateAtom, XA_ATOM, 32, PropModeReplace, ref aboveAtom, 1);
    }

    /// <summary>
    /// Shows the overlay window.
    /// </summary>
    /// <remarks>Must be called from the same thread as constructor and Dispose.</remarks>
    public void Show()
    {
        if (_disposed != 0) return;
        XMapWindow(_display, _window);
        XRaiseWindow(_display, _window);
        XFlush(_display);
    }

    /// <summary>
    /// Hides the overlay window.
    /// </summary>
    /// <remarks>Must be called from the same thread as constructor and Dispose.</remarks>
    public void Hide()
    {
        if (_disposed != 0) return;
        XUnmapWindow(_display, _window);
        XFlush(_display);
    }

    /// <summary>
    /// Disposes the overlay and releases X11 resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Release resources in reverse order of creation.
        // Each cleanup is wrapped to ensure all resources are freed even if one fails.
        try { if (_gc != nint.Zero) XFreeGC(_display, _gc); } catch { /* ignore */ }
        try { if (_shapeMask != nint.Zero) XFreePixmap(_display, _shapeMask); } catch { /* ignore */ }
        try { if (_window != nint.Zero) XDestroyWindow(_display, _window); } catch { /* ignore */ }
        try { if (_display != nint.Zero) XCloseDisplay(_display); } catch { /* ignore */ }
    }
}
