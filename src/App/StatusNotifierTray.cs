using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GifMaker.App;

/// <summary>
/// System tray using StatusNotifierItem D-Bus protocol via P/Invoke.
/// Works with GNOME, KDE, and other modern Linux desktops.
/// Uses direct GLib D-Bus calls for proper integration.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class StatusNotifierTray : IDisposable
{
    private const string LibGio = "libgio-2.0.so.0";
    private const string LibGLib = "libglib-2.0.so.0";

    private const string WatcherService = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const string WatcherInterface = "org.kde.StatusNotifierWatcher";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string ObjectPath = "/StatusNotifierItem";

    private readonly nint _connection;
    private readonly uint _registrationId;
    private readonly nint _nodeInfo;
    private readonly nint _vtablePtr;
    private readonly GCHandle _selfHandle; // prevent GC during D-Bus callbacks
    private int _disposed;

    private string _iconName = "camera-video";
    private string _title = "GifMaker";
    private string _tooltip = "GifMaker - Screen Recorder";

    // prevent GC of delegates
    private readonly GDBusMethodCallFunc _methodCallFunc;
    private readonly GDBusGetPropertyFunc _getPropertyFunc;

    /// <summary>Fired when user activates (left-clicks) the tray icon.</summary>
    public event Action? Activated;

    /// <summary>Fired when user secondary-activates (middle-clicks) the tray icon.</summary>
    public event Action? SecondaryActivated;

    /// <summary>
    /// Creates a StatusNotifier tray icon using the application's D-Bus connection.
    /// </summary>
    /// <param name="application">The Gio.Application to get the D-Bus connection from.</param>
    public StatusNotifierTray(Gio.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        // Pin ourselves to prevent GC during D-Bus callbacks
        _selfHandle = GCHandle.Alloc(this);

        // Get the D-Bus connection from the application - this is the same connection GTK uses
        // GirCore wraps GObject handles, we need the raw pointer
        var appHandle = application.Handle.DangerousGetHandle();
        _connection = g_application_get_dbus_connection(appHandle);
        if (_connection == nint.Zero)
        {
            throw new InvalidOperationException("Application does not have a D-Bus connection. Is it running?");
        }

        // Keep delegates alive (prevent GC)
        _methodCallFunc = OnMethodCall;
        _getPropertyFunc = OnGetProperty;

        // Parse introspection XML
        nint error;
        var xml = BuildIntrospectionXml();
        _nodeInfo = g_dbus_node_info_new_for_xml(xml, out error);
        if (_nodeInfo == nint.Zero || error != nint.Zero)
        {
            FreeErrorAndThrow(error, "Failed to parse D-Bus introspection");
        }

        var interfaceInfo = g_dbus_node_info_lookup_interface(_nodeInfo, ItemInterface);
        if (interfaceInfo == nint.Zero)
            throw new InvalidOperationException($"Interface {ItemInterface} not found");

        // Create vtable with method/property handlers
        var vtable = new GDBusInterfaceVTable
        {
            method_call = Marshal.GetFunctionPointerForDelegate(_methodCallFunc),
            get_property = Marshal.GetFunctionPointerForDelegate(_getPropertyFunc),
            set_property = nint.Zero
        };

        _vtablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<GDBusInterfaceVTable>());
        Marshal.StructureToPtr(vtable, _vtablePtr, false);

        // Register the D-Bus object
        _registrationId = g_dbus_connection_register_object(
            _connection,
            ObjectPath,
            interfaceInfo,
            _vtablePtr,
            nint.Zero,
            nint.Zero,
            out error);

        if (_registrationId == 0 || error != nint.Zero)
        {
            Marshal.FreeHGlobal(_vtablePtr);
            FreeErrorAndThrow(error, "Failed to register D-Bus object");
        }

        // Register with StatusNotifierWatcher
        RegisterWithWatcher();
    }

    private static void FreeErrorAndThrow(nint error, string message)
    {
        string? errorMsg = null;
        if (error != nint.Zero)
        {
            errorMsg = GetGErrorMessage(error);
            g_error_free(error);
        }
        throw new InvalidOperationException(errorMsg ?? message);
    }

    private static string? GetGErrorMessage(nint error)
    {
        if (error == nint.Zero) return null;
        // GError struct: domain (uint32), code (int), message (char*)
        var messagePtr = Marshal.ReadIntPtr(error, 8); // offset 8 for message pointer on 64-bit
        return Marshal.PtrToStringUTF8(messagePtr);
    }

    private void RegisterWithWatcher()
    {
        // Get our unique connection name (e.g. ":1.123")
        var uniqueNamePtr = g_dbus_connection_get_unique_name(_connection);
        var uniqueName = Marshal.PtrToStringUTF8(uniqueNamePtr) ?? "";

        // StatusNotifierWatcher expects "busname/objectpath" format
        var serviceId = $"{uniqueName}{ObjectPath}";

        var parameters = g_variant_new_parsed($"('{serviceId}',)");

        g_dbus_connection_call(
            _connection,
            WatcherService,
            WatcherPath,
            WatcherInterface,
            "RegisterStatusNotifierItem",
            parameters,
            nint.Zero, // No reply type
            0, // DBusCallFlags.None
            -1, // Default timeout
            nint.Zero, // No cancellable
            nint.Zero, // No callback
            nint.Zero); // No user data
    }

    private static string BuildIntrospectionXml() => """
        <node>
          <interface name="org.kde.StatusNotifierItem">
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="Menu" type="o" access="read"/>
            <method name="Activate">
              <arg name="x" type="i" direction="in"/>
              <arg name="y" type="i" direction="in"/>
            </method>
            <method name="ContextMenu">
              <arg name="x" type="i" direction="in"/>
              <arg name="y" type="i" direction="in"/>
            </method>
            <method name="SecondaryActivate">
              <arg name="x" type="i" direction="in"/>
              <arg name="y" type="i" direction="in"/>
            </method>
            <method name="Scroll">
              <arg name="delta" type="i" direction="in"/>
              <arg name="orientation" type="s" direction="in"/>
            </method>
            <signal name="NewIcon"/>
            <signal name="NewTitle"/>
            <signal name="NewStatus">
              <arg name="status" type="s"/>
            </signal>
          </interface>
        </node>
        """;

    private void OnMethodCall(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint methodName,
        nint parameters,
        nint invocation,
        nint userData)
    {
        var method = Marshal.PtrToStringUTF8(methodName);

        switch (method)
        {
            case "Activate":
                GLib.Functions.IdleAdd(0, () => { Activated?.Invoke(); return false; });
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;

            case "SecondaryActivate":
                GLib.Functions.IdleAdd(0, () => { SecondaryActivated?.Invoke(); return false; });
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;

            case "ContextMenu":
            case "Scroll":
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;

            default:
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;
        }
    }

    private nint OnGetProperty(
        nint connection,
        nint sender,
        nint objectPath,
        nint interfaceName,
        nint propertyName,
        nint errorPtr, // GError** - we'd write *errorPtr = NULL on success, but we never fail
        nint userData)
    {
        // Set *error to NULL to indicate no error
        if (errorPtr != nint.Zero)
            Marshal.WriteIntPtr(errorPtr, nint.Zero);

        if (propertyName == nint.Zero)
            return nint.Zero;

        var prop = Marshal.PtrToStringUTF8(propertyName);

        return prop switch
        {
            "Category" => g_variant_new_string("ApplicationStatus"),
            "Id" => g_variant_new_string("gifmaker"),
            "Title" => g_variant_new_string(_title),
            "Status" => g_variant_new_string("Active"),
            "IconName" => g_variant_new_string(_iconName),
            "IconThemePath" => g_variant_new_string(""),
            "ItemIsMenu" => g_variant_new_boolean(false),
            "Menu" => g_variant_new_object_path("/NO_DBUSMENU"),
            "ToolTip" => BuildTooltipVariant(),
            _ => nint.Zero
        };
    }

    private nint BuildTooltipVariant()
    {
        // (sa(iiay)ss) - icon name, icon data array, title, description
        var icon = g_variant_new_string(_iconName);
        var emptyArray = g_variant_new_array(g_variant_type_new("(iiay)"), [], 0);
        var title = g_variant_new_string(_title);
        var desc = g_variant_new_string(_tooltip);

        var children = new[] { icon, emptyArray, title, desc };
        return g_variant_new_tuple(children, (nuint)children.Length);
    }

    /// <summary>Sets the icon.</summary>
    public void SetIcon(string iconName)
    {
        if (_disposed != 0) return;
        _iconName = iconName;
        EmitSignal("NewIcon");
    }

    /// <summary>Sets recording state icon.</summary>
    public void SetRecording(bool recording)
    {
        SetIcon(recording ? "media-record" : "camera-video");
    }

    private void EmitSignal(string signalName)
    {
        if (_disposed != 0) return;

        g_dbus_connection_emit_signal(
            _connection,
            nint.Zero, // No destination
            ObjectPath,
            ItemInterface,
            signalName,
            nint.Zero, // No parameters
            out _);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_registrationId != 0)
            g_dbus_connection_unregister_object(_connection, _registrationId);

        if (_vtablePtr != nint.Zero)
            Marshal.FreeHGlobal(_vtablePtr);

        if (_nodeInfo != nint.Zero)
            g_dbus_node_info_unref(_nodeInfo);

        if (_selfHandle.IsAllocated)
            _selfHandle.Free();

        // Connection is shared, don't close it
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct GDBusInterfaceVTable
    {
        public nint method_call;
        public nint get_property;
        public nint set_property;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GDBusMethodCallFunc(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint methodName, nint parameters, nint invocation, nint userData);

    // Note: error is GError** - a pointer to a pointer. We receive it as nint (the pointer)
    // and should write nint.Zero to *error if no error. But since we never error, we can ignore it.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GDBusGetPropertyFunc(
        nint connection, nint sender, nint objectPath, nint interfaceName,
        nint propertyName, nint errorPtr, nint userData);

    [LibraryImport(LibGio)]
    private static partial nint g_application_get_dbus_connection(nint application);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_dbus_node_info_new_for_xml(string xml, out nint error);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_dbus_node_info_lookup_interface(nint info, string name);

    [LibraryImport(LibGio)]
    private static partial void g_dbus_node_info_unref(nint info);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint g_dbus_connection_register_object(
        nint connection, string objectPath, nint interfaceInfo,
        nint vtable, nint userData, nint userDataFreeFunc, out nint error);

    [LibraryImport(LibGio)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool g_dbus_connection_unregister_object(nint connection, uint registrationId);

    [LibraryImport(LibGio)]
    private static partial nint g_dbus_connection_get_unique_name(nint connection);

    [LibraryImport(LibGio)]
    private static partial void g_dbus_method_invocation_return_value(nint invocation, nint parameters);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void g_dbus_connection_call(
        nint connection, string busName, string objectPath, string interfaceName,
        string methodName, nint parameters, nint replyType, int flags,
        int timeoutMsec, nint cancellable, nint callback, nint userData);

    [LibraryImport(LibGio, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool g_dbus_connection_emit_signal(
        nint connection, nint destinationBusName, string objectPath,
        string interfaceName, string signalName, nint parameters, out nint error);

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_variant_new_string(string value);

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_variant_new_object_path(string value);

    [LibraryImport(LibGLib)]
    private static partial nint g_variant_new_boolean([MarshalAs(UnmanagedType.Bool)] bool value);

    [LibraryImport(LibGLib)]
    private static partial nint g_variant_new_tuple(nint[] children, nuint nChildren);

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_variant_type_new(string typeString);

    [LibraryImport(LibGLib)]
    private static partial nint g_variant_new_array(nint childType, nint[] children, nuint nChildren);

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint g_variant_new_parsed(string format);

    [LibraryImport(LibGLib)]
    private static partial void g_error_free(nint error);

    #endregion
}
