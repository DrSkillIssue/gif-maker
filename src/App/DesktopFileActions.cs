using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text;
using Gdk;
using GifMaker.Core;
using GLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GifMaker.App;

/// <summary>
/// Desktop shell operations for files created by the application.
/// </summary>
public sealed class DesktopFileActions
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

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<DesktopFileActions> _logger;

    public DesktopFileActions(
        IProcessRunner? processRunner = null,
        ILogger<DesktopFileActions>? logger = null)
    {
        _processRunner = processRunner ?? ProcessRunner.Default;
        _logger = logger ?? NullLogger<DesktopFileActions>.Instance;
    }

    public Result<SavedMedia> Open(SavedMedia media)
    {
        if (!File.Exists(media.Path))
            return Result<SavedMedia>.Fail("File not found");

        return _processRunner.StartDetached(new ProcessCommand("xdg-open", [media.Path], ProcessIo.Detached))
            ? Result<SavedMedia>.Ok(media)
            : Result<SavedMedia>.Fail("Failed to open file");
    }

    public Result<SavedMedia> OpenContainingFolder(SavedMedia media)
    {
        var folder = Path.GetDirectoryName(media.Path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return Result<SavedMedia>.Fail("Containing folder not found");

        return _processRunner.StartDetached(new ProcessCommand("xdg-open", [folder], ProcessIo.Detached))
            ? Result<SavedMedia>.Ok(media)
            : Result<SavedMedia>.Fail("Failed to open folder");
    }

    public Result<SavedMedia> CopyToClipboard(Gtk.Widget owner, SavedMedia media)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!File.Exists(media.Path))
            return Result<SavedMedia>.Fail("File not found");

        var clipboard = owner.GetClipboard();
        if (clipboard is null)
            return Result<SavedMedia>.Fail("Failed to get clipboard");

        var file = Gio.FileHelper.NewForPath(media.Path);
        var uri = file.GetUri();
        if (string.IsNullOrEmpty(uri))
            return Result<SavedMedia>.Fail("Failed to get file URI");

        Span<nint> providerHandles = stackalloc nint[MaxProviders];
        var providerCount = 0;

        try
        {
            var mimeType = GetMimeType(media.Path);

            if (IsImageMime(mimeType))
            {
                var imageProvider = CreateImageProvider(media.Path, mimeType);
                if (imageProvider is not null)
                    providerHandles[providerCount++] = imageProvider.Handle.DangerousGetHandle();
            }

            var uriListProvider = CreateUriListProvider(uri);
            providerHandles[providerCount++] = uriListProvider.Handle.DangerousGetHandle();

            var gnomeProvider = CreateGnomeCopiedFilesProvider(uri);
            providerHandles[providerCount++] = gnomeProvider.Handle.DangerousGetHandle();

            var textProvider = CreateTextPlainProvider(media.Path);
            providerHandles[providerCount++] = textProvider.Handle.DangerousGetHandle();

            var handleArray = providerHandles[..providerCount].ToArray();
            var unionHandle = GdkClipboardNative.GdkContentProviderNewUnion(handleArray, (nuint)providerCount);

            if (unionHandle == 0)
                return Result<SavedMedia>.Fail("Failed to create content provider union");

            var success = GdkClipboardNative.GdkClipboardSetContent(
                clipboard.Handle.DangerousGetHandle(),
                unionHandle);

            return success
                ? Result<SavedMedia>.Ok(media)
                : Result<SavedMedia>.Fail("Failed to set clipboard content");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Clipboard copy failed for {FilePath}", media.Path);
            return Result<SavedMedia>.Fail(ex.Message);
        }
    }

    private ContentProvider? CreateImageProvider(string filePath, string mimeType)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var gBytes = Bytes.New(fileBytes);
            return ContentProvider.NewForBytes(mimeType, gBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read image for clipboard: {Path}", filePath);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsImageMime(string mimeType) =>
        mimeType.AsSpan().StartsWith("image/", StringComparison.Ordinal);
}
