namespace GifMaker.Core;

/// <summary>
/// Extension methods for <see cref="IFFmpegProcess"/>.
/// </summary>
public static class FFmpegProcessExtensions
{
    /// <summary>
    /// Kills the process safely, swallowing expected exceptions.
    /// </summary>
    /// <param name="process">Process to kill.</param>
    /// <param name="timeout">Timeout waiting for exit after kill (default 5s).</param>
    public static async Task KillSafelyAsync(this IFFmpegProcess process, TimeSpan? timeout = null)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(timeout ?? TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (TimeoutException)
        {
            // Kill timeout - process is stuck
        }
        catch (SystemException)
        {
            // Various system errors
        }
    }

    /// <summary>
    /// Kills the recording process safely if still running.
    /// </summary>
    /// <param name="process">Process to kill.</param>
    /// <param name="timeout">Timeout waiting for exit after kill (default 2s).</param>
    public static async Task KillSafelyAsync(this IFFmpegRecordingProcess process, TimeSpan? timeout = null)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(timeout ?? TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        catch (TimeoutException)
        {
            // Kill timeout - process is stuck
        }
        catch (SystemException)
        {
            // Various system errors
        }
    }
}

/// <summary>
/// Base abstraction over FFmpeg process for testability.
/// Wraps process lifecycle: start, stderr reading, exit handling, and termination.
/// </summary>
public interface IFFmpegProcess : IDisposable
{
    /// <summary>Starts the FFmpeg process.</summary>
    /// <returns><c>true</c> if process started successfully.</returns>
    bool Start();

    /// <summary>Waits asynchronously for process exit.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task WaitForExitAsync(CancellationToken ct);

    /// <summary>Process exit code. Only valid after process has exited.</summary>
    int ExitCode { get; }

    /// <summary>Standard error stream for reading FFmpeg output/progress.</summary>
    StreamReader StandardError { get; }

    /// <summary>Terminates the process.</summary>
    /// <param name="entireProcessTree">If <c>true</c>, kills child processes too.</param>
    void Kill(bool entireProcessTree);
}

/// <summary>
/// Extended FFmpeg process interface for recording operations.
/// Exposes stdin for interactive commands (e.g., 'q' to quit) and exit status checking.
/// </summary>
public interface IFFmpegRecordingProcess : IFFmpegProcess
{
    /// <summary>Whether the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Standard input stream for sending commands (e.g., 'q' to quit).</summary>
    StreamWriter StandardInput { get; }
}

/// <summary>
/// Factory for creating FFmpeg processes.
/// Enables dependency injection and test mocking.
/// </summary>
/// <typeparam name="TProcess">The process interface type to create.</typeparam>
public interface IFFmpegProcessFactory<out TProcess> where TProcess : IFFmpegProcess
{
    /// <summary>Creates a new FFmpeg process with the specified arguments.</summary>
    /// <param name="args">Command-line arguments for FFmpeg.</param>
    /// <returns>A new process instance ready to start.</returns>
    TProcess Create(ReadOnlySpan<string> args);
}
