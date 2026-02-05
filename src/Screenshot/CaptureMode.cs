namespace GifMaker.Screenshot;

/// <summary>
/// Screenshot capture mode matching Ubuntu's screenshot tool.
/// </summary>
public enum CaptureMode
{
    /// <summary>User selects an area with mouse drag.</summary>
    Selection,

    /// <summary>Capture the entire screen.</summary>
    Screen,

    /// <summary>Capture the currently focused window.</summary>
    Window
}
