using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GifMaker.App;

[SupportedOSPlatform("linux")]
internal sealed class StatusNotifierTray : IDisposable
{
    private readonly StatusNotifierItemEndpoint _endpoint;
    private readonly Action<AppAction> _dispatch;

    public StatusNotifierTray(Gio.Application application, Action<AppAction> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        _dispatch = dispatch;
        var item = new StatusNotifierItem(
            IconName: "camera-video",
            Title: "GifMaker",
            Tooltip: "GifMaker - Screen Recorder");

        _endpoint = new StatusNotifierItemEndpoint(application, item);
        _endpoint.Activated += OnActivated;
        _endpoint.SecondaryActivated += OnSecondaryActivated;
    }

    public void Dispose()
    {
        _endpoint.Activated -= OnActivated;
        _endpoint.SecondaryActivated -= OnSecondaryActivated;
        _endpoint.Dispose();
    }

    private void OnActivated() => _dispatch(new AppAction.ShowWindow());

    private void OnSecondaryActivated() => _dispatch(new AppAction.CaptureSelection());
}

internal sealed record StatusNotifierItem(string IconName, string Title, string Tooltip);

/// <summary>
/// StatusNotifierItem D-Bus endpoint.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class StatusNotifierItemEndpoint : IDisposable
{
    private const string LibGio = "libgio-2.0.so.0";
    private const string LibGLib = "libglib-2.0.so.0";

    private const string WatcherService = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";
    private const string WatcherInterface = "org.kde.StatusNotifierWatcher";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string ObjectPath = "/StatusNotifierItem";

    private readonly StatusNotifierItem _item;
    private readonly nint _connection;
    private readonly uint _registrationId;
    private readonly nint _nodeInfo;
    private readonly nint _vtablePtr;
    private readonly GCHandle _selfHandle;
    private readonly GDBusMethodCallFunc _methodCallFunc;
    private readonly GDBusGetPropertyFunc _getPropertyFunc;
    private int _disposed;

    public StatusNotifierItemEndpoint(
        Gio.Application application,
        StatusNotifierItem item)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(item);

        _item = item;
        _methodCallFunc = OnMethodCall;
        _getPropertyFunc = OnGetProperty;

        var selfHandle = default(GCHandle);
        var connection = nint.Zero;
        var nodeInfo = nint.Zero;
        var vtablePtr = nint.Zero;
        uint registrationId = 0;

        void ReleaseAllocated()
        {
            if (registrationId != 0 && connection != nint.Zero)
                g_dbus_connection_unregister_object(connection, registrationId);

            if (vtablePtr != nint.Zero)
                Marshal.FreeHGlobal(vtablePtr);

            if (nodeInfo != nint.Zero)
                g_dbus_node_info_unref(nodeInfo);

            if (selfHandle.IsAllocated)
                selfHandle.Free();
        }

        try
        {
            selfHandle = GCHandle.Alloc(this);

            var appHandle = application.Handle.DangerousGetHandle();
            connection = g_application_get_dbus_connection(appHandle);
            if (connection == nint.Zero)
                throw new InvalidOperationException("Application does not have a D-Bus connection. Is it running?");

            nint error;
            var xml = BuildIntrospectionXml();
            nodeInfo = g_dbus_node_info_new_for_xml(xml, out error);
            if (nodeInfo == nint.Zero || error != nint.Zero)
                FreeErrorAndThrow(error, "Failed to parse D-Bus introspection");

            var interfaceInfo = g_dbus_node_info_lookup_interface(nodeInfo, ItemInterface);
            if (interfaceInfo == nint.Zero)
                throw new InvalidOperationException($"Interface {ItemInterface} not found");

            var vtable = new GDBusInterfaceVTable
            {
                method_call = Marshal.GetFunctionPointerForDelegate(_methodCallFunc),
                get_property = Marshal.GetFunctionPointerForDelegate(_getPropertyFunc),
                set_property = nint.Zero
            };

            vtablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<GDBusInterfaceVTable>());
            Marshal.StructureToPtr(vtable, vtablePtr, false);

            registrationId = g_dbus_connection_register_object(
                connection,
                ObjectPath,
                interfaceInfo,
                vtablePtr,
                nint.Zero,
                nint.Zero,
                out error);

            if (registrationId == 0 || error != nint.Zero)
                FreeErrorAndThrow(error, "Failed to register D-Bus object");

            _connection = connection;
            _nodeInfo = nodeInfo;
            _vtablePtr = vtablePtr;
            _registrationId = registrationId;
            _selfHandle = selfHandle;

            RegisterWithWatcher();
        }
        catch
        {
            ReleaseAllocated();
            throw;
        }
    }

    public event Action? Activated;

    public event Action? SecondaryActivated;

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
        var messagePtr = Marshal.ReadIntPtr(error, 8);
        return Marshal.PtrToStringUTF8(messagePtr);
    }

    private void RegisterWithWatcher()
    {
        var uniqueNamePtr = g_dbus_connection_get_unique_name(_connection);
        var uniqueName = Marshal.PtrToStringUTF8(uniqueNamePtr) ?? "";
        var serviceId = $"{uniqueName}{ObjectPath}";
        var parameters = g_variant_new_parsed($"('{serviceId}',)");

        g_dbus_connection_call(
            _connection,
            WatcherService,
            WatcherPath,
            WatcherInterface,
            "RegisterStatusNotifierItem",
            parameters,
            nint.Zero,
            0,
            -1,
            nint.Zero,
            nint.Zero,
            nint.Zero);
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
                RaiseActivated();
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;

            case "SecondaryActivate":
                RaiseSecondaryActivated();
                g_dbus_method_invocation_return_value(invocation, nint.Zero);
                break;

            case "ContextMenu":
            case "Scroll":
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
        nint errorPtr,
        nint userData)
    {
        if (errorPtr != nint.Zero)
            Marshal.WriteIntPtr(errorPtr, nint.Zero);

        if (propertyName == nint.Zero)
            return nint.Zero;

        var prop = Marshal.PtrToStringUTF8(propertyName);

        return prop switch
        {
            "Category" => g_variant_new_string("ApplicationStatus"),
            "Id" => g_variant_new_string("gifmaker"),
            "Title" => g_variant_new_string(_item.Title),
            "Status" => g_variant_new_string("Active"),
            "IconName" => g_variant_new_string(_item.IconName),
            "IconThemePath" => g_variant_new_string(""),
            "ItemIsMenu" => g_variant_new_boolean(false),
            "Menu" => g_variant_new_object_path("/NO_DBUSMENU"),
            "ToolTip" => BuildTooltipVariant(),
            _ => nint.Zero
        };
    }

    private nint BuildTooltipVariant()
    {
        var icon = g_variant_new_string(_item.IconName);
        var emptyArray = g_variant_new_array(g_variant_type_new("(iiay)"), [], 0);
        var title = g_variant_new_string(_item.Title);
        var desc = g_variant_new_string(_item.Tooltip);

        var children = new[] { icon, emptyArray, title, desc };
        return g_variant_new_tuple(children, (nuint)children.Length);
    }

    private void RaiseActivated()
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            Activated?.Invoke();
            return false;
        });
    }

    private void RaiseSecondaryActivated()
    {
        GLib.Functions.IdleAdd(0, () =>
        {
            SecondaryActivated?.Invoke();
            return false;
        });
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
    }

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
}
