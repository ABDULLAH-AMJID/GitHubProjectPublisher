using System.Text.Json.Serialization;

namespace ProjectPublisher.Models;

public sealed class AppSettings
{
    public string GitHubClientId { get; set; } = string.Empty;
    public string LastProjectFolder { get; set; } = string.Empty;
    public string LastOwner { get; set; } = string.Empty;
    public string ScreenshotCommand { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public bool GenerateReadme { get; set; } = true;
    public bool ReplaceOrigin { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string LastCommitMessage { get; set; } = "Publish project update";
    public bool OpenRepositoryAfterPublish { get; set; } = true;
}

public enum FindingSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record SecurityFinding(
    string RelativePath,
    int? Line,
    FindingSeverity Severity,
    string Kind,
    string Action,
    string Description);

public sealed class ProjectAnalysis
{
    public required string SourcePath { get; init; }
    public required string ProjectName { get; init; }
    public required string SuggestedRepositoryName { get; init; }
    public required string SuggestedDescription { get; init; }
    public required string ProjectType { get; init; }
    public required IReadOnlyDictionary<string, int> Languages { get; init; }
    public required IReadOnlyList<SecurityFinding> Findings { get; init; }
    public required long TotalBytes { get; init; }
    public required int FileCount { get; init; }
    public required int ExcludedFileCount { get; init; }
    public string? PrimaryProjectFile { get; init; }
    public string? SuggestedRunCommand { get; init; }
    public string? SuggestedPreviewUrl { get; init; }

    public string SizeText => TotalBytes switch
    {
        >= 1_073_741_824 => $"{TotalBytes / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{TotalBytes / 1_048_576d:0.0} MB",
        >= 1024 => $"{TotalBytes / 1024d:0.0} KB",
        _ => $"{TotalBytes} B"
    };
}

public sealed record DeviceCodeResponse(
    [property: JsonPropertyName("device_code")] string DeviceCode,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval);

public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

public sealed class TokenBundle
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? GrantedScopes { get; init; }
}

public sealed class GitHubUser
{
    [JsonPropertyName("login")]
    public required string Login { get; init; }

    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }
}

public sealed class GitHubRepository
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("html_url")]
    public required string HtmlUrl { get; init; }

    [JsonPropertyName("clone_url")]
    public required string CloneUrl { get; init; }

    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; init; }

    [JsonPropertyName("private")]
    public bool IsPrivate { get; init; }
}

public sealed record PublishResult(
    string RepositoryUrl,
    bool RepositoryCreated,
    int RedactedSecrets,
    int ExcludedFiles,
    string CommitSha,
    bool HadChanges);

public sealed class StagingResult
{
    public required string Destination { get; init; }
    public required int CopiedFiles { get; init; }
    public required int ExcludedFiles { get; init; }
    public required int RedactedSecrets { get; init; }
    public required IReadOnlyList<string> EnvironmentExamplesCreated { get; init; }
}
