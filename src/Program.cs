using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using GifMaker.App;
using GifMaker.Cli;

[assembly: SupportedOSPlatform("linux")]

var cliArgs = CliParser.Parse(args);
if (cliArgs is CliArgs.Gui)
    GtkProcessEnvironment.PreferCairoRendererOnX11();

Cairo.Module.Initialize();
GdkPixbuf.Module.Initialize();
Gdk.Module.Initialize();

// Handle CLI commands directly (no GUI)
if (cliArgs is not CliArgs.Gui)
{
    return await CliRunner.RunAsync(cliArgs);
}

// Launch GUI
var app = GifMakerApp.Create();
return app.Run();

internal static partial class GtkProcessEnvironment
{
    public static void PreferCairoRendererOnX11()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var runningOnX11 = string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(display) && string.IsNullOrEmpty(waylandDisplay));

        if (runningOnX11 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GSK_RENDERER")))
            g_setenv("GSK_RENDERER", "cairo", true);
    }

    [LibraryImport("libglib-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool g_setenv(
        string variable,
        string value,
        [MarshalAs(UnmanagedType.Bool)] bool overwrite);
}
