namespace ProjectPublisher.Models;

public sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string CommandDisplay);

public sealed class GitCliPublishOptions
{
    public required string Owner { get; init; }
    public required string RepositoryName { get; init; }
    public required string Description { get; init; }
    public required string CommitMessage { get; init; }
    public required string BranchName { get; init; }
    public required bool IsPrivate { get; init; }
    public required bool GenerateReadmeWhenMissing { get; init; }
    public required bool ReplaceOrigin { get; init; }
    public string? ScreenshotPath { get; init; }
}

public sealed class GitCommandException : Exception
{
    public string Command { get; }
    public int ExitCode { get; }

    public GitCommandException(string command, int exitCode, string details)
        : base($"Git command failed with exit code {exitCode}.\n\n{command}\n\n{details}".Trim())
    {
        Command = command;
        ExitCode = exitCode;
    }
}

public sealed class GitNotInstalledException : Exception
{
    public GitNotInstalledException(string message, Exception innerException) : base(message, innerException) { }
}
