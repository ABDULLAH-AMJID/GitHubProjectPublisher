using System.Text;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

/// <summary>
/// Creates a separate upload tree. This service never writes to the selected
/// source folder. All redaction, generated examples, README files, screenshots,
/// Git metadata, builds, and package installs happen outside the source folder.
/// </summary>
public sealed class StagingService
{
    private readonly SecretScanner _scanner;

    public StagingService(SecretScanner scanner) => _scanner = scanner;

    public async Task<StagingResult> CopySanitizedAsync(
        string sourceRoot,
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        destinationRoot = Path.GetFullPath(destinationRoot);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException(sourceRoot);
        if (destinationRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The staging folder cannot be inside the selected project.");

        Directory.CreateDirectory(destinationRoot);
        var copied = 0;
        var excluded = 0;
        var redacted = 0;
        var environmentExamples = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFile in UploadPolicy.EnumerateFiles(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);

            if (UploadPolicy.IsEnvironmentFile(sourceFile))
            {
                excluded++;
                var exampleRelative = GetEnvironmentExampleName(relative);
                var safeExample = await BuildEnvironmentExampleAsync(sourceFile, cancellationToken);
                if (!environmentExamples.TryGetValue(exampleRelative, out var combined))
                {
                    combined = new StringBuilder();
                    environmentExamples[exampleRelative] = combined;
                }
                combined.AppendLine(safeExample.TrimEnd());
                progress?.Report($"Protected environment file: {relative}");
                continue;
            }

            var info = new FileInfo(sourceFile);
            if (UploadPolicy.IsSensitiveFile(sourceFile) || info.Length > UploadPolicy.MaximumUploadFileBytes)
            {
                excluded++;
                progress?.Report($"Excluded sensitive/large file: {relative}");
                continue;
            }

            var destinationFile = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            try
            {
                if (UploadPolicy.IsProbablyText(sourceFile))
                {
                    var content = await File.ReadAllTextAsync(sourceFile, cancellationToken);
                    var result = _scanner.RedactContent(content);
                    redacted += result.RedactionCount;
                    await File.WriteAllTextAsync(destinationFile, result.Content, new UTF8Encoding(false), cancellationToken);
                }
                else
                {
                    File.Copy(sourceFile, destinationFile, true);
                }
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                excluded++;
                progress?.Report($"Skipped unreadable file: {relative}");
            }
        }

        foreach (var example in environmentExamples)
        {
            var examplePath = Path.Combine(destinationRoot, example.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(examplePath)!);
            await MergeEnvironmentExampleAsync(examplePath, example.Value.ToString(), cancellationToken);
        }

        await AppendSecurityGitIgnoreAsync(destinationRoot, cancellationToken);
        return new StagingResult
        {
            Destination = destinationRoot,
            CopiedFiles = copied,
            ExcludedFiles = excluded,
            RedactedSecrets = redacted,
            EnvironmentExamplesCreated = environmentExamples.Keys.ToArray()
        };
    }

    private static string GetEnvironmentExampleName(string relative)
    {
        var directory = Path.GetDirectoryName(relative);
        var name = Path.GetFileName(relative);
        var exampleName = name.Equals(".env", StringComparison.OrdinalIgnoreCase)
            ? ".env.example"
            : name + ".example";
        return string.IsNullOrEmpty(directory) ? exampleName : Path.Combine(directory, exampleName);
    }

    private static async Task<string> BuildEnvironmentExampleAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        foreach (var rawLine in await File.ReadAllLinesAsync(path, cancellationToken))
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
            if (key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
                output.AppendLine($"{key}=__SET_IN_LOCAL_ENV__");
        }
        return output.ToString();
    }

    private static async Task MergeEnvironmentExampleAsync(
        string destination,
        string generated,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(destination))
        {
            await File.WriteAllTextAsync(destination, generated, new UTF8Encoding(false), cancellationToken);
            return;
        }

        var existing = await File.ReadAllTextAsync(destination, cancellationToken);
        var existingKeys = existing.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains('=') && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2)[0].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = generated.Split('\n')
            .Where(line => line.Contains('=') && !existingKeys.Contains(line.Split('=', 2)[0].Trim()));
        var merged = existing.TrimEnd() + Environment.NewLine + string.Join(Environment.NewLine, additions) + Environment.NewLine;
        await File.WriteAllTextAsync(destination, merged, new UTF8Encoding(false), cancellationToken);
    }

    private static async Task AppendSecurityGitIgnoreAsync(
        string destinationRoot,
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

""";
        var path = Path.Combine(destinationRoot, ".gitignore");
        var current = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
        if (!current.Contains(marker, StringComparison.Ordinal))
            await File.AppendAllTextAsync(path, rules, new UTF8Encoding(false), cancellationToken);
    }
}
