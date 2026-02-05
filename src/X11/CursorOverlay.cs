using System.Runtime.Versioning;

using static GifMaker.X11.X11Interop;

namespace GifMaker.X11;

/// <summary>
/// Displays a frozen cursor image at a fixed screen position.
/// Used during screenshot selection to show where cursor will appear.
/// </summary>
/// <remarks>
/// Uses X11 shape extension for true transparency (no black box).
/// Not thread-safe. All methods must be called from the same thread.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class CursorOverlay : IDisposable
{
    // Standard arrow cursor dimensions (matches X11 left_ptr)
    private const int CursorWidth = 11;
    private const int CursorHeight = 18;

    // Arrow cursor pixel data: each row's width from tip down
    // Standard pointer: grows 1px/row, then has notch for tail
    // Row widths: tip=1, then diagonal edge grows, notch at row 11-12, tail narrows
    private static ReadOnlySpan<int> RowWidths =>
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

    private readonly nint _display;
    private readonly nint _window;
    private readonly nint _shapeMask;
    private readonly nint _gc;
    private int _disposed;

    /// <summary>
    /// Creates a cursor overlay at the specified screen position.
    /// </summary>
    /// <param name="x">Screen X coordinate (cursor tip).</param>
    /// <param name="y">Screen Y coordinate (cursor tip).</param>
    /// <exception cref="InvalidOperationException">Failed to connect to X11 display.</exception>
    public CursorOverlay(int x, int y)
    {
        _display = XOpenDisplay(0);
        if (_display == nint.Zero)
            throw new InvalidOperationException("Failed to open X11 display");

        try
        {
            (_window, _shapeMask, _gc) = CreateOverlayWindow(x, y);
        }
        catch
        {
            if (_gc != nint.Zero) XFreeGC(_display, _gc);
            if (_shapeMask != nint.Zero) XFreePixmap(_display, _shapeMask);
            if (_window != nint.Zero) XDestroyWindow(_display, _window);
            XCloseDisplay(_display);
            throw;
        }
    }

    private (nint window, nint shapeMask, nint gc) CreateOverlayWindow(int x, int y)
    {
        var root = XDefaultRootWindow(_display);
        var screen = XDefaultScreen(_display);

        // Get black and white pixels for drawing
        var blackPixel = XBlackPixel(_display, screen);
        var whitePixel = XWhitePixel(_display, screen);

        // Create window at cursor position
        var window = XCreateSimpleWindow(
            _display, root,
            x, y, CursorWidth, CursorHeight,
            0, 0, whitePixel);  // White background for cursor fill

        if (window == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 cursor overlay window");

        // Create shape mask (1-bit pixmap) for transparency
        var shapeMask = XCreatePixmap(_display, window, CursorWidth, CursorHeight, 1);
        if (shapeMask == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 pixmap for shape mask");

        // Create GC for the shape mask
        var gc = XCreateGC(_display, shapeMask, 0, 0);
        if (gc == nint.Zero)
            throw new InvalidOperationException("Failed to create X11 graphics context");

        // Draw cursor shape into the mask (1 = visible, 0 = transparent)
        DrawCursorMask(shapeMask, gc);

        // Apply shape mask - only the cursor shape is visible
        XShapeCombineMask(_display, window, ShapeBounding, 0, 0, shapeMask, ShapeSet);

        // Create GC for window drawing and draw the actual cursor
        var windowGc = XCreateGC(_display, window, 0, 0);
        if (windowGc != nint.Zero)
        {
            DrawCursor(window, windowGc, blackPixel, whitePixel);
            XFreeGC(_display, windowGc);
        }

        // Make window stay on top and be non-interactive
        SetWindowProperties(window);

        return (window, shapeMask, gc);
    }

    /// <summary>
    /// Draws the cursor shape into the 1-bit shape mask.
    /// 1 = visible (cursor pixels), 0 = transparent (background).
    /// </summary>
    private void DrawCursorMask(nint mask, nint gc)
    {
        // Clear to 0 (transparent)
        XSetForeground(_display, gc, 0);
        XFillRectangle(_display, mask, gc, 0, 0, CursorWidth, CursorHeight);

        // Draw cursor shape as 1 (visible)
        XSetForeground(_display, gc, 1);

        var rowWidths = RowWidths;
        for (var row = 0; row < rowWidths.Length && row < CursorHeight; row++)
        {
            var width = rowWidths[row];
            XFillRectangle(_display, mask, gc, 0, row, (uint)width, 1);
        }

        XFlush(_display);
    }

    /// <summary>
    /// Draws the actual cursor appearance (black outline, white fill).
    /// </summary>
    private void DrawCursor(nint window, nint gc, nint blackPixel, nint whitePixel)
    {
        var rowWidths = RowWidths;

        // Fill entire cursor shape with white first
        XSetForeground(_display, gc, whitePixel);
        for (var row = 0; row < rowWidths.Length && row < CursorHeight; row++)
        {
            var width = rowWidths[row];
            XFillRectangle(_display, window, gc, 0, row, (uint)width, 1);
        }

        // Draw black outline
        XSetForeground(_display, gc, blackPixel);

        // Left edge (column 0, full height)
        for (var row = 0; row < rowWidths.Length && row < CursorHeight; row++)
            XFillRectangle(_display, window, gc, 0, row, 1, 1);

        // Right/diagonal edge
        for (var row = 0; row < rowWidths.Length && row < CursorHeight; row++)
        {
            var width = rowWidths[row];
            if (width > 1)
                XFillRectangle(_display, window, gc, width - 1, row, 1, 1);
        }

        // Bottom of main pointer (row 10, before notch)
        XFillRectangle(_display, window, gc, 5, 10, 6, 1);

        // Inner edges of the notch (rows 11-12)
        XFillRectangle(_display, window, gc, 5, 11, 1, 1);
        XFillRectangle(_display, window, gc, 5, 12, 1, 1);

        XFlush(_display);
    }

    private void SetWindowProperties(nint window)
    {
        // _NET_WM_WINDOW_TYPE_DOCK makes it stay on top
        var typeAtom = XInternAtom(_display, "_NET_WM_WINDOW_TYPE", false);
        var dockAtom = XInternAtom(_display, "_NET_WM_WINDOW_TYPE_DOCK", false);
        XChangeProperty(_display, window, typeAtom, XA_ATOM, 32, PropModeReplace, ref dockAtom, 1);

        // _NET_WM_STATE_ABOVE
        var stateAtom = XInternAtom(_display, "_NET_WM_STATE", false);
        var aboveAtom = XInternAtom(_display, "_NET_WM_STATE_ABOVE", false);
        XChangeProperty(_display, window, stateAtom, XA_ATOM, 32, PropModeReplace, ref aboveAtom, 1);
    }

    /// <summary>
    /// Shows the cursor overlay.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The overlay has been disposed.</exception>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        XMapWindow(_display, _window);
        XRaiseWindow(_display, _window);
        XFlush(_display);
    }

    /// <summary>
    /// Hides the cursor overlay.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The overlay has been disposed.</exception>
    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
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

        try { if (_gc != nint.Zero) XFreeGC(_display, _gc); } catch { /* ignore */ }
        try { if (_shapeMask != nint.Zero) XFreePixmap(_display, _shapeMask); } catch { /* ignore */ }
        try { if (_window != nint.Zero) XDestroyWindow(_display, _window); } catch { /* ignore */ }
        try { if (_display != nint.Zero) XCloseDisplay(_display); } catch { /* ignore */ }
    }
}
