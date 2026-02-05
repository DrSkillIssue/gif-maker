# AGENTS.md - GifMaker

Linux GTK4 screen recording application (.NET 10, C# 14).

## Build & Run

```bash
dotnet build                    # Debug build
dotnet build -c Release         # Release build
dotnet run                      # Run debug
dotnet publish -c Release       # Publish self-contained
```

## Tests

No test infrastructure currently. If added:

```bash
dotnet test                                    # Run all
dotnet test --filter "FullyQualifiedName~Foo" # Single test by name
dotnet test --filter "ClassName=FooTests"     # Single class
```

## Linting & Formatting

```bash
dotnet format                          # Format code
dotnet format --verify-no-changes      # Check only
```

No `.editorconfig` yet. Nullable reference types enforced via csproj.

## Project Structure

```
src/
  App/        # GTK application, UI windows
  Cli/        # Command-line interface
  Core/       # Domain primitives: Result<T>, Rectangle, interfaces
  Recording/  # FFmpeg x11grab recording
  Conversion/ # FFmpeg format conversion (GIF/MP4/WebM)
  Screenshot/ # FFmpeg screenshot capture
  X11/        # P/Invoke, native interop
```

## Code Style

### Naming

- **Namespaces**: PascalCase, match folder (`GifMaker.Core`)
- **Types**: PascalCase (`FFmpegRecorder`, `RecordWindow`)
- **Interfaces**: `I` prefix (`IFFmpegProcess`, `IProcessRunner`)
- **Private fields**: underscore prefix (`_display`, `_logger`)
- **Constants**: PascalCase (`DefaultStopTimeout`, `MaxFps`)
- **Methods**: PascalCase (`RunAsync`, `CreateOrThrow`)
- **Parameters**: camelCase (`sourcePath`, `ct` for CancellationToken)

### Imports

```csharp
// System namespaces implicit via ImplicitUsings
using Gtk;
using GifMaker.Core;
using GifMaker.Recording;
using Microsoft.Extensions.Logging;
using static GifMaker.X11.X11Interop;  // For P/Invoke
```

Order: Framework, external packages, project namespaces, static imports last.

### Type Safety (CRITICAL)

- **No `any` equivalent**: Always use concrete types
- **No non-null assertion**: Use `[MemberNotNullWhen]` attributes
- **No unsafe type assertions**: Pattern match instead
- **Nullable enabled**: All reference types explicitly nullable or non-null

### Make Illegal States Unrepresentable

Use discriminated unions (sealed record hierarchies):

```csharp
private abstract record WindowState
{
    private WindowState() { }  // Sealed hierarchy
    public sealed record Ready : WindowState;
    public sealed record Recording(FFmpegRecorder Recorder, int Fps) : WindowState;
    public sealed record Error(string Message) : WindowState;
}
```

### Error Handling

Use `Result<T>` for fallible operations instead of exceptions:

```csharp
public static Result<T> Create(...)
{
    if (!valid)
        return Result<T>.Fail("Error message");
    return Result<T>.Ok(value);
}

// Consumer
result.Match(
    onSuccess: value => ...,
    onError: error => ...
);
```

Reserve exceptions for truly exceptional cases. Custom exceptions include context:

```csharp
public class FFmpegException(string message, int exitCode, string stderr)
    : Exception(message) { ... }
```

### Disposal

Thread-safe disposal with `Interlocked`:

```csharp
private int _disposed;

public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) != 0)
        return;
    // cleanup
}

private void ThrowIfDisposed() => 
    ObjectDisposedException.ThrowIf(_disposed != 0, this);
```

### Zero-Allocation Patterns

For hot paths, minimize allocations:

```csharp
// Stack-allocated formatting
Span<char> buffer = stackalloc char[32];
value.TryFormat(buffer, out var len);

// string.Create for formatted output
return string.Create(length, state, static (span, s) => { ... });

// Object pools for reusable buffers
var builder = StringBuilderPool.Shared.Get();
try { ... }
finally { StringBuilderPool.Shared.Return(builder); }

// ArrayPool for temp buffers
var buffer = ArrayPool<char>.Shared.Rent(4096);
try { ... }
finally { ArrayPool<char>.Shared.Return(buffer); }
```

### Async

- Suffix async methods with `Async`
- Use `ConfigureAwait(false)` in library code
- Accept `CancellationToken ct = default` parameter
- Kill child processes on cancellation

### Documentation

XML doc comments on public APIs:

```csharp
/// <summary>
/// Brief description.
/// </summary>
/// <param name="region">Screen region to capture.</param>
/// <returns>Recording statistics.</returns>
/// <exception cref="InvalidOperationException">When...</exception>
```

### P/Invoke

Centralize in `*Interop.cs` files. Use source generators:

```csharp
[LibraryImport("libX11.so.6")]
internal static partial nint XOpenDisplay(nint display);
```

### Testability

Inject dependencies via interfaces:

```csharp
public FFmpegRecorder(
    ILogger<FFmpegRecorder>? logger = null,
    IFFmpegRecordingProcessFactory? processFactory = null)
{
    _logger = logger ?? NullLogger<FFmpegRecorder>.Instance;
    _processFactory = processFactory ?? FFmpegRecordingProcessFactory.Default;
}
```

## Dependencies

- **Runtime**: .NET 10, GTK4, X11
- **External tools**: `ffmpeg`, `slop` (area selection)
- **NuGet**: GirCore.Gtk-4.0, Microsoft.Extensions.Logging, Microsoft.Extensions.ObjectPool

## Platform

Linux-only. Mark with `[SupportedOSPlatform("linux")]` where appropriate.
X11 required for full functionality; graceful degradation on Wayland.
