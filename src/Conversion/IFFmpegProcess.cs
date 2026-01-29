namespace GifMaker.Conversion;

/// <summary>
/// Abstraction over FFmpeg conversion process for testability.
/// Wraps process lifecycle: start, stderr reading, exit handling, and termination.
/// </summary>
/// <remarks>
/// Unlike <c>IFFmpegRecordingProcess</c>, this interface does not expose stdin
/// since conversion processes run to completion without interactive commands.
/// </remarks>
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
/// Factory for creating FFmpeg conversion processes.
/// Enables dependency injection and test mocking.
/// </summary>
public interface IFFmpegProcessFactory
{
    /// <summary>Creates a new FFmpeg process with the specified arguments.</summary>
    /// <param name="args">Command-line arguments for FFmpeg.</param>
    /// <returns>A new process instance ready to start.</returns>
    IFFmpegProcess Create(ReadOnlySpan<string> args);
}
