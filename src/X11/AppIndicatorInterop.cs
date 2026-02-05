using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GifMaker.X11;

/// <summary>
/// P/Invoke bindings for libayatana-appindicator3 and GTK3 menu (Ubuntu system tray).
/// </summary>
/// <remarks>
/// Requires: libayatana-appindicator3-1, libgtk-3-0
/// Install: sudo apt install libayatana-appindicator3-1
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class AppIndicatorInterop
{
    private const string LibAppIndicator = "libayatana-appindicator3.so.1";
    private const string LibGtk3 = "libgtk-3.so.0";
    private const string LibGObject = "libgobject-2.0.so.0";

    #region AppIndicator

    /// <summary>AppIndicator category for application status.</summary>
    internal const int CategoryApplicationStatus = 0;

    /// <summary>AppIndicator status values.</summary>
    internal const int StatusPassive = 0;
    internal const int StatusActive = 1;
    internal const int StatusAttention = 2;

    /// <summary>
    /// Creates a new AppIndicator.
    /// </summary>
    [LibraryImport(LibAppIndicator, EntryPoint = "app_indicator_new", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint AppIndicatorNew(string id, string iconName, int category);

    /// <summary>
    /// Sets the indicator status (passive/active/attention).
    /// </summary>
    [LibraryImport(LibAppIndicator, EntryPoint = "app_indicator_set_status")]
    internal static partial void AppIndicatorSetStatus(nint indicator, int status);

    /// <summary>
    /// Sets the menu to display when indicator is clicked.
    /// </summary>
    [LibraryImport(LibAppIndicator, EntryPoint = "app_indicator_set_menu")]
    internal static partial void AppIndicatorSetMenu(nint indicator, nint menu);

    /// <summary>
    /// Sets the indicator icon by name.
    /// </summary>
    [LibraryImport(LibAppIndicator, EntryPoint = "app_indicator_set_icon", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void AppIndicatorSetIcon(nint indicator, string iconName);

    /// <summary>
    /// Sets the indicator title (tooltip).
    /// </summary>
    [LibraryImport(LibAppIndicator, EntryPoint = "app_indicator_set_title", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void AppIndicatorSetTitle(nint indicator, string title);

    #endregion

    #region GTK3 Menu (required by AppIndicator)

    /// <summary>Creates a new GTK3 menu.</summary>
    [LibraryImport(LibGtk3, EntryPoint = "gtk_menu_new")]
    internal static partial nint GtkMenuNew();

    /// <summary>Creates a new GTK3 menu item with label.</summary>
    [LibraryImport(LibGtk3, EntryPoint = "gtk_menu_item_new_with_label", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GtkMenuItemNewWithLabel(string label);

    /// <summary>Creates a new GTK3 separator menu item.</summary>
    [LibraryImport(LibGtk3, EntryPoint = "gtk_separator_menu_item_new")]
    internal static partial nint GtkSeparatorMenuItemNew();

    /// <summary>Appends item to menu shell.</summary>
    [LibraryImport(LibGtk3, EntryPoint = "gtk_menu_shell_append")]
    internal static partial void GtkMenuShellAppend(nint menuShell, nint child);

    /// <summary>Shows all widgets in container.</summary>
    [LibraryImport(LibGtk3, EntryPoint = "gtk_widget_show_all")]
    internal static partial void GtkWidgetShowAll(nint widget);

    #endregion

    #region GObject Signal

    /// <summary>Connects a callback to a GObject signal.</summary>
    [LibraryImport(LibGObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint GSignalConnectData(
        nint instance,
        string detailedSignal,
        nint handler,
        nint data,
        nint destroyData,
        int connectFlags);

    /// <summary>Delegate for GTK3 "activate" signal (no args).</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ActivateCallback(nint widget, nint userData);

    #endregion
}
