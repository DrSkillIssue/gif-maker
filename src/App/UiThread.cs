namespace GifMaker.App;

/// <summary>
/// Helper for executing code on the GTK main thread.
/// </summary>
public static class UiThread
{
    /// <summary>
    /// Schedules an action to run on the GTK main thread.
    /// </summary>
    /// <param name="action">Action to execute.</param>
    /// <remarks>
    /// Uses GLib.Functions.IdleAdd with priority 0.
    /// The action runs once and is not rescheduled.
    /// </remarks>
    public static void Run(Action action) =>
        GLib.Functions.IdleAdd(0, () => { action(); return false; });
}
