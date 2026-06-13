using System.Diagnostics;

namespace GifMaker.Core;

/// <summary>
/// External process stream behavior.
/// </summary>
public enum ProcessIo
{
    Capture,
    CaptureError,
    Detached,
    Interactive
}

/// <summary>
/// Complete command line for an external process.
/// </summary>
public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments, ProcessIo Io);

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
    Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken ct = default);

    bool StartDetached(ProcessCommand command);
}

/// <summary>
/// Starts long-running processes whose lifecycle is owned by the caller.
/// </summary>
public interface IProcessLauncher
{
    IInteractiveProcess StartInteractive(ProcessCommand command);
}

public interface IRunningProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    StreamReader StandardError { get; }
    Task WaitForExitAsync(CancellationToken ct);
    void Kill(bool entireProcessTree);
}

public interface IInteractiveProcess : IRunningProcess
{
    StreamWriter StandardInput { get; }
}

public static class RunningProcessExtensions
{
    public static async Task KillSafelyAsync(this IRunningProcess process, TimeSpan? timeout = null)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(timeout ?? TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (TimeoutException)
        {
            // Kill timeout.
        }
        catch (SystemException)
        {
            // Process handle errors are non-actionable during cleanup.
        }
    }
}

/// <summary>
/// Default implementation using System.Diagnostics.Process.
/// </summary>
public sealed class ProcessRunner : IProcessRunner, IProcessLauncher
{
    /// <summary>Singleton instance.</summary>
    public static ProcessRunner Default { get; } = new();

    private ProcessRunner() { }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, forDetachedStart: false)
        };

        process.Start();

        var stdoutTask = process.StartInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(ct)
            : Task.FromResult(string.Empty);
        var stderrTask = process.StartInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(ct)
            : Task.FromResult(string.Empty);

        try
        {
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
    public bool StartDetached(ProcessCommand command)
    {
        try
        {
            var process = Process.Start(CreateStartInfo(command, forDetachedStart: true));
            process?.Dispose();
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public IInteractiveProcess StartInteractive(ProcessCommand command)
    {
        var interactiveCommand = command with { Io = ProcessIo.Interactive };
        var process = new Process { StartInfo = CreateStartInfo(interactiveCommand, forDetachedStart: false) };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start {command.FileName}");
        }

        return new RunningProcess(process);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessCommand command, bool forDetachedStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.FileName);

        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = !forDetachedStart && command.Io == ProcessIo.Interactive,
            RedirectStandardOutput = !forDetachedStart && command.Io == ProcessIo.Capture,
            RedirectStandardError = !forDetachedStart && command.Io is ProcessIo.Capture or ProcessIo.CaptureError or ProcessIo.Interactive,
            CreateNoWindow = true
        };

        foreach (var arg in command.Arguments)
            startInfo.ArgumentList.Add(arg);

        return startInfo;
    }

    private sealed class RunningProcess(Process process) : IInteractiveProcess
    {
        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public StreamReader StandardError => process.StandardError;
        public StreamWriter StandardInput => process.StandardInput;

        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);

        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);

        public void Dispose() => process.Dispose();
    }
}
