using System.Globalization;
using System.Text.RegularExpressions;
using GifMaker.Core;

namespace GifMaker.X11;

/// <summary>
/// Allows user to select a rectangular screen region using slop.
/// Works correctly with compositors (Mutter, KWin, etc.).
/// </summary>
public sealed partial class AreaSelector : IDisposable
{
    private readonly IProcessRunner _processRunner;
    private int _disposed;
    
    [GeneratedRegex(@"(\d+)x(\d+)\+(\d+)\+(\d+)")]
    private static partial Regex GeometryRegex();
    
    /// <summary>
    /// Creates an AreaSelector with default process runner.
    /// </summary>
    public AreaSelector() : this(ProcessRunner.Default) { }
    
    /// <summary>
    /// Creates an AreaSelector with custom process runner (for testing).
    /// </summary>
    public AreaSelector(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }
    
    /// <summary>
    /// Blocks until user selects a region or cancels.
    /// </summary>
    /// <returns>Result containing selected region on success, or error on failure.</returns>
    public async Task<Result<Rectangle>> SelectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        
        // -f: format, -b: border width, -c: color (RGBA), -l: classic XP style
        string[] args = ["-f", "%wx%h+%x+%y", "-b", "4", "-c", "1,0.2,0.2,0.8", "-l"];
        
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync("slop", args, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<Rectangle>.Fail($"Failed to start slop: {ex.Message}");
        }
        
        // Exit code 1 = cancelled (Escape pressed)
        if (result.ExitCode == 1)
            return Result<Rectangle>.Fail("Selection cancelled");
        
        if (result.ExitCode != 0)
            return Result<Rectangle>.Fail($"slop failed with exit code {result.ExitCode}: {result.StandardError}");
        
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            return Result<Rectangle>.Fail("slop produced no output");
        
        // Parse output: WxH+X+Y (e.g., "640x480+100+200")
        var match = GeometryRegex().Match(result.StandardOutput);
        if (!match.Success)
            return Result<Rectangle>.Fail($"Invalid slop output format: {result.StandardOutput}");
        
        var width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var x = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var y = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        
        var rect = new Rectangle(x, y, width, height);
        return rect.IsValid 
            ? Result<Rectangle>.Ok(rect) 
            : Result<Rectangle>.Fail("Selected region has zero area");
    }
    
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
    
    /// <summary>
    /// Disposes the area selector.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        // No unmanaged resources - Process is handled by IProcessRunner
    }
}
