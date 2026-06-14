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
    private const int MaxProviders = 3;

    private const string UriListMime = "text/uri-list";
    private const string GnomeCopiedFilesMime = "x-special/gnome-copied-files";
    private const string TextPlainMime = "text/plain";

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

    public Result<SavedMedia> CopyFileToClipboard(Gtk.Widget owner, SavedMedia media)
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

        var providers = new ContentProvider[MaxProviders];
        var providerCount = 0;

        try
        {
            var uriListProvider = CreateUriListProvider(uri);
            providers[providerCount++] = uriListProvider;

            var gnomeProvider = CreateGnomeCopiedFilesProvider(uri);
            providers[providerCount++] = gnomeProvider;

            var textProvider = CreateTextPlainProvider(media.Path);
            providers[providerCount++] = textProvider;

            var transferredProviderHandles = new nint[providerCount];
            var transferredProviderCount = 0;
            var unionHandle = nint.Zero;

            try
            {
                // GTK's union constructor consumes provider references; duplicate the GirCore-owned refs before transfer.
                for (var i = 0; i < providerCount; i++)
                {
                    var handle = providers[i].Handle.DangerousGetHandle();
                    transferredProviderHandles[i] = GdkClipboardNative.GObjectRef(handle);
                    transferredProviderCount++;
                }

                unionHandle = GdkClipboardNative.GdkContentProviderNewUnion(
                    transferredProviderHandles,
                    (nuint)providerCount);

                if (unionHandle == 0)
                    return Result<SavedMedia>.Fail("Failed to create content provider union");

                transferredProviderCount = 0;

                var success = GdkClipboardNative.GdkClipboardSetContent(
                    clipboard.Handle.DangerousGetHandle(),
                    unionHandle);

                return success
                    ? Result<SavedMedia>.Ok(media)
                    : Result<SavedMedia>.Fail("Failed to set clipboard content");
            }
            finally
            {
                for (var i = 0; i < transferredProviderCount; i++)
                    GdkClipboardNative.GObjectUnref(transferredProviderHandles[i]);

                if (unionHandle != 0)
                    GdkClipboardNative.GObjectUnref(unionHandle);

                GC.KeepAlive(providers);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Clipboard file copy failed for {FilePath}", media.Path);
            return Result<SavedMedia>.Fail(ex.Message);
        }
    }

    private static ContentProvider CreateUriListProvider(string uri) =>
        ContentProvider.NewForBytes(UriListMime, Bytes.New(Encoding.UTF8.GetBytes(uri + "\r\n")));

    private static ContentProvider CreateGnomeCopiedFilesProvider(string uri) =>
        ContentProvider.NewForBytes(GnomeCopiedFilesMime, Bytes.New(Encoding.UTF8.GetBytes("copy\n" + uri)));

    private static ContentProvider CreateTextPlainProvider(string filePath) =>
        ContentProvider.NewForBytes(TextPlainMime, Bytes.New(Encoding.UTF8.GetBytes(filePath)));
}
