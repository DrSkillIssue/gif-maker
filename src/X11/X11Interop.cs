using System.Runtime.InteropServices;

namespace GifMaker.X11;

/// <summary>
/// P/Invoke declarations for X11 libraries.
/// Centralized to avoid duplication across X11-related classes.
/// </summary>
internal static partial class X11Interop
{
    internal const string LibX11 = "libX11.so.6";
    internal const string LibXext = "libXext.so.6";
    // GDK X11 functions are exported from libgtk-4.so (GTK includes GDK)
    internal const string LibGtkGdk = "libgtk-4.so.1";

    #region Core Display Functions

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

    #endregion

    #region Window Management

    [LibraryImport(LibX11)]
    internal static partial nint XCreateSimpleWindow(
        nint display, nint parent, int x, int y, uint width, uint height,
        uint borderWidth, nint border, nint background);

    [LibraryImport(LibX11)]
    internal static partial int XDestroyWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XMapWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XUnmapWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XMoveWindow(nint display, nint window, int x, int y);

    /// <summary>
    /// Moves window by X11 window ID (nuint overload for XID).
    /// </summary>
    [LibraryImport(LibX11, EntryPoint = "XMoveWindow")]
    internal static partial int XMoveWindowByXid(nint display, nuint window, int x, int y);

    /// <summary>
    /// Translates coordinates from one window to another.
    /// Used to get window position relative to root (screen coordinates).
    /// </summary>
    [LibraryImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool XTranslateCoordinates(
        nint display, nuint srcWindow, nuint destWindow,
        int srcX, int srcY, out int destX, out int destY, out nuint child);

    [LibraryImport(LibX11)]
    internal static partial int XRaiseWindow(nint display, nint window);

    [LibraryImport(LibX11)]
    internal static partial int XSetWindowBorderWidth(nint display, nint window, uint width);

    [LibraryImport(LibX11)]
    internal static partial int XSetWindowBorder(nint display, nint window, nint pixel);

    [LibraryImport(LibX11)]
    internal static partial int XSetWindowBackground(nint display, nint window, nint pixel);

    #endregion

    #region Input Grabbing

    [LibraryImport(LibX11)]
    internal static partial int XGrabPointer(
        nint display, nint grabWindow, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents,
        uint eventMask, int pointerMode, int keyboardMode,
        nint confineTo, nint cursor, nint time);

    [LibraryImport(LibX11)]
    internal static partial int XUngrabPointer(nint display, nint time);

    [LibraryImport(LibX11)]
    internal static partial int XGrabKeyboard(
        nint display, nint grabWindow, [MarshalAs(UnmanagedType.Bool)] bool ownerEvents,
        int pointerMode, int keyboardMode, nint time);

    [LibraryImport(LibX11)]
    internal static partial int XUngrabKeyboard(nint display, nint time);

    [LibraryImport(LibX11)]
    internal static partial int XGrabKey(
        nint display, int keycode, uint modifiers, nint grabWindow,
        [MarshalAs(UnmanagedType.Bool)] bool ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(LibX11)]
    internal static partial int XUngrabKey(nint display, int keycode, uint modifiers, nint grabWindow);

    [LibraryImport(LibX11)]
    internal static partial int XSelectInput(nint display, nint window, nint eventMask);

    #endregion

    #region Events

    [LibraryImport(LibX11)]
    internal static partial int XNextEvent(nint display, ref XEvent eventReturn);

    [LibraryImport(LibX11)]
    internal static partial int XPending(nint display);

    #endregion

    #region Graphics Context

    [LibraryImport(LibX11)]
    internal static partial nint XCreateGC(nint display, nint drawable, nint valueMask, nint values);

    [LibraryImport(LibX11)]
    internal static partial int XFreeGC(nint display, nint gc);

    [LibraryImport(LibX11)]
    internal static partial int XSetForeground(nint display, nint gc, nint foreground);

    [LibraryImport(LibX11)]
    internal static partial int XSetFunction(nint display, nint gc, int function);

    [LibraryImport(LibX11)]
    internal static partial int XSetSubwindowMode(nint display, nint gc, int subwindowMode);

    [LibraryImport(LibX11)]
    internal static partial int XSetLineAttributes(
        nint display, nint gc, uint lineWidth, int lineStyle, int capStyle, int joinStyle);

    [LibraryImport(LibX11)]
    internal static partial int XDrawRectangle(
        nint display, nint drawable, nint gc, int x, int y, uint width, uint height);

    [LibraryImport(LibX11)]
    internal static partial int XFillRectangle(
        nint display, nint drawable, nint gc, int x, int y, uint width, uint height);

    #endregion

    #region Pixmaps

    [LibraryImport(LibX11)]
    internal static partial nint XCreatePixmap(nint display, nint drawable, uint width, uint height, uint depth);

    [LibraryImport(LibX11)]
    internal static partial int XFreePixmap(nint display, nint pixmap);

    #endregion

    #region Cursors

    [LibraryImport(LibX11)]
    internal static partial nint XCreateFontCursor(nint display, uint shape);

    [LibraryImport(LibX11)]
    internal static partial int XFreeCursor(nint display, nint cursor);

    #endregion

    #region Keyboard

    [LibraryImport(LibX11)]
    internal static partial int XKeysymToKeycode(nint display, nint keysym);

    #endregion

    #region Pointer Query

    /// <summary>
    /// Queries the current pointer position.
    /// </summary>
    [LibraryImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool XQueryPointer(
        nint display, nint window,
        out nint rootReturn, out nint childReturn,
        out int rootX, out int rootY,
        out int winX, out int winY,
        out uint maskReturn);

    #endregion

    #region Atoms & Properties

    [LibraryImport(LibX11, EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint XInternAtom(nint display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibX11)]
    internal static partial int XChangeProperty(
        nint display, nint window, nint property, nint type, int format, int mode, ref nint data, int nelements);

    #endregion

    #region Shape Extension (Xext)

    [LibraryImport(LibXext)]
    internal static partial void XShapeCombineMask(nint display, nint dest, int destKind, int xOff, int yOff, nint src, int op);

    #endregion

    #region GDK X11 Integration

    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_x11_surface_get_xid")]
    internal static partial nuint GdkX11SurfaceGetXid(nint surface);

    #endregion

    #region GDK Clipboard

    /// <summary>
    /// Creates a union content provider from multiple providers.
    /// The union offers all formats from all providers.
    /// </summary>
    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_content_provider_new_union")]
    internal static partial nint GdkContentProviderNewUnion(nint[] providers, nuint nProviders);

    /// <summary>
    /// Sets the clipboard content to the given provider.
    /// </summary>
    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_clipboard_set_content")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdkClipboardSetContent(nint clipboard, nint provider);

    #endregion

    #region Constants

    // Event masks
    internal const uint ButtonPressMask = 1 << 2;
    internal const uint ButtonReleaseMask = 1 << 3;
    internal const uint PointerMotionMask = 1 << 6;
    internal const uint KeyPressMask = 1 << 0;
    internal const nint ExposureMask = 1 << 15;

    // Grab modes
    internal const int GrabModeAsync = 1;

    // GC functions
    internal const int GXxor = 0x6;

    // Subwindow modes
    internal const int IncludeInferiors = 1;

    // Line styles
    internal const int LineSolid = 0;
    internal const int CapButt = 1;
    internal const int JoinMiter = 0;

    // Event types
    internal const int KeyPress = 2;
    internal const int ButtonPress = 4;
    internal const int ButtonRelease = 5;
    internal const int MotionNotify = 6;

    // Cursor shapes
    internal const uint XC_crosshair = 34;

    // CurrentTime
    internal const nint CurrentTime = 0;

    // Shape extension constants
    internal const int ShapeBounding = 0;
    internal const int ShapeSet = 0;

    // Property modes
    internal const int PropModeReplace = 0;

    // Atom types
    internal const nint XA_ATOM = 4;

    // Modifier masks
    internal const uint ShiftMask = 1u << 0;
    internal const uint LockMask = 1u << 1;   // CapsLock
    internal const uint ControlMask = 1u << 2;
    internal const uint Mod1Mask = 1u << 3;   // Alt
    internal const uint Mod2Mask = 1u << 4;   // NumLock (typically)
    internal const uint Mod4Mask = 1u << 6;   // Super/Win

    // Mask for modifier keys we care about (excludes lock states)
    internal const uint SignificantModifierMask = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;

    // Keysyms (X11 key symbols)
    internal const nint XK_r = 0x72;
    internal const nint XK_s = 0x73;
    internal const nint XK_q = 0x71;
    internal const nint XK_Escape = 0xff1b;
    internal const nint XK_Print = 0xff61;

    // Border colors
    internal const nint BorderColorRed = 0xFF3333;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Makes a window stay on top and be non-interactive (dock-like behavior).
    /// </summary>
    /// <param name="display">X11 display.</param>
    /// <param name="window">Window to configure.</param>
    internal static void SetOverlayWindowProperties(nint display, nint window)
    {
        // _NET_WM_WINDOW_TYPE_DOCK makes it stay on top
        var typeAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
        var dockAtom = XInternAtom(display, "_NET_WM_WINDOW_TYPE_DOCK", false);
        XChangeProperty(display, window, typeAtom, XA_ATOM, 32, PropModeReplace, ref dockAtom, 1);

        // _NET_WM_STATE_ABOVE
        var stateAtom = XInternAtom(display, "_NET_WM_STATE", false);
        var aboveAtom = XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
        XChangeProperty(display, window, stateAtom, XA_ATOM, 32, PropModeReplace, ref aboveAtom, 1);
    }

    /// <summary>
    /// Shows a window (maps it to screen and raises it).
    /// </summary>
    internal static void ShowWindow(nint display, nint window)
    {
        XMapWindow(display, window);
        XRaiseWindow(display, window);
        XFlush(display);
    }

    /// <summary>
    /// Hides a window (unmaps it from screen).
    /// </summary>
    internal static void HideWindow(nint display, nint window)
    {
        XUnmapWindow(display, window);
        XFlush(display);
    }

    /// <summary>
    /// Safely disposes X11 resources in order.
    /// </summary>
    internal static void DisposeX11Resources(nint display, nint window, nint shapeMask, nint gc)
    {
        try { if (gc != nint.Zero) XFreeGC(display, gc); } catch { /* ignore */ }
        try { if (shapeMask != nint.Zero) XFreePixmap(display, shapeMask); } catch { /* ignore */ }
        try { if (window != nint.Zero) XDestroyWindow(display, window); } catch { /* ignore */ }
        try { if (display != nint.Zero) XCloseDisplay(display); } catch { /* ignore */ }
    }

    #endregion
}

#region X11 Event Structures

/// <summary>
/// XEvent union - 192 bytes on 64-bit (24 longs).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    /// <summary>Event type discriminator.</summary>
    [FieldOffset(0)] public int Type;

    /// <summary>Button press/release event data.</summary>
    [FieldOffset(0)] public XButtonEvent XButton;

    /// <summary>Pointer motion event data.</summary>
    [FieldOffset(0)] public XMotionEvent XMotion;

    /// <summary>Key press/release event data.</summary>
    [FieldOffset(0)] public XKeyEvent XKey;
}

/// <summary>
/// X11 button press/release event structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XButtonEvent
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
    public uint Button;
    public int SameScreen;
}

/// <summary>
/// X11 pointer motion event structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XMotionEvent
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
    public byte IsHint;
    private byte _pad1;  // Padding for alignment
    private byte _pad2;
    private byte _pad3;
    public int SameScreen;
}

/// <summary>
/// X11 key press/release event structure.
/// </summary>
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

#endregion

#region RAII Wrappers

/// <summary>
/// RAII wrapper for X11 display connection.
/// Uses ref struct to prevent copying and ensure single disposal.
/// </summary>
internal ref struct DisplayHandle
{
    private nint _value;

    /// <summary>Raw X11 display pointer.</summary>
    public nint Value => _value;

    /// <summary>Whether the handle is valid (non-null).</summary>
    public bool IsValid => _value != nint.Zero;

    private DisplayHandle(nint value) => _value = value;

    /// <summary>Opens a connection to the X server.</summary>
    /// <returns>Handle to the display, or invalid handle if connection failed.</returns>
    public static DisplayHandle Open() => new(X11Interop.XOpenDisplay(nint.Zero));

    /// <summary>Closes the display connection.</summary>
    public void Dispose()
    {
        if (_value != nint.Zero)
        {
            X11Interop.XCloseDisplay(_value);
            _value = nint.Zero;
        }
    }
}

#endregion

#region Poll Interop

/// <summary>
/// P/Invoke for libc poll() function.
/// </summary>
internal static partial class LibC
{
    private const string LibName = "libc.so.6";

    [LibraryImport(LibName, SetLastError = true)]
    internal static partial int poll(ref PollFd fds, nuint nfds, int timeout);

    /// <summary>Data available for reading.</summary>
    internal const short POLLIN = 0x001;
}

/// <summary>
/// Structure for poll() file descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    /// <summary>File descriptor to poll.</summary>
    public int Fd;

    /// <summary>Requested events.</summary>
    public short Events;

    /// <summary>Returned events.</summary>
    public short Revents;
}

#endregion
