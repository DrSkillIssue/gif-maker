using System.Runtime.InteropServices;
using GifMaker.Core;

namespace GifMaker.X11;

internal static partial class X11Native
{
    private const string LibX11 = "libX11.so.6";
    private const string LibGtkGdk = "libgtk-4.so.1";

    internal const uint KeyPressMask = 1u << 0;
    internal const int GrabModeAsync = 1;
    internal const int KeyPress = 2;
    internal const int PropModeReplace = 0;
    internal const nint XA_ATOM = 4;
    internal const uint ShiftMask = 1u << 0;
    internal const uint LockMask = 1u << 1;
    internal const uint ControlMask = 1u << 2;
    internal const uint Mod1Mask = 1u << 3;
    internal const uint Mod2Mask = 1u << 4;
    internal const uint Mod4Mask = 1u << 6;
    internal const uint SignificantModifierMask = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;
    internal const nint XK_s = 0x73;
    internal const nint XK_Print = 0xff61;

    [LibraryImport(LibX11)]
    internal static partial nint XOpenDisplay(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XCloseDisplay(nint display);

    [LibraryImport(LibX11)]
    internal static partial nint XDefaultRootWindow(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XDefaultScreen(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XDisplayWidth(nint display, int screen);

    [LibraryImport(LibX11)]
    internal static partial int XDisplayHeight(nint display, int screen);

    [LibraryImport(LibX11)]
    internal static partial nint XBlackPixel(nint display, int screen);

    [LibraryImport(LibX11)]
    internal static partial nint XWhitePixel(nint display, int screen);

    [LibraryImport(LibX11)]
    internal static partial int XConnectionNumber(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XFlush(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [LibraryImport(LibX11)]
    internal static partial nint XCreateSimpleWindow(
        nint display,
        nint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nint border,
        nint background);

    [LibraryImport(LibX11)]
    internal static partial int XDestroyWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XMapWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XUnmapWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XRaiseWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XSetWindowBorderWidth(nint display, nint window, uint width);

    [LibraryImport(LibX11)]
    internal static partial int XSetWindowBorder(nint display, nint window, nint pixel);

    [LibraryImport(LibX11, EntryPoint = "XMoveWindow")]
    internal static partial int XMoveWindowById(nint display, nuint window, int x, int y);

    [LibraryImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool XTranslateCoordinates(
        nint display,
        nuint srcWindow,
        nuint destWindow,
        int srcX,
        int srcY,
        out int destX,
        out int destY,
        out nuint child);

    [LibraryImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool XQueryPointer(
        nint display,
        nint window,
        out nint rootReturn,
        out nint childReturn,
        out int rootX,
        out int rootY,
        out int winX,
        out int winY,
        out uint maskReturn);

    [LibraryImport(LibX11)]
    internal static partial int XGrabKey(
        nint display,
        int keycode,
        uint modifiers,
        nint grabWindow,
        [MarshalAs(UnmanagedType.Bool)] bool ownerEvents,
        int pointerMode,
        int keyboardMode);

    [LibraryImport(LibX11)]
    internal static partial int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);

    [LibraryImport(LibX11)]
    internal static partial int XSelectInput(nint display, nint window, nint eventMask);

    [LibraryImport(LibX11)]
    internal static partial int XNextEvent(nint display, ref XEvent eventReturn);

    [LibraryImport(LibX11)]
    internal static partial int XPending(nint display);

    [LibraryImport(LibX11)]
    internal static partial nint XCreateGC(nint display, nint drawable, nint valueMask, nint values);

    [LibraryImport(LibX11)]
    internal static partial int XFreeGC(nint display, nint gc);

    [LibraryImport(LibX11)]
    internal static partial int XSetForeground(nint display, nint gc, nint foreground);

    [LibraryImport(LibX11)]
    internal static partial int XFillRectangle(
        nint display,
        nint drawable,
        nint gc,
        int x,
        int y,
        uint width,
        uint height);

    [LibraryImport(LibX11)]
    internal static partial nint XCreatePixmap(nint display, nint drawable, uint width, uint height, uint depth);

    [LibraryImport(LibX11)]
    internal static partial int XFreePixmap(nint display, nint pixmap);

    [LibraryImport(LibX11)]
    internal static partial int XKeysymToKeycode(nint display, nint keysym);

    [LibraryImport(LibX11, EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint XInternAtom(
        nint display,
        string atomName,
        [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibX11)]
    internal static partial int XChangeProperty(
        nint display,
        nint window,
        nint property,
        nint type,
        int format,
        int mode,
        ref nint data,
        int nelements);

    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_x11_surface_get_xid")]
    internal static partial nuint GdkX11SurfaceGetXid(nint surface);
}

internal static partial class XextNative
{
    private const string LibXext = "libXext.so.6";

    internal const int ShapeBounding = 0;
    internal const int ShapeSet = 0;

    [LibraryImport(LibXext)]
    internal static partial void XShapeCombineMask(
        nint display,
        nint dest,
        int destKind,
        int xOff,
        int yOff,
        nint src,
        int op);
}

internal static partial class LibCNative
{
    private const string LibName = "libc.so.6";

    internal const short POLLIN = 0x001;

    [LibraryImport(LibName, SetLastError = true)]
    internal static partial int poll(ref PollFd fds, nuint nfds, int timeout);
}

[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    [FieldOffset(0)] public int Type;

    [FieldOffset(0)] public XKeyEvent XKey;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XKeyEvent
{
    public int Type;
    public nint Serial;
    public int SendEvent;
    public nint Display;
    public nint Window;
    public nint Root;
    public nint Subwindow;
    public nint Time;
    public int X;
    public int Y;
    public int XRoot;
    public int YRoot;
    public uint State;
    public uint Keycode;
    public int SameScreen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    public int Fd;
    public short Events;
    public short Revents;
}

internal sealed class XDisplayHandle : SafeHandle
{
    private XDisplayHandle() : base(nint.Zero, ownsHandle: true)
    {
    }

    public override bool IsInvalid => handle == nint.Zero;

    public static Result<XDisplayHandle> Open()
    {
        var display = X11Native.XOpenDisplay(nint.Zero);
        if (display == nint.Zero)
            return Result<XDisplayHandle>.Fail("Failed to open X11 display");

        var handle = new XDisplayHandle();
        handle.SetHandle(display);
        return Result<XDisplayHandle>.Ok(handle);
    }

    protected override bool ReleaseHandle()
    {
        _ = X11Native.XCloseDisplay(handle);
        return true;
    }
}

internal sealed class XWindowHandle : SafeHandle
{
    private readonly XDisplayHandle _display;

    public XWindowHandle(XDisplayHandle display, nint window) : base(nint.Zero, ownsHandle: true)
    {
        _display = display;
        SetHandle(window);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        _ = X11Native.XDestroyWindow(_display.DangerousGetHandle(), handle);
        return true;
    }
}

internal sealed class XPixmapHandle : SafeHandle
{
    private readonly XDisplayHandle _display;

    public XPixmapHandle(XDisplayHandle display, nint pixmap) : base(nint.Zero, ownsHandle: true)
    {
        _display = display;
        SetHandle(pixmap);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        _ = X11Native.XFreePixmap(_display.DangerousGetHandle(), handle);
        return true;
    }
}

internal sealed class XGraphicsContextHandle : SafeHandle
{
    private readonly XDisplayHandle _display;

    public XGraphicsContextHandle(XDisplayHandle display, nint graphicsContext) : base(nint.Zero, ownsHandle: true)
    {
        _display = display;
        SetHandle(graphicsContext);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        _ = X11Native.XFreeGC(_display.DangerousGetHandle(), handle);
        return true;
    }
}
