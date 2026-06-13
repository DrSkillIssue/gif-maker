using System.Runtime.InteropServices;

namespace GifMaker.App;

internal static partial class GdkClipboardNative
{
    private const string LibGtkGdk = "libgtk-4.so.1";
    private const string LibGObject = "libgobject-2.0.so.0";

    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_content_provider_new_union")]
    internal static partial nint GdkContentProviderNewUnion(nint[] providers, nuint providerCount);

    [LibraryImport(LibGtkGdk, EntryPoint = "gdk_clipboard_set_content")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdkClipboardSetContent(nint clipboard, nint provider);

    [LibraryImport(LibGObject, EntryPoint = "g_object_ref")]
    internal static partial nint GObjectRef(nint instance);

    [LibraryImport(LibGObject, EntryPoint = "g_object_unref")]
    internal static partial void GObjectUnref(nint instance);
}
