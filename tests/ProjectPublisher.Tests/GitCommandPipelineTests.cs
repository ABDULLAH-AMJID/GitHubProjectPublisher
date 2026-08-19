using System.Net;
using System.Text;
using ProjectPublisher.Models;
using ProjectPublisher.Services;
using Xunit;

namespace ProjectPublisher.Tests;

public sealed class GitCommandPipelineTests
{
    [Fact]
    public async Task DirectPipeline_CommitsSafeFiles_AndKeepsSecretsLocal()
    {
        var root = Path.Combine(Path.GetTempPath(), "ProjectPublisherGitTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "project");
        var remote = Path.Combine(root, "remote.git");
        Directory.CreateDirectory(source);

        try
        {
            var scanner = new SecretScanner();
            var git = new GitCommandService(scanner);
            await git.RunAsync(root, ["init", "--bare", remote], null, CancellationToken.None);

            const string originalEnvironment =
                "API_KEY=never-upload-this-value\nPUBLIC_URL=https://example.test\n";
            await File.WriteAllTextAsync(Path.Combine(source, ".env"), originalEnvironment);
            await File.WriteAllTextAsync(Path.Combine(source, "README.md"), "# Demo\n");
            await File.WriteAllTextAsync(Path.Combine(source, "app.py"), "print('safe')\n");
            var shapedKey = "sk-" + "proj-" + "fakefakefakefakefakefake123";
            await File.WriteAllTextAsync(Path.Combine(source, "config.py"), $"api_key='{shapedKey}'\n");

            var analysis = new ProjectAnalysis
            {
                SourcePath = source,
                ProjectName = "demo",
                SuggestedRepositoryName = "demo",
                SuggestedDescription = "demo",
                ProjectType = "Python project",
                Languages = new Dictionary<string, int> { ["Python"] = 2 },
                Findings =
                [
                    new SecurityFinding(".env", null, FindingSeverity.Critical,
                        "Environment file", "Exclude", "Test"),
                    new SecurityFinding("config.py", 1, FindingSeverity.Warning,
                        "Secret-like assignment", "Exclude", "Test")
                ],
                TotalBytes = 100,
                FileCount = 4,
                ExcludedFileCount = 1
            };

            var repositoryJson = $$"""
            {
              "name":"demo",
              "full_name":"tester/demo",
              "html_url":"https://example.test/tester/demo",
              "clone_url":{{System.Text.Json.JsonSerializer.Serialize(remote)}},
              "default_branch":"main",
              "private":true
            }
            """;
            var httpClient = new HttpClient(new FakeGitHubHandler(repositoryJson))
            {
                BaseAddress = new Uri("https://api.github.com/")
            };
            var publisher = new GitCliPublishService(
                git,
                new GitHubApiService(httpClient),
                new ReadmeGenerator());

            var result = await publisher.PublishAsync(
                analysis,
                new GitCliPublishOptions
                {
                    Owner = "tester",
                    RepositoryName = "demo",
                    Description = "demo",
                    CommitMessage = "Initial secure publish",
                    BranchName = "main",
                    IsPrivate = true,
                    GenerateReadmeWhenMissing = true,
                    ReplaceOrigin = false
                },
                new GitHubUser { Login = "tester", Id = 42, Name = "Tester" },
                "non-production-test-value",
                null,
                CancellationToken.None);

            var tree = await git.RunAsync(
                root,
                ["--git-dir", remote, "ls-tree", "-r", "--name-only", "main"],
                null,
                CancellationToken.None);
            var committed = tree.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("README.md", committed);
            Assert.Contains("app.py", committed);
            Assert.Contains(".env.example", committed);
            Assert.Contains(".gitattributes", committed);
            Assert.Contains(".gitignore", committed);
            Assert.DoesNotContain(".env", committed);
            Assert.DoesNotContain("config.py", committed);
            Assert.Equal(originalEnvironment, await File.ReadAllTextAsync(Path.Combine(source, ".env")));
            Assert.True(File.Exists(Path.Combine(source, "config.py")));
            Assert.Equal(2, result.ExcludedFiles);
        }
        finally
        {
            AppPaths.TryDeleteTree(root);
        }
    }

    private sealed class FakeGitHubHandler(string repositoryJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(repositoryJson, Encoding.UTF8, "application/json")
        });
    }
}
