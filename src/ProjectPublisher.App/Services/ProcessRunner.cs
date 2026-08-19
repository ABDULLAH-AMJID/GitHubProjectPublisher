using System.Diagnostics;

namespace ProjectPublisher.Services;

public sealed class ProcessRunner
{
    public async Task RunToCompletionAsync(
        string command,
        string workingDirectory,
        IProgress<string>? progress,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = CreateCommandProcess(command, workingDirectory, environment);
        AttachOutput(process, progress);
        if (!process.Start()) throw new InvalidOperationException($"Could not start: {command}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {command}");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    public Process StartBackground(
        string command,
        string workingDirectory,
        IProgress<string>? progress,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var process = CreateCommandProcess(command, workingDirectory, environment);
        AttachOutput(process, progress);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Could not start: {command}");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static void TryKill(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static Process CreateCommandProcess(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        if (environment is not null)
            foreach (var item in environment) startInfo.Environment[item.Key] = item.Value;
        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private static void AttachOutput(Process process, IProgress<string>? progress)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) progress?.Report(e.Data);
        };
    }
}
