using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GifMaker.IntegrationTests;

public sealed class ScreenshotSelectionActionTests
{
    private readonly ITestOutputHelper _output;

    public ScreenshotSelectionActionTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 30000)]
    public async Task CaptureSelectionFromHiddenWindowDoesNotRemapMainWindowAndCreatesOneOverlay()
    {
        if (!IntegrationEnvironment.IsAvailable(_output))
            return;

        var appId = $"com.gifmaker.tests.t{Environment.ProcessId}.g{Guid.NewGuid():N}";
        await using var app = await GifMakerAppProcess.StartAsync(appId, _output);

        var mainWindow = await X11WindowCatalog.WaitForWindowAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromSeconds(10));
        X11WindowCatalog.Hide(mainWindow);
        await X11WindowCatalog.WaitForNoWindowAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromSeconds(5));

        await app.ActivateActionAsync("capture-selection");

        var mainWindowWasRemapped = await X11WindowCatalog.SawWindowDuringAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromMilliseconds(700));
        Assert.False(mainWindowWasRemapped);

        await X11WindowCatalog.WaitForWindowAsync(
            app.ProcessId,
            "Select screenshot area",
            TimeSpan.FromSeconds(10));

        await app.ActivateActionAsync("capture-selection");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var selectionWindows = (await X11WindowCatalog.ReadAsync())
            .Where(window => window.ProcessId == app.ProcessId && window.Title == "Select screenshot area")
            .ToArray();
        Assert.Single(selectionWindows);
    }

    [Fact(Timeout = 45000)]
    public async Task CaptureSelectionWaitsForEnterAndCopiesImagePixelsEveryTime()
    {
        if (!IntegrationEnvironment.IsAvailable(_output) || !ClipboardAssertions.IsAvailable(_output))
            return;

        var appId = $"com.gifmaker.tests.t{Environment.ProcessId}.g{Guid.NewGuid():N}";
        await using var app = await GifMakerAppProcess.StartAsync(appId, _output);
        var pngLengths = new List<int>();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ClipboardAssertions.ReplaceWithText($"gifmaker-test-{attempt}-{Guid.NewGuid():N}");
            await ClipboardAssertions.WaitUntilPngUnavailableAsync(TimeSpan.FromSeconds(2));

            await app.ActivateActionAsync("capture-selection");

            var selectionWindow = await X11WindowCatalog.WaitForWindowAsync(
                app.ProcessId,
                "Select screenshot area",
                TimeSpan.FromSeconds(10));
            if (!X11Pointer.DragSelection(selectionWindow))
            {
                _output.WriteLine("Skipping X11 integration test: XTest pointer automation is unavailable.");
                return;
            }

            var selectionWindowsBeforeEnter = (await X11WindowCatalog.ReadAsync())
                .Where(window => window.ProcessId == app.ProcessId && window.Title == "Select screenshot area")
                .ToArray();
            Assert.Single(selectionWindowsBeforeEnter);
            await ClipboardAssertions.WaitUntilPngUnavailableAsync(TimeSpan.FromMilliseconds(500));

            if (!X11Pointer.PressEnter())
            {
                _output.WriteLine("Skipping X11 integration test: XTest key automation is unavailable.");
                return;
            }

            await X11WindowCatalog.WaitForNoWindowAsync(
                app.ProcessId,
                "Select screenshot area",
                TimeSpan.FromSeconds(10));
            var png = await ClipboardAssertions.WaitForPngImageAsync(TimeSpan.FromSeconds(5));
            Assert.True(png.Length > ClipboardAssertions.PngSignatureLength);
            pngLengths.Add(png.Length);
        }

        Assert.Equal(3, pngLengths.Count);
    }
}

internal static class IntegrationEnvironment
{
    public static bool IsAvailable(ITestOutputHelper output)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            output.WriteLine("Skipping X11 integration test: DISPLAY is not set.");
            return false;
        }

        foreach (var command in new[] { "dotnet", "gdbus", "xwininfo", "xprop" })
        {
            if (FindOnPath(command) is not null)
                continue;

            output.WriteLine($"Skipping X11 integration test: `{command}` is not available on PATH.");
            return false;
        }

        return true;
    }

    private static string? FindOnPath(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

internal sealed class GifMakerAppProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _objectPath;
    private readonly ITestOutputHelper _output;

    private GifMakerAppProcess(Process process, string appId, ITestOutputHelper output)
    {
        _process = process;
        _objectPath = "/" + appId.Replace('.', '/');
        _output = output;
    }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public static async Task<GifMakerAppProcess> StartAsync(string appId, ITestOutputHelper output)
    {
        var repoRoot = RepositoryPaths.FindRoot();
        var assemblyPath = RepositoryPaths.FindBuiltApp(repoRoot);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.Environment["GIFMAKER_APP_ID"] = appId;
        startInfo.Environment["GSK_RENDERER"] = "cairo";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start GifMaker process");
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.WriteLine($"gifmaker stdout: {args.Data}");
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                output.WriteLine($"gifmaker stderr: {args.Data}");
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var app = new GifMakerAppProcess(process, appId, output);
        await WaitUntilAsync(
            async () => (await app.CallDbusAsync(app._objectPath, "org.gtk.Actions.List")).ExitCode == 0,
            TimeSpan.FromSeconds(10),
            "Timed out waiting for GifMaker DBus registration");
        return app;
    }

    public async Task ActivateActionAsync(string actionName)
    {
        var result = await CallDbusAsync(
            _objectPath,
            "org.gtk.Actions.Activate",
            actionName,
            "[]",
            "{}");
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to activate `{actionName}`: {result.StandardError}");
    }

    public Task<CommandResult> CallDbusAsync(string objectPath, string method, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("gdbus")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("call");
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add("--dest");
        startInfo.ArgumentList.Add(_objectPath.Trim('/').Replace('/', '.'));
        startInfo.ArgumentList.Add("--object-path");
        startInfo.ArgumentList.Add(objectPath);
        startInfo.ArgumentList.Add("--method");
        startInfo.ArgumentList.Add(method);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return CommandRunner.RunAsync(startInfo, TimeSpan.FromSeconds(5));
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        await _process.WaitForExitAsync().WaitAsync(timeout);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                await ActivateActionAsync("quit");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _output.WriteLine($"Failed to quit GifMaker over DBus: {ex.Message}");
        }

        if (!_process.HasExited)
        {
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }

        _process.Dispose();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string failure)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < stopAt)
        {
            if (await condition())
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException(failure);
    }
}

internal readonly record struct X11Window(nuint Id, int ProcessId, string Title, X11Bounds Bounds);

internal readonly record struct X11Bounds(int X, int Y, int Width, int Height);

internal static partial class X11WindowCatalog
{
    private static readonly Regex XWinInfoWindowLine = CreateXWinInfoWindowLineRegex();

    public static async Task<IReadOnlyList<X11Window>> ReadAsync()
    {
        var startInfo = new ProcessStartInfo("xwininfo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-root");
        startInfo.ArgumentList.Add("-tree");

        var result = await CommandRunner.RunAsync(startInfo, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"xwininfo failed: {result.StandardError}");

        var windows = new List<X11Window>();
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var match = XWinInfoWindowLine.Match(line);
            if (!match.Success)
                continue;

            var id = nuint.Parse(match.Groups["id"].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var title = match.Groups["title"].Value;
            if (title is not "GIF Maker" and not "Select screenshot area")
                continue;

            var bounds = await ReadWindowBoundsAsync(id);
            if (bounds is null)
                continue;

            var pid = await ReadWindowPidAsync(id);
            if (pid is not null)
                windows.Add(new X11Window(id, pid.Value, title, bounds.Value));
        }

        return windows;
    }

    public static async Task<X11Window> WaitForWindowAsync(int processId, string title, TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < stopAt)
        {
            var match = (await ReadAsync()).FirstOrDefault(window =>
                window.ProcessId == processId && window.Title == title);
            if (match.Id != 0)
                return match;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for `{title}` window owned by process {processId}");
    }

    public static async Task WaitForNoWindowAsync(int processId, string title, TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < stopAt)
        {
            var exists = (await ReadAsync()).Any(window =>
                window.ProcessId == processId && window.Title == title);
            if (!exists)
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for `{title}` window owned by process {processId} to hide");
    }

    public static async Task<bool> SawWindowDuringAsync(int processId, string title, TimeSpan duration)
    {
        var stopAt = DateTimeOffset.UtcNow + duration;
        while (DateTimeOffset.UtcNow < stopAt)
        {
            var exists = (await ReadAsync()).Any(window =>
                window.ProcessId == processId && window.Title == title);
            if (exists)
                return true;

            await Task.Delay(20);
        }

        return false;
    }

    public static void Hide(X11Window window)
    {
        var display = XOpenDisplay(nint.Zero);
        if (display == nint.Zero)
            throw new InvalidOperationException("Failed to open X11 display");

        try
        {
            _ = XUnmapWindow(display, (nint)window.Id);
            _ = XFlush(display);
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    private static async Task<int?> ReadWindowPidAsync(nuint id)
    {
        var startInfo = new ProcessStartInfo("xprop")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-id");
        startInfo.ArgumentList.Add("0x" + id.ToString("x", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("_NET_WM_PID");

        var result = await CommandRunner.RunAsync(startInfo, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0)
            return null;

        var marker = "_NET_WM_PID(CARDINAL) = ";
        var index = result.StandardOutput.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var pidText = result.StandardOutput[(index + marker.Length)..].Trim();
        return int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            ? pid
            : null;
    }

    private static async Task<X11Bounds?> ReadWindowBoundsAsync(nuint id)
    {
        var startInfo = new ProcessStartInfo("xwininfo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-id");
        startInfo.ArgumentList.Add("0x" + id.ToString("x", CultureInfo.InvariantCulture));

        var result = await CommandRunner.RunAsync(startInfo, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0 ||
            !result.StandardOutput.Contains("Map State: IsViewable", StringComparison.Ordinal))
        {
            return null;
        }

        int? x = null;
        int? y = null;
        int? width = null;
        int? height = null;

        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Absolute upper-left X:", StringComparison.Ordinal))
                x = int.Parse(trimmed["Absolute upper-left X:".Length..], CultureInfo.InvariantCulture);
            else if (trimmed.StartsWith("Absolute upper-left Y:", StringComparison.Ordinal))
                y = int.Parse(trimmed["Absolute upper-left Y:".Length..], CultureInfo.InvariantCulture);
            else if (trimmed.StartsWith("Width:", StringComparison.Ordinal))
                width = int.Parse(trimmed["Width:".Length..], CultureInfo.InvariantCulture);
            else if (trimmed.StartsWith("Height:", StringComparison.Ordinal))
                height = int.Parse(trimmed["Height:".Length..], CultureInfo.InvariantCulture);
        }

        return x is { } left && y is { } top && width is { } w && height is { } h
            ? new X11Bounds(left, top, w, h)
            : null;
    }

    [GeneratedRegex("""^\s*(?<id>0x[0-9a-fA-F]+)\s+"(?<title>[^"]*)":""")]
    private static partial Regex CreateXWinInfoWindowLineRegex();

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XUnmapWindow(nint display, nint window);

    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(nint display);
}

internal static partial class X11Pointer
{
    private const int CurrentScreen = -1;
    private const uint LeftButton = 1;
    private const nint ReturnKeySym = 0xff0d;
    private const ulong NoDelay = 0;
    private const ulong ShortDelay = 50;

    public static bool DragSelection(X11Window window)
    {
        if (window.Bounds.Width < 20 || window.Bounds.Height < 20)
            return false;

        var display = XOpenDisplay(nint.Zero);
        if (display == nint.Zero)
            return false;

        try
        {
            if (XTestQueryExtension(display, out _, out _, out _, out _) == 0)
                return false;

            var startX = window.Bounds.X + 10;
            var startY = window.Bounds.Y + 10;
            var endX = window.Bounds.X + Math.Min(window.Bounds.Width - 10, 160);
            var endY = window.Bounds.Y + Math.Min(window.Bounds.Height - 10, 120);
            if (endX <= startX || endY <= startY)
                return false;

            _ = XTestFakeMotionEvent(display, CurrentScreen, startX, startY, NoDelay);
            _ = XTestFakeButtonEvent(display, LeftButton, true, NoDelay);
            _ = XTestFakeMotionEvent(display, CurrentScreen, endX, endY, ShortDelay);
            _ = XTestFakeButtonEvent(display, LeftButton, false, ShortDelay);
            _ = XFlush(display);
            return true;
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    public static bool PressEnter()
    {
        var display = XOpenDisplay(nint.Zero);
        if (display == nint.Zero)
            return false;

        try
        {
            if (XTestQueryExtension(display, out _, out _, out _, out _) == 0)
                return false;

            var keycode = XKeysymToKeycode(display, ReturnKeySym);
            if (keycode == 0)
                return false;

            _ = XTestFakeKeyEvent(display, keycode, true, NoDelay);
            _ = XTestFakeKeyEvent(display, keycode, false, ShortDelay);
            _ = XFlush(display);
            return true;
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    [LibraryImport("libX11.so.6")]
    private static partial nint XOpenDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial int XFlush(nint display);

    [LibraryImport("libX11.so.6")]
    private static partial uint XKeysymToKeycode(nint display, nint keysym);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestQueryExtension(
        nint display,
        out int eventBase,
        out int errorBase,
        out int majorVersion,
        out int minorVersion);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeButtonEvent(
        nint display,
        uint button,
        [MarshalAs(UnmanagedType.Bool)] bool isPress,
        ulong delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeMotionEvent(
        nint display,
        int screen,
        int x,
        int y,
        ulong delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeKeyEvent(
        nint display,
        uint keycode,
        [MarshalAs(UnmanagedType.Bool)] bool isPress,
        ulong delay);
}

internal static class ClipboardAssertions
{
    private const string PngMime = "image/png";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static int PngSignatureLength => PngSignature.Length;

    public static bool IsAvailable(ITestOutputHelper output)
    {
        try
        {
            GtkClipboardTestRuntime.Initialize();
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            output.WriteLine($"Skipping X11 integration test: GTK native library is unavailable: {ex.Message}");
            return false;
        }

        if (!Gtk.Functions.IsInitialized() && !Gtk.Functions.InitCheck())
        {
            output.WriteLine("Skipping X11 integration test: GTK could not initialize.");
            return false;
        }

        if (Gdk.Display.GetDefault() is null)
        {
            output.WriteLine("Skipping X11 integration test: GDK display is unavailable.");
            return false;
        }

        return true;
    }

    public static void ReplaceWithText(string text)
    {
        GetClipboard().SetText(text);
        DrainMainContext();
    }

    public static async Task<byte[]> WaitForPngImageAsync(TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        Exception? lastFailure = null;

        while (DateTimeOffset.UtcNow < stopAt)
        {
            DrainMainContext();
            using var formats = GetClipboard().GetFormats();
            if (!formats.ContainMimeType(PngMime))
            {
                await Task.Delay(50);
                continue;
            }

            try
            {
                var bytes = await AwaitGtkAsync(
                    ReadClipboardMimeAsync(PngMime),
                    TimeSpan.FromSeconds(2));
                if (bytes.Length >= PngSignature.Length &&
                    bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
                {
                    return bytes;
                }

                lastFailure = new InvalidDataException($"Clipboard PNG payload had {bytes.Length} bytes.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lastFailure = ex;
            }

            await Task.Delay(50);
        }

        using var finalFormats = GetClipboard().GetFormats();
        var finalMimeTypes = finalFormats.GetMimeTypes(out _) ?? [];
        var detail = lastFailure is null ? string.Empty : $" Last read failure: {lastFailure.Message}";
        throw new TimeoutException(
            $"Timed out waiting for readable PNG clipboard content. Formats: {string.Join(", ", finalMimeTypes)}.{detail}");
    }

    public static async Task WaitUntilPngUnavailableAsync(TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < stopAt)
        {
            DrainMainContext();
            using var formats = GetClipboard().GetFormats();
            if (!formats.ContainMimeType(PngMime))
                return;

            await Task.Delay(50);
        }

        using var finalFormats = GetClipboard().GetFormats();
        var finalMimeTypes = finalFormats.GetMimeTypes(out _) ?? [];
        throw new TimeoutException(
            $"Timed out waiting for clipboard to drop PNG content. Formats: {string.Join(", ", finalMimeTypes)}.");
    }

    private static Task<byte[]> ReadClipboardMimeAsync(string mimeType)
    {
        var clipboard = GetClipboard();
        var mimeTypes = GLib.Internal.Utf8StringArrayNullTerminatedOwnedHandle.Create([mimeType]);
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        Gio.Internal.AsyncReadyCallbackAsyncHandler? callbackHandler = null;
        callbackHandler = new Gio.Internal.AsyncReadyCallbackAsyncHandler((_, result, _) =>
        {
            try
            {
                var stream = clipboard.ReadFinish(result, out var actualMime)
                    ?? throw new InvalidOperationException("Clipboard did not return an input stream.");
                if (!string.Equals(actualMime, mimeType, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Clipboard returned `{actualMime}` instead of `{mimeType}`.");

                _ = Task.Run(() =>
                {
                    try
                    {
                        using (stream)
                            tcs.SetResult(ReadAll(stream));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        tcs.SetException(ex);
                    }
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                tcs.SetException(ex);
            }
            finally
            {
                mimeTypes.Dispose();
                GC.KeepAlive(callbackHandler);
                GC.KeepAlive(clipboard);
            }
        });

        try
        {
            Gdk.Internal.Clipboard.ReadAsync(
                clipboard.Handle.DangerousGetHandle(),
                mimeTypes,
                0,
                nint.Zero,
                callbackHandler.NativeCallback,
                nint.Zero);
        }
        catch
        {
            mimeTypes.Dispose();
            throw;
        }

        return tcs.Task;
    }

    private static async Task<T> AwaitGtkAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var stopAt = DateTimeOffset.UtcNow + timeout;
        while (!task.IsCompleted)
        {
            if (DateTimeOffset.UtcNow >= stopAt)
                throw new TimeoutException("Timed out waiting for GTK async clipboard read.");

            DrainMainContext();
            await Task.Delay(10);
        }

        DrainMainContext();
        return await task;
    }

    private static byte[] ReadAll(Gio.InputStream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var bytesRead = stream.Read(buffer, null);
            if (bytesRead == 0)
                break;

            output.Write(buffer, 0, checked((int)bytesRead));
        }

        stream.Close(null);
        return output.ToArray();
    }

    private static Gdk.Clipboard GetClipboard()
    {
        var display = Gdk.Display.GetDefault()
            ?? throw new InvalidOperationException("GDK display is unavailable");
        return display.GetClipboard();
    }

    private static void DrainMainContext()
    {
        using var context = GLib.Functions.MainContextDefault();
        while (context.Pending())
            _ = context.Iteration(false);
    }
}

internal static class GtkClipboardTestRuntime
{
    private const string GtkLinuxLibraryName = "libgtk-4.so.1";

    private static bool _initialized;
    private static nint _gtkHandle;

    public static void Initialize()
    {
        if (_initialized)
            return;

        Gdk.Module.Initialize();
        NativeLibrary.SetDllImportResolver(typeof(Gtk.Module).Assembly, ResolveGtk);
        _initialized = true;
    }

    private static nint ResolveGtk(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "Gtk")
            return nint.Zero;

        if (_gtkHandle != nint.Zero)
            return _gtkHandle;

        _gtkHandle = NativeLibrary.Load(GtkLinuxLibraryName, assembly, searchPath);
        return _gtkHandle;
    }
}

internal static class RepositoryPaths
{
    public static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GifMaker.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root");
    }

    public static string FindBuiltApp(string repoRoot)
    {
        var candidates = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "bin"), "GifMaker.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        return candidates.FirstOrDefault()?.FullName
            ?? throw new InvalidOperationException("Could not locate built GifMaker.dll");
    }
}

internal static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start `{startInfo.FileName}`");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return new CommandResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }
}

internal readonly record struct CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
