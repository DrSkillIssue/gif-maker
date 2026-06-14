namespace GifMaker.IntegrationTests;

public sealed class AppLifecycleActionTests
{
    private readonly ITestOutputHelper _output;

    public AppLifecycleActionTests(ITestOutputHelper output) => _output = output;

    [Fact(Timeout = 30000)]
    public async Task QuitActionRemovesWindowAndExitsProcess()
    {
        if (!IntegrationEnvironment.IsAvailable(_output))
            return;

        var appId = $"com.gifmaker.tests.t{Environment.ProcessId}.g{Guid.NewGuid():N}";
        await using var app = await GifMakerAppProcess.StartAsync(appId, _output);

        await X11WindowCatalog.WaitForWindowAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromSeconds(10));

        await app.ActivateActionAsync("quit");
        await app.WaitForExitAsync(TimeSpan.FromSeconds(5));

        Assert.True(app.HasExited);
        await X11WindowCatalog.WaitForNoWindowAsync(
            app.ProcessId,
            "GIF Maker",
            TimeSpan.FromSeconds(5));
    }
}
