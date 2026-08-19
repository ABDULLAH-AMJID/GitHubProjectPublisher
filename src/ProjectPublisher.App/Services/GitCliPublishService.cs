using System.Text;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class GitCliPublishService
{
    private const string SecurityMarker = "# Project Publisher protected paths";
    private readonly GitCommandService _git;
    private readonly GitHubApiService _gitHub;
    private readonly ReadmeGenerator _readme;

    public GitCliPublishService(GitCommandService git, GitHubApiService gitHub, ReadmeGenerator readme)
    {
        _git = git;
        _gitHub = gitHub;
        _readme = readme;
    }

    public async Task<PublishResult> PublishAsync(
        ProjectAnalysis analysis,
        GitCliPublishOptions options,
        GitHubUser user,
        string token,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Validate(options, analysis.SourcePath);
        var root = Path.GetFullPath(analysis.SourcePath);
        await _git.CheckVersionAsync(progress, cancellationToken);
        await _git.RunAsync(root, ["check-ref-format", "--branch", options.BranchName], null, cancellationToken);
        await EnsureRepositoryRootAsync(root, progress, cancellationToken);

        progress?.Report("◇ Applying local Git security exclusions...");
        var protectedPaths = GetProtectedPaths(analysis);
        await WriteLocalExcludesAsync(root, protectedPaths, cancellationToken);
        await EnsureSecurityGitIgnoreAsync(root, progress, cancellationToken);
        await UntrackProtectedFilesAsync(root, protectedPaths, progress, cancellationToken);
        await GenerateEnvironmentExamplesAsync(root, progress, cancellationToken);
        await EnsureLineEndingPolicyAsync(root, progress, cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.ScreenshotPath) && File.Exists(options.ScreenshotPath))
        {
            var imageDirectory = Path.Combine(root, "images");
            Directory.CreateDirectory(imageDirectory);
            var destination = Path.Combine(imageDirectory, "project-preview.png");
            SaveScreenshotAsPng(options.ScreenshotPath, destination);
            progress?.Report("◇ Added reviewed interface image: images/project-preview.png");
        }

        if (options.GenerateReadmeWhenMissing && !HasReadme(root))
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                _readme.Generate(analysis, options.Description, !string.IsNullOrWhiteSpace(options.ScreenshotPath)),
                cancellationToken);
            progress?.Report("◇ Generated README.md in the selected project.");
        }

        await _git.RunAsync(root, ["status", "--short"], progress, cancellationToken, throwOnError: false);
        await _git.RunAsync(root, ["add", "--all", "--", "."], progress, cancellationToken);
        await UntrackProtectedFilesAsync(root, protectedPaths, progress, cancellationToken);

        var stagedCheck = await _git.RunAsync(
            root, ["diff", "--cached", "--quiet"], null, cancellationToken, throwOnError: false);
        var hasStagedChanges = stagedCheck.ExitCode == 1;
        if (stagedCheck.ExitCode is not (0 or 1))
            throw new GitCommandException(stagedCheck.CommandDisplay, stagedCheck.ExitCode, stagedCheck.StandardError);

        var hasHead = (await _git.RunAsync(
            root, ["rev-parse", "--verify", "HEAD"], null, cancellationToken, throwOnError: false)).ExitCode == 0;
        string commitSha;
        if (hasStagedChanges)
        {
            await EnsureIdentityAsync(root, user, progress, cancellationToken);
            await _git.RunAsync(root, ["commit", "-m", options.CommitMessage], progress, cancellationToken);
            commitSha = (await _git.RunAsync(root, ["rev-parse", "HEAD"], null, cancellationToken)).StandardOutput.Trim();
        }
        else if (hasHead)
        {
            progress?.Report("◇ No staged changes; existing commit will be pushed.");
            commitSha = (await _git.RunAsync(root, ["rev-parse", "HEAD"], null, cancellationToken)).StandardOutput.Trim();
        }
        else
        {
            throw new InvalidOperationException(
                "No safe files are available to commit. Protected files were left local and were not staged.");
        }

        progress?.Report("◇ Checking GitHub repository metadata...");
        var remoteRepository = await _gitHub.GetRepositoryAsync(
            options.Owner, options.RepositoryName, token, cancellationToken);
        var created = remoteRepository is null;
        remoteRepository ??= await _gitHub.CreateRepositoryAsync(
            options.Owner, user, options.RepositoryName, options.Description,
            options.IsPrivate, token, cancellationToken);

        await ConfigureOriginAsync(root, remoteRepository.CloneUrl, options.ReplaceOrigin, progress, cancellationToken);
        await _git.RunAsync(root, ["branch", "-M", options.BranchName], progress, cancellationToken);

        var auth = _git.CreateGitHubAuthEnvironment(user.Login, token);
        await _git.RunAsync(
            root,
            ["push", "--set-upstream", "origin", options.BranchName],
            progress,
            cancellationToken,
            auth);

        if (!created)
        {
            try
            {
                await _gitHub.UpdateRepositoryDescriptionAsync(
                    options.Owner, options.RepositoryName, options.Description, token, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress?.Report($"◇ Push succeeded; description update warning: {ex.Message}");
            }
        }

        return new PublishResult(
            remoteRepository.HtmlUrl,
            created,
            0,
            protectedPaths.Count,
            commitSha,
            hasStagedChanges);
    }

    public async Task<string> StatusAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureExistingRepositoryAsync(root, cancellationToken);
        var result = await _git.RunAsync(root, ["status", "--short", "--branch"], progress, cancellationToken);
        return string.IsNullOrWhiteSpace(result.StandardOutput) ? "Working tree clean." : result.StandardOutput;
    }

    public async Task FetchAsync(
        string root,
        GitHubUser user,
        string token,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureExistingRepositoryAsync(root, cancellationToken);
        await _git.RunAsync(root, ["fetch", "origin", "--prune"], progress, cancellationToken,
            _git.CreateGitHubAuthEnvironment(user.Login, token));
    }

    public async Task PullRebaseAsync(
        string root,
        string branch,
        GitHubUser user,
        string token,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureExistingRepositoryAsync(root, cancellationToken);
        await _git.RunAsync(root, ["pull", "--rebase", "origin", branch], progress, cancellationToken,
            _git.CreateGitHubAuthEnvironment(user.Login, token));
    }

    public async Task<string> HistoryAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await EnsureExistingRepositoryAsync(root, cancellationToken);
        var result = await _git.RunAsync(
            root,
            ["--no-pager", "log", "-25", "--date=short", "--pretty=format:%h  %ad  %an  %s"],
            progress,
            cancellationToken);
        return result.StandardOutput;
    }

    private async Task EnsureRepositoryRootAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var check = await _git.RunAsync(
            root, ["rev-parse", "--show-toplevel"], null, cancellationToken, throwOnError: false);
        if (check.ExitCode == 0)
        {
            var actual = Path.GetFullPath(check.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar));
            if (!actual.TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The selected folder is inside another Git repository: {actual}. Select that repository root or a folder outside it.");
            }
            progress?.Report("◇ Existing Git repository detected.");
            return;
        }

        await _git.RunAsync(root, ["init"], progress, cancellationToken);
    }

    private async Task EnsureExistingRepositoryAsync(string root, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException("Select a valid project folder.");
        var check = await _git.RunAsync(root, ["rev-parse", "--show-toplevel"], null, cancellationToken, throwOnError: false);
        if (check.ExitCode != 0) throw new InvalidOperationException("The selected folder is not a Git repository yet.");
    }

    private async Task EnsureIdentityAsync(
        string root,
        GitHubUser user,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var name = await _git.RunAsync(root, ["config", "--local", "--get", "user.name"], null,
            cancellationToken, throwOnError: false);
        if (name.ExitCode != 0 || string.IsNullOrWhiteSpace(name.StandardOutput))
            await _git.RunAsync(root, ["config", "--local", "user.name", user.Name ?? user.Login], progress, cancellationToken);

        var email = await _git.RunAsync(root, ["config", "--local", "--get", "user.email"], null,
            cancellationToken, throwOnError: false);
        if (email.ExitCode != 0 || string.IsNullOrWhiteSpace(email.StandardOutput))
            await _git.RunAsync(root,
                ["config", "--local", "user.email", $"{user.Id}+{user.Login}@users.noreply.github.com"],
                progress, cancellationToken);
    }

    private async Task ConfigureOriginAsync(
        string root,
        string expectedUrl,
        bool replaceOrigin,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var current = await _git.RunAsync(root, ["remote", "get-url", "origin"], null,
            cancellationToken, throwOnError: false);
        if (current.ExitCode != 0)
        {
            await _git.RunAsync(root, ["remote", "add", "origin", expectedUrl], progress, cancellationToken);
            return;
        }
        if (NormalizeRemote(current.StandardOutput) == NormalizeRemote(expectedUrl)) return;
        if (!replaceOrigin)
            throw new InvalidOperationException(
                $"Origin already points to {current.StandardOutput.Trim()}. Enable 'Replace different origin' only after reviewing both URLs.");
        await _git.RunAsync(root, ["remote", "set-url", "origin", expectedUrl], progress, cancellationToken);
    }

    private async Task UntrackProtectedFilesAsync(
        string root,
        IReadOnlyCollection<string> protectedPaths,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var relative in protectedPaths)
        {
            await _git.RunAsync(
                root,
                ["rm", "--cached", "-f", "--ignore-unmatch", "--", relative],
                progress,
                cancellationToken,
                throwOnError: false);
        }
    }

    private static IReadOnlyCollection<string> GetProtectedPaths(ProjectAnalysis analysis) => analysis.Findings
        .Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
        .Select(f => f.RelativePath.Replace('\\', '/'))
        .Where(path => !path.Equals(".", StringComparison.Ordinal) && !path.EndsWith('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static async Task EnsureSecurityGitIgnoreAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        const string marker = "# Project Publisher security rules";
        const string rules = """

# Project Publisher security rules
.env
.env.*
!.env.example
!.env.*.example
*.pfx
*.p12
*.pem
*.key
*.jks
*.keystore
credentials.json
service-account.json
secrets.json
node_modules/
.venv/
venv/
bin/
obj/
__pycache__/

""";
        var path = Path.Combine(root, ".gitignore");
        var current = File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : string.Empty;
        if (current.Contains(marker, StringComparison.Ordinal)) return;
        await File.AppendAllTextAsync(path, rules, new UTF8Encoding(false), cancellationToken);
        progress?.Report("◇ Added repository-wide security rules to .gitignore.");
    }

    private static async Task GenerateEnvironmentExamplesAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var environmentFile in UploadPolicy.EnumerateFiles(root).Where(UploadPolicy.IsEnvironmentFile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, environmentFile);
            var directory = Path.GetDirectoryName(relative);
            var name = Path.GetFileName(relative);
            var exampleName = name.Equals(".env", StringComparison.OrdinalIgnoreCase)
                ? ".env.example"
                : name + ".example";
            var exampleRelative = string.IsNullOrEmpty(directory)
                ? exampleName
                : Path.Combine(directory, exampleName);
            var examplePath = Path.Combine(root, exampleRelative);
            if (File.Exists(examplePath)) continue;

            var output = new StringBuilder();
            foreach (var rawLine in await File.ReadAllLinesAsync(environmentFile, cancellationToken))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    output.AppendLine(rawLine);
                    continue;
                }
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line[..separator].Trim();
                if (key.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) key = key[7..].Trim();
                if (key.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.'))
                    output.AppendLine($"{key}=__SET_IN_LOCAL_ENV__");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(examplePath)!);
            await File.WriteAllTextAsync(examplePath, output.ToString(), new UTF8Encoding(false), cancellationToken);
            progress?.Report($"◇ Generated value-free environment template: {exampleRelative}");
        }
    }

    private static async Task EnsureLineEndingPolicyAsync(
        string root,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ".gitattributes");
        if (File.Exists(path)) return;
        const string attributes = """
# Project Publisher cross-platform line-ending policy
* text=auto
*.bat text eol=crlf
*.cmd text eol=crlf
*.ps1 text eol=crlf
*.sh text eol=lf
*.py text eol=lf
*.yml text eol=lf
*.yaml text eol=lf
*.json text eol=lf
*.md text eol=lf

""";
        await File.WriteAllTextAsync(path, attributes, new UTF8Encoding(false), cancellationToken);
        progress?.Report("◇ Added .gitattributes to normalize LF/CRLF safely.");
    }

    private static async Task WriteLocalExcludesAsync(
        string root,
        IReadOnlyCollection<string> protectedPaths,
        CancellationToken cancellationToken)
    {
        var infoDirectory = Path.Combine(root, ".git", "info");
        Directory.CreateDirectory(infoDirectory);
        var excludePath = Path.Combine(infoDirectory, "exclude");
        var current = File.Exists(excludePath)
            ? await File.ReadAllTextAsync(excludePath, cancellationToken)
            : string.Empty;
        var beforeMarker = current.Contains(SecurityMarker, StringComparison.Ordinal)
            ? current[..current.IndexOf(SecurityMarker, StringComparison.Ordinal)].TrimEnd()
            : current.TrimEnd();
        var builder = new StringBuilder(beforeMarker);
        if (builder.Length > 0) builder.AppendLine().AppendLine();
        builder.AppendLine(SecurityMarker);
        builder.AppendLine(".env");
        builder.AppendLine(".env.*");
        builder.AppendLine("!.env.example");
        builder.AppendLine("!.env.*.example");
        builder.AppendLine("*.pfx");
        builder.AppendLine("*.p12");
        builder.AppendLine("*.pem");
        builder.AppendLine("*.key");
        builder.AppendLine("*.jks");
        builder.AppendLine("*.keystore");
        builder.AppendLine("credentials.json");
        builder.AppendLine("secrets.json");
        builder.AppendLine("node_modules/");
        builder.AppendLine(".venv/");
        builder.AppendLine("venv/");
        builder.AppendLine("bin/");
        builder.AppendLine("obj/");
        builder.AppendLine("dist/");
        builder.AppendLine("build/");
        builder.AppendLine("coverage/");
        builder.AppendLine("__pycache__/");
        foreach (var path in protectedPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine('/' + EscapeIgnorePath(path));
        await File.WriteAllTextAsync(excludePath, builder.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static string EscapeIgnorePath(string path) => path
        .Replace("\\", "/")
        .Replace("[", "\\[")
        .Replace("]", "\\]")
        .Replace("#", "\\#")
        .Replace("!", "\\!");

    private static string NormalizeRemote(string value) => value.Trim().TrimEnd('/')
        .Replace("git@github.com:", "https://github.com/", StringComparison.OrdinalIgnoreCase)
        .Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase)
        .ToLowerInvariant();

    private static bool HasReadme(string root) => Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
        .Any(file => Path.GetFileName(file).StartsWith("README", StringComparison.OrdinalIgnoreCase));

    private static void SaveScreenshotAsPng(string source, string destination)
    {
        var temporary = destination + ".project-publisher.tmp.png";
        using (var image = System.Drawing.Image.FromFile(source))
        using (var bitmap = new System.Drawing.Bitmap(image.Width, image.Height))
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            bitmap.Save(temporary, System.Drawing.Imaging.ImageFormat.Png);
        }
        File.Move(temporary, destination, true);
    }

    private static void Validate(GitCliPublishOptions options, string source)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Select a valid project folder.");
        if (string.IsNullOrWhiteSpace(options.Owner) || string.IsNullOrWhiteSpace(options.RepositoryName))
            throw new InvalidOperationException("Repository owner and name are required.");
        if (string.IsNullOrWhiteSpace(options.CommitMessage))
            throw new InvalidOperationException("Commit message is required.");
    }
}
