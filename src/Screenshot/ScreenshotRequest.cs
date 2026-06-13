using GifMaker.Core;

namespace GifMaker.Screenshot;

public sealed record ScreenshotRequest(
    ScreenRegion Region,
    CaptureMode Mode,
    bool ShowPointer,
    (int X, int Y)? FixedCursorPosition,
    ScreenshotOutputTarget OutputTarget);
