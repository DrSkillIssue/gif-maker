namespace GifMaker.IntegrationTests;

public sealed class TrayMenuIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TrayMenuIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 30000)]
    public async Task StatusNotifierContextMenuExposesShowRecordScreenshotQuit()
    {
        if (!IntegrationEnvironment.IsAvailable(_output))
            return;

        var appId = $"com.gifmaker.tests.t{Environment.ProcessId}.g{Guid.NewGuid():N}";
        await using var app = await GifMakerAppProcess.StartAsync(appId, _output);

        var menuPath = await app.CallDbusAsync(
            "/StatusNotifierItem",
            "org.freedesktop.DBus.Properties.Get",
            "org.kde.StatusNotifierItem",
            "Menu");
        Assert.Equal(0, menuPath.ExitCode);
        Assert.Contains("/StatusNotifierItem/Menu", menuPath.StandardOutput, StringComparison.Ordinal);

        var layout = await app.CallDbusAsync(
            "/StatusNotifierItem/Menu",
            "com.canonical.dbusmenu.GetLayout",
            "0",
            "-1",
            "[]");
        Assert.True(
            layout.ExitCode == 0,
            $"GetLayout failed with stderr `{layout.StandardError}` and stdout `{layout.StandardOutput}`.");
        Assert.Contains("Show Window", layout.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Record", layout.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Screenshot", layout.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Quit", layout.StandardOutput, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30000)]
    public async Task StatusNotifierSecondaryActivateStartsSelectionWithoutPresentingMainWindow()
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

        var activation = await app.CallDbusAsync(
            "/StatusNotifierItem",
            "org.kde.StatusNotifierItem.SecondaryActivate",
            "0",
            "0");
        Assert.Equal(0, activation.ExitCode);

        var mainWindowWasRemapped = await X11WindowCatalog.SawWindowDuringAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromMilliseconds(700));
        Assert.False(mainWindowWasRemapped);

        await X11WindowCatalog.WaitForWindowAsync(
            app.ProcessId,
            "Select screenshot area",
            TimeSpan.FromSeconds(10));
    }
}
