using System.Runtime.InteropServices;
using GifMaker.Core;

namespace GifMaker.X11;

internal static partial class X11Native
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";
    private const string LibGtkGdk = "libgtk-4.so.1";

    internal const int ZPixmap = 2;
    internal const uint KeyPressMask = 1u << 0;
    internal const int GrabModeAsync = 1;
    internal const int KeyPress = 2;
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
    internal static partial int XConnectionNumber(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XFlush(nint display);

    [LibraryImport(LibX11)]
    internal static partial int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);

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
    internal static partial int XKeysymToKeycode(nint display, nint keysym);

    [LibraryImport(LibX11)]
    internal static partial nint XGetImage(
        nint display,
        nint drawable,
        int x,
        int y,
        uint width,
        uint height,
        nuint planeMask,
        int format);

    [LibraryImport(LibX11)]
    internal static partial int XDestroyImage(nint image);

    [LibraryImport(LibX11)]
    internal static partial int XFree(nint data);

    [LibraryImport(LibXfixes)]
    internal static partial nint XFixesGetCursorImage(nint display);

    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_x11_surface_get_xid")]
    internal static partial nuint GdkX11SurfaceGetXid(nint surface);
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

[StructLayout(LayoutKind.Sequential)]
internal readonly struct XImage
{
    public readonly int Width;
    public readonly int Height;
    public readonly int XOffset;
    public readonly int Format;
    public readonly nint Data;
    public readonly int ByteOrder;
    public readonly int BitmapUnit;
    public readonly int BitmapBitOrder;
    public readonly int BitmapPad;
    public readonly int Depth;
    public readonly int BytesPerLine;
    public readonly int BitsPerPixel;
    public readonly nuint RedMask;
    public readonly nuint GreenMask;
    public readonly nuint BlueMask;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct XFixesCursorImage
{
    public readonly short X;
    public readonly short Y;
    public readonly ushort Width;
    public readonly ushort Height;
    public readonly ushort XHot;
    public readonly ushort YHot;
    public readonly nuint CursorSerial;
    public readonly nint Pixels;
    public readonly nint Atom;
    public readonly nint Name;
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
