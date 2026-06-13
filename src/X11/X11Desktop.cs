using GifMaker.Core;

namespace GifMaker.X11;

public sealed class X11Desktop
{
    private static readonly nuint AllPlanes = nuint.MaxValue;
    private const int SupportedBitsPerPixel = 32;
    private const nuint SupportedRedMask = 0x00ff0000;
    private const nuint SupportedGreenMask = 0x0000ff00;
    private const nuint SupportedBlueMask = 0x000000ff;

    public Result<ScreenSize> GetScreenSize()
    {
        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenSize>.Fail);

        using var display = displayResult.GetValueOrThrow();
        var screen = X11Native.XDefaultScreen(display.DangerousGetHandle());
        return Result<ScreenSize>.Ok(new ScreenSize(
            X11Native.XDisplayWidth(display.DangerousGetHandle(), screen),
            X11Native.XDisplayHeight(display.DangerousGetHandle(), screen)));
    }

    public Result<ScreenPoint> GetPointerLocation()
    {
        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenPoint>.Fail);

        using var display = displayResult.GetValueOrThrow();
        var root = X11Native.XDefaultRootWindow(display.DangerousGetHandle());
        if (!X11Native.XQueryPointer(display.DangerousGetHandle(), root, out _, out _, out var x, out var y, out _, out _, out _))
            return Result<ScreenPoint>.Fail("Failed to query pointer location");

        return Result<ScreenPoint>.Ok(new ScreenPoint(x, y));
    }

    public Result<ScreenPoint> GetWindowLocation(Gdk.Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var handle = surface.Handle.DangerousGetHandle();
        var xid = handle == nint.Zero ? 0 : X11Native.GdkX11SurfaceGetXid(handle);
        if (xid == 0)
            return Result<ScreenPoint>.Fail("Surface is not an X11 surface");

        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<ScreenPoint>.Fail);

        using var display = displayResult.GetValueOrThrow();
        var root = (nuint)X11Native.XDefaultRootWindow(display.DangerousGetHandle());
        if (!X11Native.XTranslateCoordinates(display.DangerousGetHandle(), xid, root, 0, 0, out var x, out var y, out _))
            return Result<ScreenPoint>.Fail("Failed to get window location");

        return Result<ScreenPoint>.Ok(new ScreenPoint(x, y));
    }

    public Result MoveWindow(Gdk.Surface surface, ScreenPoint topLeft)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var handle = surface.Handle.DangerousGetHandle();
        var xid = handle == nint.Zero ? 0 : X11Native.GdkX11SurfaceGetXid(handle);
        if (xid == 0)
            return Result.Fail("Surface is not an X11 surface");

        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result.Fail);

        using var display = displayResult.GetValueOrThrow();
        _ = X11Native.XMoveWindowById(display.DangerousGetHandle(), xid, topLeft.X, topLeft.Y);
        _ = X11Native.XSync(display.DangerousGetHandle(), discard: false);
        return Result.Ok();
    }

    public unsafe Result<Cairo.ImageSurface> CaptureRootImage(ScreenRegion region, ScreenPointerCapture pointer)
    {
        if (!BitConverter.IsLittleEndian)
            return Result<Cairo.ImageSurface>.Fail("Unsupported CPU byte order for X11 image capture");

        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<Cairo.ImageSurface>.Fail);

        using var display = displayResult.GetValueOrThrow();
        var imageHandle = X11Native.XGetImage(
            display.DangerousGetHandle(),
            X11Native.XDefaultRootWindow(display.DangerousGetHandle()),
            region.X,
            region.Y,
            (uint)region.Width,
            (uint)region.Height,
            AllPlanes,
            X11Native.ZPixmap);
        if (imageHandle == nint.Zero)
            return Result<Cairo.ImageSurface>.Fail("Failed to capture X11 root image");

        Cairo.ImageSurface? surface = null;
        try
        {
            var image = System.Runtime.InteropServices.Marshal.PtrToStructure<XImage>(imageHandle);
            if (image.Data == nint.Zero ||
                image.BitsPerPixel != SupportedBitsPerPixel ||
                image.RedMask != SupportedRedMask ||
                image.GreenMask != SupportedGreenMask ||
                image.BlueMask != SupportedBlueMask ||
                image.BytesPerLine < region.Width * 4)
            {
                return Result<Cairo.ImageSurface>.Fail(
                    $"Unsupported X11 image format: {image.BitsPerPixel}bpp masks R=0x{image.RedMask:x} G=0x{image.GreenMask:x} B=0x{image.BlueMask:x}");
            }

            surface = new Cairo.ImageSurface(Cairo.Format.Rgb24, region.Width, region.Height);
            if (surface.Status != Cairo.Status.Success)
                return Result<Cairo.ImageSurface>.Fail($"Failed to allocate screenshot pixels: {surface.Status}");

            CopyRootImage(image, surface);

            if (pointer == ScreenPointerCapture.Included)
            {
                var pointerResult = BlendCursor(display.DangerousGetHandle(), region, surface);
                if (!pointerResult.IsSuccess)
                    return pointerResult.Match(
                        () => throw new InvalidOperationException("Unreachable result state"),
                        Result<Cairo.ImageSurface>.Fail);
            }
            else if (pointer != ScreenPointerCapture.Excluded)
            {
                return Result<Cairo.ImageSurface>.Fail($"Unknown pointer capture mode: {pointer}");
            }

            var captured = surface;
            surface = null;
            return Result<Cairo.ImageSurface>.Ok(captured);
        }
        finally
        {
            surface?.Dispose();
            _ = X11Native.XDestroyImage(imageHandle);
        }
    }

    private static unsafe void CopyRootImage(XImage image, Cairo.ImageSurface surface)
    {
        surface.Flush();
        var source = new ReadOnlySpan<byte>((void*)image.Data, image.BytesPerLine * image.Height);
        var destination = surface.GetData();

        for (var y = 0; y < image.Height; y++)
        {
            var sourceRow = source.Slice(y * image.BytesPerLine, image.Width * 4);
            var destinationRow = destination.Slice(y * surface.Stride, image.Width * 4);
            sourceRow.CopyTo(destinationRow);
        }

        surface.MarkDirty();
    }

    private static unsafe Result BlendCursor(nint display, ScreenRegion region, Cairo.ImageSurface surface)
    {
        var cursorHandle = X11Native.XFixesGetCursorImage(display);
        if (cursorHandle == nint.Zero)
            return Result.Fail("Failed to capture X11 cursor image");

        try
        {
            var cursor = System.Runtime.InteropServices.Marshal.PtrToStructure<XFixesCursorImage>(cursorHandle);
            if (cursor.Pixels == nint.Zero || cursor.Width == 0 || cursor.Height == 0)
                return Result.Fail("Invalid X11 cursor image");

            var left = cursor.X - cursor.XHot - region.X;
            var top = cursor.Y - cursor.YHot - region.Y;
            var cursorPixels = new ReadOnlySpan<nuint>((void*)cursor.Pixels, cursor.Width * cursor.Height);
            var clippedLeft = Math.Max(0, left);
            var clippedTop = Math.Max(0, top);
            var clippedRight = Math.Min(surface.Width, left + cursor.Width);
            var clippedBottom = Math.Min(surface.Height, top + cursor.Height);
            if (clippedLeft >= clippedRight || clippedTop >= clippedBottom)
                return Result.Ok();

            surface.Flush();
            var destination = surface.GetData();
            var sourceStartX = clippedLeft - left;
            var sourceStartY = clippedTop - top;
            var clippedWidth = clippedRight - clippedLeft;
            var clippedHeight = clippedBottom - clippedTop;

            for (var y = 0; y < clippedHeight; y++)
            {
                var sourceOffset = (sourceStartY + y) * cursor.Width + sourceStartX;
                var destinationOffset = (clippedTop + y) * surface.Stride + clippedLeft * 4;
                for (var x = 0; x < clippedWidth; x++)
                {
                    var pixel = (uint)cursorPixels[sourceOffset + x];
                    var alpha = (int)(pixel >> 24);
                    if (alpha == 0)
                    {
                        destinationOffset += 4;
                        continue;
                    }

                    var red = (int)((pixel >> 16) & 0xff);
                    var green = (int)((pixel >> 8) & 0xff);
                    var blue = (int)(pixel & 0xff);

                    destination[destinationOffset] = Blend(destination[destinationOffset], blue, alpha);
                    destination[destinationOffset + 1] = Blend(destination[destinationOffset + 1], green, alpha);
                    destination[destinationOffset + 2] = Blend(destination[destinationOffset + 2], red, alpha);
                    destinationOffset += 4;
                }
            }

            surface.MarkDirty(clippedLeft, clippedTop, clippedWidth, clippedHeight);
            return Result.Ok();
        }
        finally
        {
            _ = X11Native.XFree(cursorHandle);
        }
    }

    private static byte Blend(byte background, int foreground, int alpha) =>
        (byte)((foreground * alpha + background * (255 - alpha) + 127) / 255);

}
