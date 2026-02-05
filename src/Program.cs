using System.Runtime.Versioning;
using GifMaker.App;
using GifMaker.Cli;

[assembly: SupportedOSPlatform("linux")]

var cliArgs = CliParser.Parse(args);

// Handle CLI commands directly (no GUI)
if (cliArgs is not CliArgs.Gui)
{
    return await CliRunner.RunAsync(cliArgs);
}

// Launch GUI
var app = GifMakerApp.Create();
return app.Run();
