namespace GifMaker.Recording;

/// <summary>
/// Abstraction over FFmpeg recording process for testability.
/// Wraps process lifecycle: start, I/O streams, exit handling, and termination.
/// </summary>
public interface IFFmpegRecordingProcess : IDisposable
{
    /// <summary>Starts the FFmpeg process.</summary>
    /// <returns><c>true</c> if process started successfully.</returns>
    bool Start();

    /// <summary>Whether the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Process exit code. Only valid after <see cref="HasExited"/> is <c>true</c>.</summary>
    int ExitCode { get; }

    /// <summary>Standard input stream for sending commands (e.g., 'q' to quit).</summary>
    StreamWriter StandardInput { get; }

    /// <summary>Standard error stream for reading FFmpeg output/progress.</summary>
    StreamReader StandardError { get; }

    /// <summary>Waits asynchronously for process exit.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task WaitForExitAsync(CancellationToken ct);

    /// <summary>Terminates the process.</summary>
    /// <param name="entireProcessTree">If <c>true</c>, kills child processes too.</param>
    void Kill(bool entireProcessTree);
}

/// <summary>
/// Factory for creating FFmpeg recording processes.
/// Enables dependency injection and test mocking.
/// </summary>
public interface IFFmpegRecordingProcessFactory
{
    /// <summary>Creates a new FFmpeg process with the specified arguments.</summary>
    /// <param name="args">Command-line arguments for FFmpeg.</param>
    /// <returns>A new process instance ready to start.</returns>
    IFFmpegRecordingProcess Create(ReadOnlySpan<string> args);
}
