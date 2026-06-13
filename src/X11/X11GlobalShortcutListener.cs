using GifMaker.Core;

namespace GifMaker.X11;

public readonly record struct X11Shortcut<TCommand>(
    TCommand Command,
    X11Key Key,
    X11Modifiers Modifiers)
    where TCommand : notnull;

public enum X11Key
{
    S,
    Print
}

[Flags]
public enum X11Modifiers
{
    None = 0,
    Control = 1,
    Alt = 2
}

public sealed class X11GlobalShortcutListener<TCommand> : IDisposable
    where TCommand : notnull
{
    private static ReadOnlySpan<uint> LockMasks => [0u, X11Native.LockMask, X11Native.Mod2Mask, X11Native.LockMask | X11Native.Mod2Mask];

    private const int PollTimeoutMs = 100;

    private readonly XDisplayHandle _display;
    private readonly nint _root;
    private readonly int _connectionFd;
    private readonly RegisteredShortcut[] _shortcuts;
    private readonly Lock _lock = new();
    private int _disposed;

    private X11GlobalShortcutListener(
        XDisplayHandle display,
        nint root,
        int connectionFd,
        RegisteredShortcut[] shortcuts)
    {
        _display = display;
        _root = root;
        _connectionFd = connectionFd;
        _shortcuts = shortcuts;
    }

    public static Result<X11GlobalShortcutListener<TCommand>> Create(
        IReadOnlyList<X11Shortcut<TCommand>> shortcuts)
    {
        if (shortcuts.Count == 0)
            return Result<X11GlobalShortcutListener<TCommand>>.Fail("At least one global shortcut is required");

        var displayResult = XDisplayHandle.Open();
        if (!displayResult.IsSuccess)
            return displayResult.Match(
                _ => throw new InvalidOperationException("Unreachable result state"),
                Result<X11GlobalShortcutListener<TCommand>>.Fail);

        var display = displayResult.GetValueOrThrow();
        var displayHandle = display.DangerousGetHandle();
        var root = X11Native.XDefaultRootWindow(displayHandle);
        var registered = new List<RegisteredShortcut>(shortcuts.Count);
        _ = X11Native.XSelectInput(displayHandle, root, (nint)X11Native.KeyPressMask);

        foreach (var shortcut in shortcuts)
        {
            var keycode = X11Native.XKeysymToKeycode(displayHandle, ToKeySym(shortcut.Key));
            if (keycode == 0)
            {
                UngrabRegistered(displayHandle, root, registered);
                display.Dispose();
                return Result<X11GlobalShortcutListener<TCommand>>.Fail($"Failed to translate X11 key: {shortcut.Key}");
            }

            var modifiers = ToNativeModifiers(shortcut.Modifiers);
            var failed = false;
            foreach (var lockMask in LockMasks)
            {
                var result = X11Native.XGrabKey(
                    displayHandle,
                    keycode,
                    modifiers | lockMask,
                    root,
                    ownerEvents: false,
                    X11Native.GrabModeAsync,
                    X11Native.GrabModeAsync);

                if (result == 0)
                    failed = true;
            }

            if (failed)
            {
                foreach (var lockMask in LockMasks)
                    _ = X11Native.XUngrabKey(displayHandle, keycode, modifiers | lockMask, root);

                UngrabRegistered(displayHandle, root, registered);
                display.Dispose();
                return Result<X11GlobalShortcutListener<TCommand>>.Fail($"Failed to register global shortcut: {shortcut.Key}");
            }

            registered.Add(new RegisteredShortcut(shortcut.Command, keycode, modifiers));
        }

        _ = X11Native.XFlush(displayHandle);
        return Result<X11GlobalShortcutListener<TCommand>>.Ok(new X11GlobalShortcutListener<TCommand>(
            display,
            root,
            X11Native.XConnectionNumber(displayHandle),
            registered.ToArray()));
    }

    public Result<TCommand> Wait(CancellationToken ct = default)
    {
        var pollFd = new PollFd
        {
            Fd = _connectionFd,
            Events = LibCNative.POLLIN,
            Revents = 0
        };

        XEvent ev = default;

        while (!ct.IsCancellationRequested)
        {
            lock (_lock)
            {
                if (_disposed != 0)
                    return Result<TCommand>.Fail("Global shortcut listener stopped");

                var displayHandle = _display.DangerousGetHandle();
                while (X11Native.XPending(displayHandle) > 0)
                {
                    _ = X11Native.XNextEvent(displayHandle, ref ev);
                    if (ev.Type != X11Native.KeyPress)
                        continue;

                    var keycode = (int)ev.XKey.Keycode;
                    var modifiers = ev.XKey.State & X11Native.SignificantModifierMask;
                    foreach (var shortcut in _shortcuts)
                    {
                        if (shortcut.Keycode == keycode && shortcut.Modifiers == modifiers)
                            return Result<TCommand>.Ok(shortcut.Command);
                    }
                }
            }

            pollFd.Revents = 0;
            if (LibCNative.poll(ref pollFd, 1, PollTimeoutMs) < 0)
                continue;
        }

        return Result<TCommand>.Fail("Global shortcut listener stopped");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            UngrabRegistered(_display.DangerousGetHandle(), _root, _shortcuts);
            _display.Dispose();
        }
    }

    private static void UngrabRegistered(nint display, nint root, IReadOnlyList<RegisteredShortcut> shortcuts)
    {
        foreach (var shortcut in shortcuts)
        {
            foreach (var lockMask in LockMasks)
                _ = X11Native.XUngrabKey(display, shortcut.Keycode, shortcut.Modifiers | lockMask, root);
        }

        _ = X11Native.XFlush(display);
    }

    private static nint ToKeySym(X11Key key) =>
        key switch
        {
            X11Key.S => X11Native.XK_s,
            X11Key.Print => X11Native.XK_Print,
            _ => throw new InvalidOperationException($"Unhandled X11 key: {key}")
        };

    private static uint ToNativeModifiers(X11Modifiers modifiers)
    {
        uint native = 0;
        if ((modifiers & X11Modifiers.Control) != 0)
            native |= X11Native.ControlMask;
        if ((modifiers & X11Modifiers.Alt) != 0)
            native |= X11Native.Mod1Mask;
        return native;
    }

    private readonly record struct RegisteredShortcut(TCommand Command, int Keycode, uint Modifiers);
}
