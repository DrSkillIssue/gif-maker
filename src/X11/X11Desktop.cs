using GifMaker.Core;

namespace GifMaker.X11;

public sealed class X11Desktop
{
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

}
