using System.Runtime.Versioning;
using static GifMaker.X11.X11Interop;
using static GifMaker.X11.LibC;

namespace GifMaker.X11;

/// <summary>
/// Registers and listens for global X11 hotkeys using proper fd polling.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class GlobalHotkey : IDisposable
{
    #region Constants

    // Lock modifier combinations to grab (none, CapsLock, NumLock, both)
    private static ReadOnlySpan<uint> LockMasks => [0u, LockMask, Mod2Mask, LockMask | Mod2Mask];

    // Poll timeout in milliseconds
    private const int PollTimeoutMs = 100;

    #endregion

    #region Types

    /// <summary>
    /// Binding between a hotkey action and its X11 key combination.
    /// </summary>
    private readonly record struct HotkeyBinding(HotkeyAction Action, nint Keysym, uint Modifiers);

    /// <summary>
    /// Cached keycode for a registered hotkey.
    /// </summary>
    private record struct RegisteredKey(HotkeyAction Action, int Keycode, uint Modifiers);

    #endregion

    // All supported hotkey bindings - single source of truth
    private static readonly HotkeyBinding[] Bindings =
    [
        new(HotkeyAction.SelectArea, XK_s, ControlMask | Mod1Mask),      // Ctrl+Alt+S
        new(HotkeyAction.StartRecording, XK_r, ControlMask | Mod1Mask),  // Ctrl+Alt+R
        new(HotkeyAction.StopRecording, XK_q, ControlMask | Mod1Mask),   // Ctrl+Alt+Q
        new(HotkeyAction.Cancel, XK_Escape, 0u),                         // Escape
        new(HotkeyAction.Screenshot, XK_Print, 0u),                      // Print Screen
    ];

    private readonly nint _display;
    private readonly nint _root;
    private readonly int _connectionFd;

    // Fixed-size array for registered keys (max = number of HotkeyAction values)
    private const int MaxHotkeyActions = 5; // Must match HotkeyAction enum count
    private readonly RegisteredKey[] _registeredKeys = new RegisteredKey[MaxHotkeyActions];
    private int _registeredCount;

    private readonly Lock _lock = new();
    private int _disposed;

    /// <summary>
    /// Creates a new GlobalHotkey instance and connects to X11.
    /// </summary>
    /// <exception cref="InvalidOperationException">Failed to connect to X11 display.</exception>
    public GlobalHotkey()
    {
        _display = XOpenDisplay(0);
        if (_display == nint.Zero)
            throw new InvalidOperationException("Failed to open X11 display");

        _root = XDefaultRootWindow(_display);
        _connectionFd = XConnectionNumber(_display);

        // Set up event mask once
        _ = XSelectInput(_display, _root, (nint)KeyPressMask);
    }

    /// <summary>
    /// Registers a hotkey combination.
    /// </summary>
    /// <returns>True if registered successfully, false otherwise.</returns>
    public bool Register(HotkeyAction action)
    {
        ThrowIfDisposed();

        // Find binding for action
        HotkeyBinding? binding = null;
        foreach (var b in Bindings)
        {
            if (b.Action == action)
            {
                binding = b;
                break;
            }
        }

        if (binding is not { } bind)
            return false;

        // Check if already registered
        for (var i = 0; i < _registeredCount; i++)
        {
            if (_registeredKeys[i].Action == action)
                return true; // Already registered
        }

        var keycode = XKeysymToKeycode(_display, bind.Keysym);
        if (keycode == 0)
            return false;

        // Grab with all lock modifier combinations
        var anyFailed = false;
        foreach (var lockMask in LockMasks)
        {
            var result = XGrabKey(
                _display, keycode, bind.Modifiers | lockMask, _root,
                ownerEvents: false, GrabModeAsync, GrabModeAsync);

            if (result == 0)
                anyFailed = true;
        }

        _ = XFlush(_display);

        if (anyFailed)
        {
            // Best effort: ungrab what we may have grabbed
            UngrabKey(keycode, bind.Modifiers);
            return false;
        }

        _registeredKeys[_registeredCount++] = new RegisteredKey(action, keycode, bind.Modifiers);
        return true;
    }

    /// <summary>
    /// Waits for a registered hotkey and returns the action.
    /// Uses poll() for efficient waiting instead of spin-wait.
    /// </summary>
    public HotkeyAction WaitForHotkey(CancellationToken ct = default)
    {
        if (_registeredCount == 0)
            throw new InvalidOperationException("No hotkeys registered");

        var pollFd = new PollFd
        {
            Fd = _connectionFd,
            Events = POLLIN,
            Revents = 0
        };

        XEvent ev = default;

        while (!ct.IsCancellationRequested)
        {
            // Check disposal and perform X11 operations under lock
            // to prevent race with Dispose() closing the display
            lock (_lock)
            {
                if (_disposed != 0)
                    return HotkeyAction.Cancel;

                // Process any pending events
                while (XPending(_display) > 0)
                {
                    XNextEvent(_display, ref ev);

                    if (ev.Type != KeyPress)
                        continue;

                    if (MatchHotkey(ev.XKey) is { } action)
                        return action;
                }
            }

            // Poll for new events with timeout (allows cancellation check)
            // This is outside the lock since poll() blocks
            pollFd.Revents = 0;
            var pollResult = poll(ref pollFd, 1, PollTimeoutMs);

            // pollResult: >0 = ready, 0 = timeout, -1 = error
            if (pollResult < 0)
            {
                // EINTR or other error - just retry
                continue;
            }
        }

        return HotkeyAction.Cancel;
    }

    /// <summary>
    /// Matches a key event against registered hotkeys.
    /// </summary>
    private HotkeyAction? MatchHotkey(XKeyEvent key)
    {
        var keycode = (int)key.Keycode;
        var modifiers = key.State & SignificantModifierMask;

        for (var i = 0; i < _registeredCount; i++)
        {
            ref readonly var registered = ref _registeredKeys[i];
            if (registered.Keycode == keycode && registered.Modifiers == modifiers)
                return registered.Action;
        }

        return null;
    }

    /// <summary>
    /// Ungrabs a key with all lock modifier combinations.
    /// </summary>
    private void UngrabKey(int keycode, uint modifiers)
    {
        foreach (var lockMask in LockMasks)
        {
            _ = XUngrabKey(_display, keycode, modifiers | lockMask, _root);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    /// <summary>
    /// Disposes resources and unregisters all hotkeys.
    /// </summary>
    /// <remarks>
    /// Ungrab operations are best-effort; the X server releases grabs automatically
    /// when the display connection closes.
    /// </remarks>
    public void Dispose()
    {
        // Acquire lock to synchronize with WaitForHotkey
        lock (_lock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_display != nint.Zero)
            {
                for (var i = 0; i < _registeredCount; i++)
                {
                    ref readonly var key = ref _registeredKeys[i];
                    UngrabKey(key.Keycode, key.Modifiers);
                }

                _ = XCloseDisplay(_display);
            }
        }
    }
}
