using System.Diagnostics;

namespace GifMaker.Core;

/// <summary>
/// Result of a process execution.
/// </summary>
public readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Whether the process exited successfully (exit code 0).</summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Abstraction over process execution for testability.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a process and waits for completion.
    /// </summary>
    /// <param name="fileName">Path to the executable.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing exit code and captured output.</returns>
    Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default);
    
    /// <summary>
    /// Starts a process without waiting for completion (fire and forget).
    /// </summary>
    /// <param name="fileName">Path to the executable.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <returns>True if process started successfully.</returns>
    bool StartDetached(string fileName, IEnumerable<string> arguments);
}

/// <summary>
/// Default implementation using System.Diagnostics.Process.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Singleton instance.</summary>
    public static ProcessRunner Default { get; } = new();
    
    private ProcessRunner() { }
    
    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        
        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        
        process.Start();
        
        // Read both streams concurrently to avoid deadlock.
        // Must await reads alongside WaitForExitAsync to prevent buffer deadlock
        // when process output exceeds OS pipe buffer size.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        
        try
        {
            // Wait for all three to complete: exit + both stream reads
            await Task.WhenAll(
                process.WaitForExitAsync(ct),
                stdoutTask,
                stderrTask
            ).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Kill process on cancellation to avoid orphans
            TryKillProcess(process);
            throw;
        }
        
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        
        return new ProcessResult(process.ExitCode, stdout.Trim(), stderr.Trim());
    }
    
    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
    }
    
    /// <inheritdoc />
    public bool StartDetached(string fileName, IEnumerable<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                // Don't redirect streams for detached processes - we won't read them
                // and redirecting without reading can cause the process to block
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            
            foreach (var arg in arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
            
            // Don't dispose - we want the process to continue running
            var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
