namespace GifMaker.Core;

/// <summary>
/// Actions triggered by global hotkeys.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Initiates screen region selection.</summary>
    SelectArea,
    
    /// <summary>Starts recording the selected region.</summary>
    StartRecording,
    
    /// <summary>Stops the current recording.</summary>
    StopRecording,
    
    /// <summary>Cancels the current operation.</summary>
    Cancel
}
