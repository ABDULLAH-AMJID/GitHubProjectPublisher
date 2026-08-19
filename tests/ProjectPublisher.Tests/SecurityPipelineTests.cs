using ProjectPublisher.Models;
using ProjectPublisher.Services;
using Xunit;

namespace ProjectPublisher.Tests;

public sealed class SecurityPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ProjectPublisherTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnvironmentFile_IsExcluded_AndSafeExampleIsCreated()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        const string original = "PUBLIC_URL=https://example.test\nAPI_KEY=actual-super-secret-value-123\n";
        await File.WriteAllTextAsync(Path.Combine(source, ".env"), original);

        var result = await new StagingService(new SecretScanner()).CopySanitizedAsync(source, staging);

        Assert.False(File.Exists(Path.Combine(staging, ".env")));
        var example = await File.ReadAllTextAsync(Path.Combine(staging, ".env.example"));
        Assert.Contains("PUBLIC_URL=__SET_IN_LOCAL_ENV__", example);
        Assert.Contains("API_KEY=__SET_IN_LOCAL_ENV__", example);
        Assert.DoesNotContain("actual-super-secret", example);
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(source, ".env")));
        Assert.Equal(1, result.ExcludedFiles);
    }

    [Fact]
    public async Task TokenInSource_IsRedactedOnlyInStaging()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var token = "gh" + "p_" + "abcdefghijklmnopqrstuvwxyz1234567890";
        var sourceFile = Path.Combine(source, "settings.json");
        await File.WriteAllTextAsync(sourceFile, $"{{\"access_token\":\"{token}\"}}");

        var result = await new StagingService(new SecretScanner()).CopySanitizedAsync(source, staging);

        var staged = await File.ReadAllTextAsync(Path.Combine(staging, "settings.json"));
        Assert.DoesNotContain(token, staged);
        Assert.Contains("<REDACTED>", staged);
        Assert.Contains(token, await File.ReadAllTextAsync(sourceFile));
        Assert.True(result.RedactedSecrets >= 1);
    }

    [Fact]
    public async Task PrivateKeyFile_IsNeverCopied()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "production.key"), "private-key-material");

        await new StagingService(new SecretScanner()).CopySanitizedAsync(source, staging);

        Assert.False(File.Exists(Path.Combine(staging, "production.key")));
    }

    [Fact]
    public async Task DependencyAndGitFolders_AreNotCopied()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        Directory.CreateDirectory(Path.Combine(source, ".git"));
        Directory.CreateDirectory(Path.Combine(source, "node_modules", "package"));
        Directory.CreateDirectory(Path.Combine(source, "artifacts", "win-x64"));
        await File.WriteAllTextAsync(Path.Combine(source, ".git", "config"), "secret remote");
        await File.WriteAllTextAsync(Path.Combine(source, "node_modules", "package", "index.js"), "generated");
        await File.WriteAllTextAsync(Path.Combine(source, "artifacts", "win-x64", "app.exe"), "generated");
        await File.WriteAllTextAsync(Path.Combine(source, "index.js"), "console.log('safe');");

        await new StagingService(new SecretScanner()).CopySanitizedAsync(source, staging);

        Assert.False(Directory.Exists(Path.Combine(staging, ".git")));
        Assert.False(Directory.Exists(Path.Combine(staging, "node_modules")));
        Assert.False(Directory.Exists(Path.Combine(staging, "artifacts")));
        Assert.True(File.Exists(Path.Combine(staging, "index.js")));
    }

    [Fact]
    public async Task ScanReport_DoesNotContainSecretValue()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var secret = "sk-" + "proj-" + "abcdefghijklmnopqrstuvwxyz123456";
        await File.WriteAllTextAsync(Path.Combine(source, "config.ts"), $"const api_key = '{secret}';");

        var findings = await new SecretScanner().ScanDirectoryAsync(source);
        var serializedReport = string.Join(" ", findings.Select(f =>
            $"{f.RelativePath} {f.Kind} {f.Action} {f.Description}"));

        Assert.NotEmpty(findings);
        Assert.DoesNotContain(secret, serializedReport);
    }

    [Fact]
    public async Task Scanner_DoesNotTreatCodeVariableReferencesAsSecrets()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source-code")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "AuthService.cs"),
            "var bundle = new TokenBundle { AccessToken = token.AccessToken, RefreshToken = token.RefreshToken };\n");

        var findings = await new SecretScanner().ScanDirectoryAsync(source);

        Assert.DoesNotContain(findings, finding => finding.Severity is FindingSeverity.Warning or FindingSeverity.Critical);
    }

    [Fact]
    public async Task Analyzer_PrefersDesktopApplicationOverTestProject()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "multi-project")).FullName;
        var tests = Directory.CreateDirectory(Path.Combine(source, "tests")).FullName;
        var app = Directory.CreateDirectory(Path.Combine(source, "src", "DesktopApp")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tests, "Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        await File.WriteAllTextAsync(Path.Combine(app, "DesktopApp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>");

        var analysis = await new ProjectAnalyzer(new SecretScanner()).AnalyzeAsync(source);

        Assert.Equal("WPF desktop app", analysis.ProjectType);
        Assert.EndsWith("DesktopApp.csproj", analysis.PrimaryProjectFile, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("repo read:user workflow", true)]
    [InlineData("repo,read:user,workflow", true)]
    [InlineData("repo read:user", false)]
    [InlineData("repo workflow", false)]
    [InlineData(null, false)]
    public void OAuthScopeValidation_RequiresWorkflowPermission(string? scopes, bool expected)
    {
        Assert.Equal(expected, GitHubAuthService.HasRequiredScopes(scopes));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // Test cleanup only.
        }
    }
}
