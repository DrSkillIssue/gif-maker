using GifMaker.Core;

namespace GifMaker.X11;

public readonly record struct OverlayBorder(int Width, uint RgbColor);

public sealed class ScreenOverlay : IDisposable
{
    private readonly XDisplayHandle _display;
    private readonly XWindowHandle _window;
    private readonly XPixmapHandle _shapeMask;
    private readonly XGraphicsContextHandle _graphicsContext;
    private int _disposed;

    private ScreenOverlay(
        XDisplayHandle display,
        XWindowHandle window,
        XPixmapHandle shapeMask,
        XGraphicsContextHandle graphicsContext)
    {
        _display = display;
        _window = window;
        _shapeMask = shapeMask;
        _graphicsContext = graphicsContext;
    }

    public static Result<ScreenOverlay> CreateRegionFrame(ScreenRegion region, OverlayBorder border)
    {
        if (border.Width <= 0)
            return Result<ScreenOverlay>.Fail("Overlay border width must be positive");

        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenOverlay>.Fail);

        XDisplayHandle? display = null;
        XWindowHandle? window = null;
        XPixmapHandle? shapeMask = null;
        XGraphicsContextHandle? graphicsContext = null;

        try
        {
            display = displayResult.GetValueOrThrow();
            var displayHandle = display.DangerousGetHandle();
            var root = X11Native.XDefaultRootWindow(displayHandle);
            var totalWidth = (uint)(region.Width + border.Width * 2);
            var totalHeight = (uint)(region.Height + border.Width * 2);
            var x = Math.Max(0, region.X - border.Width);
            var y = Math.Max(0, region.Y - border.Width);
            var rawWindow = X11Native.XCreateSimpleWindow(
                displayHandle,
                root,
                x,
                y,
                totalWidth,
                totalHeight,
                0,
                0,
                0);

            if (rawWindow == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 overlay window");

            window = new XWindowHandle(display, rawWindow);
            _ = X11Native.XSetWindowBorderWidth(displayHandle, rawWindow, (uint)border.Width);
            _ = X11Native.XSetWindowBorder(displayHandle, rawWindow, (nint)border.RgbColor);

            var rawShapeMask = X11Native.XCreatePixmap(displayHandle, rawWindow, totalWidth, totalHeight, 1);
            if (rawShapeMask == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 pixmap for shape mask");

            shapeMask = new XPixmapHandle(display, rawShapeMask);
            var rawGraphicsContext = X11Native.XCreateGC(displayHandle, rawShapeMask, 0, 0);
            if (rawGraphicsContext == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 graphics context");

            graphicsContext = new XGraphicsContextHandle(display, rawGraphicsContext);
            _ = X11Native.XSetForeground(displayHandle, rawGraphicsContext, 0);
            _ = X11Native.XFillRectangle(displayHandle, rawShapeMask, rawGraphicsContext, 0, 0, totalWidth, totalHeight);
            _ = X11Native.XSetForeground(displayHandle, rawGraphicsContext, 1);
            _ = X11Native.XFillRectangle(displayHandle, rawShapeMask, rawGraphicsContext, 0, 0, totalWidth, (uint)border.Width);
            _ = X11Native.XFillRectangle(displayHandle, rawShapeMask, rawGraphicsContext, 0, (int)(totalHeight - border.Width), totalWidth, (uint)border.Width);
            _ = X11Native.XFillRectangle(displayHandle, rawShapeMask, rawGraphicsContext, 0, 0, (uint)border.Width, totalHeight);
            _ = X11Native.XFillRectangle(displayHandle, rawShapeMask, rawGraphicsContext, (int)(totalWidth - border.Width), 0, (uint)border.Width, totalHeight);

            XextNative.XShapeCombineMask(
                displayHandle,
                rawWindow,
                XextNative.ShapeBounding,
                0,
                0,
                rawShapeMask,
                XextNative.ShapeSet);
            ApplyOverlayWindowHints(displayHandle, rawWindow);

            var overlay = new ScreenOverlay(display, window, shapeMask, graphicsContext);
            display = null;
            window = null;
            shapeMask = null;
            graphicsContext = null;
            return Result<ScreenOverlay>.Ok(overlay);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, ex.Message);
        }
    }

    public static Result<ScreenOverlay> CreateCursor(ScreenPoint position)
    {
        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenOverlay>.Fail);

        XDisplayHandle? display = null;
        XWindowHandle? window = null;
        XPixmapHandle? shapeMask = null;
        XGraphicsContextHandle? graphicsContext = null;

        try
        {
            display = displayResult.GetValueOrThrow();
            var displayHandle = display.DangerousGetHandle();
            var root = X11Native.XDefaultRootWindow(displayHandle);
            var screen = X11Native.XDefaultScreen(displayHandle);
            var blackPixel = X11Native.XBlackPixel(displayHandle, screen);
            var whitePixel = X11Native.XWhitePixel(displayHandle, screen);
            var glyph = PointerGlyph.LeftArrow;
            var width = (uint)glyph.Size.Width;
            var height = (uint)glyph.Size.Height;
            var rawWindow = X11Native.XCreateSimpleWindow(
                displayHandle,
                root,
                position.X,
                position.Y,
                width,
                height,
                0,
                0,
                whitePixel);

            if (rawWindow == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 cursor overlay window");

            window = new XWindowHandle(display, rawWindow);
            var rawShapeMask = X11Native.XCreatePixmap(displayHandle, rawWindow, width, height, 1);
            if (rawShapeMask == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 pixmap for shape mask");

            shapeMask = new XPixmapHandle(display, rawShapeMask);
            var rawGraphicsContext = X11Native.XCreateGC(displayHandle, rawShapeMask, 0, 0);
            if (rawGraphicsContext == nint.Zero)
                return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, "Failed to create X11 graphics context");

            graphicsContext = new XGraphicsContextHandle(display, rawGraphicsContext);
            DrawCursorMask(displayHandle, rawShapeMask, rawGraphicsContext, glyph);

            XextNative.XShapeCombineMask(
                displayHandle,
                rawWindow,
                XextNative.ShapeBounding,
                0,
                0,
                rawShapeMask,
                XextNative.ShapeSet);

            var windowGraphicsContext = X11Native.XCreateGC(displayHandle, rawWindow, 0, 0);
            if (windowGraphicsContext != nint.Zero)
            {
                try
                {
                    DrawCursor(displayHandle, rawWindow, windowGraphicsContext, blackPixel, whitePixel, glyph);
                }
                finally
                {
                    _ = X11Native.XFreeGC(displayHandle, windowGraphicsContext);
                }
            }

            ApplyOverlayWindowHints(displayHandle, rawWindow);

            var overlay = new ScreenOverlay(display, window, shapeMask, graphicsContext);
            display = null;
            window = null;
            shapeMask = null;
            graphicsContext = null;
            return Result<ScreenOverlay>.Ok(overlay);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DisposeFailedOverlay(display, window, shapeMask, graphicsContext, ex.Message);
        }
    }

    public Result Show()
    {
        if (_disposed != 0)
            return Result.Fail("Screen overlay is disposed");

        var displayHandle = _display.DangerousGetHandle();
        var windowHandle = _window.DangerousGetHandle();
        _ = X11Native.XMapWindow(displayHandle, windowHandle);
        _ = X11Native.XRaiseWindow(displayHandle, windowHandle);
        _ = X11Native.XFlush(displayHandle);
        return Result.Ok();
    }

    public Result Hide()
    {
        if (_disposed != 0)
            return Result.Fail("Screen overlay is disposed");

        var displayHandle = _display.DangerousGetHandle();
        _ = X11Native.XUnmapWindow(displayHandle, _window.DangerousGetHandle());
        _ = X11Native.XFlush(displayHandle);
        return Result.Ok();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _graphicsContext.Dispose();
        _shapeMask.Dispose();
        _window.Dispose();
        _display.Dispose();
    }

    private static Result<ScreenOverlay> DisposeFailedOverlay(
        XDisplayHandle? display,
        XWindowHandle? window,
        XPixmapHandle? shapeMask,
        XGraphicsContextHandle? graphicsContext,
        string error)
    {
        graphicsContext?.Dispose();
        shapeMask?.Dispose();
        window?.Dispose();
        display?.Dispose();
        return Result<ScreenOverlay>.Fail(error);
    }

    private static void ApplyOverlayWindowHints(nint display, nint window)
    {
        var typeAtom = X11Native.XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
        var dockAtom = X11Native.XInternAtom(display, "_NET_WM_WINDOW_TYPE_DOCK", false);
        _ = X11Native.XChangeProperty(display, window, typeAtom, X11Native.XA_ATOM, 32, X11Native.PropModeReplace, ref dockAtom, 1);

        var stateAtom = X11Native.XInternAtom(display, "_NET_WM_STATE", false);
        var aboveAtom = X11Native.XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
        _ = X11Native.XChangeProperty(display, window, stateAtom, X11Native.XA_ATOM, 32, X11Native.PropModeReplace, ref aboveAtom, 1);
    }

    private static void DrawCursorMask(nint display, nint mask, nint graphicsContext, PointerGlyph glyph)
    {
        var size = glyph.Size;
        var rowWidths = glyph.RowWidths.Span;
        _ = X11Native.XSetForeground(display, graphicsContext, 0);
        _ = X11Native.XFillRectangle(display, mask, graphicsContext, 0, 0, (uint)size.Width, (uint)size.Height);
        _ = X11Native.XSetForeground(display, graphicsContext, 1);

        for (var row = 0; row < rowWidths.Length && row < size.Height; row++)
        {
            var rowWidth = rowWidths[row];
            _ = X11Native.XFillRectangle(display, mask, graphicsContext, 0, row, (uint)rowWidth, 1);
        }

        _ = X11Native.XFlush(display);
    }

    private static void DrawCursor(
        nint display,
        nint window,
        nint graphicsContext,
        nint blackPixel,
        nint whitePixel,
        PointerGlyph glyph)
    {
        var size = glyph.Size;
        var rowWidths = glyph.RowWidths.Span;
        _ = X11Native.XSetForeground(display, graphicsContext, whitePixel);

        for (var row = 0; row < rowWidths.Length && row < size.Height; row++)
        {
            var rowWidth = rowWidths[row];
            _ = X11Native.XFillRectangle(display, window, graphicsContext, 0, row, (uint)rowWidth, 1);
        }

        _ = X11Native.XSetForeground(display, graphicsContext, blackPixel);

        for (var row = 0; row < rowWidths.Length && row < size.Height; row++)
            _ = X11Native.XFillRectangle(display, window, graphicsContext, 0, row, 1, 1);

        for (var row = 0; row < rowWidths.Length && row < size.Height; row++)
        {
            var rowWidth = rowWidths[row];
            if (rowWidth > 1)
                _ = X11Native.XFillRectangle(display, window, graphicsContext, rowWidth - 1, row, 1, 1);
        }

        _ = X11Native.XFillRectangle(display, window, graphicsContext, 5, 10, 6, 1);
        _ = X11Native.XFillRectangle(display, window, graphicsContext, 5, 11, 1, 1);
        _ = X11Native.XFillRectangle(display, window, graphicsContext, 5, 12, 1, 1);
        _ = X11Native.XFlush(display);
    }
}
