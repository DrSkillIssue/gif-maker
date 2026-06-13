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
    private const int CursorWidth = CursorShape.Width;
    private const int CursorHeight = CursorShape.Height;

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
        SetOverlayWindowProperties(_display, window);

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

        var rowWidths = CursorShape.RowWidths;
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
        var rowWidths = CursorShape.RowWidths;

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

    /// <summary>
    /// Shows the cursor overlay.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The overlay has been disposed.</exception>
    public void Show()
    {
        ThrowIfDisposed();
        ShowWindow(_display, _window);
    }

    /// <summary>
    /// Hides the cursor overlay.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The overlay has been disposed.</exception>
    public void Hide()
    {
        ThrowIfDisposed();
        HideWindow(_display, _window);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    /// <summary>
    /// Disposes the overlay and releases X11 resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        DisposeX11Resources(_display, _window, _shapeMask, _gc);
    }
}
