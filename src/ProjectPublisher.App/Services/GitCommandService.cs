using System.Diagnostics;
using System.Text;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class GitCommandService
{
    private readonly SecretScanner _redactor;

    public GitCommandService(SecretScanner redactor) => _redactor = redactor;

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

        var safeCommand = "git " + string.Join(" ", arguments.Select(QuoteForDisplay));
        progress?.Report($"❯ {safeCommand}");
        var output = new StringBuilder();
        var error = new StringBuilder();
        var started = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Capture(e.Data, output, progress);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, error, progress);

        try
        {
            if (!process.Start()) throw new InvalidOperationException("Git process could not be started.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { /* Process already ended. */ }
            });
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(process.WaitForExitAsync(), Task.Delay(30, CancellationToken.None));
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new GitNotInstalledException(
                "Git for Windows was not found. Install it from https://git-scm.com/download/win, then restart Project Publisher.", ex);
        }

        started.Stop();
        var result = new GitCommandResult(
            process.ExitCode,
            output.ToString().TrimEnd(),
            error.ToString().TrimEnd(),
            started.Elapsed,
            safeCommand);
        if (throwOnError && result.ExitCode != 0)
            throw CreateException(result);
        return result;
    }

    public Task<GitCommandResult> CheckVersionAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        RunAsync(Environment.CurrentDirectory, ["--version"], progress, cancellationToken);

    public IReadOnlyDictionary<string, string> CreateGitHubAuthEnvironment(string login, string token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login}:{token}"));
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basic}"
        };
    }

    private void Capture(string? line, StringBuilder target, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var safe = _redactor.RedactContent(line).Content;
        target.AppendLine(safe);
        progress?.Report($"  {safe}");
    }

    private static string QuoteForDisplay(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        return argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;
    }

    private static GitCommandException CreateException(GitCommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        if (details.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("fetch first", StringComparison.OrdinalIgnoreCase))
        {
            details += "\nThe remote contains commits that are not in this folder. Use Fetch/Pull and resolve conflicts; force-push is intentionally disabled.";
        }
        if (details.Contains("without `workflow` scope", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("without 'workflow' scope", StringComparison.OrdinalIgnoreCase))
        {
            details += "\nThis repository contains a GitHub Actions workflow. Install the updated app, reconnect GitHub, and approve the requested workflow scope. Your local commit is safe; retry Push afterward.";
        }
        else if (details.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                 details.Contains("403", StringComparison.OrdinalIgnoreCase))
        {
            details += "\nReconnect GitHub and verify repository/organization permissions.";
        }
        return new GitCommandException(result.CommandDisplay, result.ExitCode, details);
    }
}
