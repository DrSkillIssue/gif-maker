using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using Gdk;
using GifMaker.X11;
using GLib;
using Microsoft.Extensions.Logging;

namespace GifMaker.App;

/// <summary>
/// GTK4-native clipboard service for copying files with maximum app compatibility.
/// </summary>
/// <remarks>
/// <para>
/// Provides multiple clipboard formats simultaneously via <c>gdk_content_provider_new_union</c>
/// so different apps can consume whichever format they prefer:
/// </para>
/// <list type="bullet">
///   <item><b>Raw bytes</b> (image/*) - Discord, Slack, Electron apps</item>
///   <item><b>text/uri-list</b> - File managers, KDE apps (RFC 2483)</item>
///   <item><b>x-special/gnome-copied-files</b> - GNOME apps (Nautilus, etc.)</item>
///   <item><b>text/plain</b> - Universal fallback with file path</item>
/// </list>
/// <para>
/// Works on both X11 and Wayland - GTK4's GdkClipboard abstracts the differences.
/// </para>
/// </remarks>
internal static class ClipboardService
{
    private const int MaxProviders = 4;

    private const string UriListMime = "text/uri-list";
    private const string GnomeCopiedFilesMime = "x-special/gnome-copied-files";
    private const string TextPlainMime = "text/plain";
    private const string DefaultMime = "application/octet-stream";

    private static readonly FrozenDictionary<string, string> ExtensionToMime =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".gif"] = "image/gif",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Result of clipboard copy operation.
    /// </summary>
    public readonly record struct CopyResult(bool Success, string? Error = null)
    {
        public static CopyResult Ok() => new(true);
        public static CopyResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Copy a file to clipboard with multiple formats for broad compatibility.
    /// </summary>
    /// <param name="widget">Widget to get clipboard from.</param>
    /// <param name="filePath">Absolute path to the file to copy.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <returns>Result indicating success or failure with error message.</returns>
    public static CopyResult CopyFileToClipboard(
        Gtk.Widget widget,
        string filePath,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            return CopyResult.Fail("File not found");

        var clipboard = widget.GetClipboard();
        if (clipboard is null)
            return CopyResult.Fail("Failed to get clipboard");

        var file = Gio.FileHelper.NewForPath(filePath);
        var uri = file.GetUri();
        if (string.IsNullOrEmpty(uri))
            return CopyResult.Fail("Failed to get file URI");

        // Stack-allocated provider handle array
        Span<nint> providerHandles = stackalloc nint[MaxProviders];
        var providerCount = 0;

        try
        {
            var mimeType = GetMimeType(filePath);

            // 1. Raw bytes for images (Discord/Slack/Electron apps)
            if (IsImageMime(mimeType))
            {
                var imageProvider = CreateImageProvider(filePath, mimeType, logger);
                if (imageProvider is not null)
                    providerHandles[providerCount++] = imageProvider.Handle.DangerousGetHandle();
            }

            // 2. text/uri-list (RFC 2483)
            var uriListProvider = CreateUriListProvider(uri);
            providerHandles[providerCount++] = uriListProvider.Handle.DangerousGetHandle();

            // 3. x-special/gnome-copied-files
            var gnomeProvider = CreateGnomeCopiedFilesProvider(uri);
            providerHandles[providerCount++] = gnomeProvider.Handle.DangerousGetHandle();

            // 4. text/plain fallback
            var textProvider = CreateTextPlainProvider(filePath);
            providerHandles[providerCount++] = textProvider.Handle.DangerousGetHandle();

            // Create union - must copy to array for P/Invoke
            var handleArray = providerHandles[..providerCount].ToArray();
            var unionHandle = X11Interop.GdkContentProviderNewUnion(handleArray, (nuint)providerCount);

            if (unionHandle == 0)
                return CopyResult.Fail("Failed to create content provider union");

            var success = X11Interop.GdkClipboardSetContent(
                clipboard.Handle.DangerousGetHandle(),
                unionHandle);

            return success ? CopyResult.Ok() : CopyResult.Fail("Failed to set clipboard content");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger?.LogError(ex, "Clipboard copy failed for {FilePath}", filePath);
            return CopyResult.Fail(ex.Message);
        }
    }

    private static ContentProvider? CreateImageProvider(
        string filePath,
        string mimeType,
        ILogger? logger)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var gBytes = Bytes.New(fileBytes);
            return ContentProvider.NewForBytes(mimeType, gBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Failed to read image for clipboard: {Path}", filePath);
            return null;
        }
    }

    private static ContentProvider CreateUriListProvider(string uri) =>
        ContentProvider.NewForBytes(UriListMime, Bytes.New(Encoding.UTF8.GetBytes(uri + "\r\n")));

    private static ContentProvider CreateGnomeCopiedFilesProvider(string uri) =>
        ContentProvider.NewForBytes(GnomeCopiedFilesMime, Bytes.New(Encoding.UTF8.GetBytes("copy\n" + uri)));

    private static ContentProvider CreateTextPlainProvider(string filePath) =>
        ContentProvider.NewForBytes(TextPlainMime, Bytes.New(Encoding.UTF8.GetBytes(filePath)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return ExtensionToMime.GetValueOrDefault(extension, DefaultMime);
    }

    /// <summary>
    /// Checks if MIME type is an image type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsImageMime(string mimeType) =>
        mimeType.AsSpan().StartsWith("image/", StringComparison.Ordinal);
}
