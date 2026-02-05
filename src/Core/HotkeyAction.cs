namespace GifMaker.Core;

/// <summary>
/// Actions triggered by global hotkeys.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Initiates screen region selection for recording.</summary>
    SelectArea,

    /// <summary>Starts recording the selected region.</summary>
    StartRecording,

    /// <summary>Stops the current recording.</summary>
    StopRecording,

    /// <summary>Cancels the current operation.</summary>
    Cancel,

    /// <summary>Takes a screenshot (opens screenshot mode).</summary>
    Screenshot
}
