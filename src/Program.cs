using System.Runtime.Versioning;
using GifMaker.App;

[assembly: SupportedOSPlatform("linux")]

var app = GifMakerApp.Create();
return app.Run();
