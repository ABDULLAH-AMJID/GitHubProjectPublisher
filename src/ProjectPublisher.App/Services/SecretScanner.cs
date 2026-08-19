using System.Text;
using System.Text.RegularExpressions;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class SecretScanner
{
    private sealed record SecretRule(
        string Kind,
        FindingSeverity Severity,
        Regex Pattern,
        string Action,
        string Description);

    private static readonly SecretRule[] Rules =
    [
        Rule("GitHub token", FindingSeverity.Critical,
            @"(?<secret>github_pat_[A-Za-z0-9_]{20,255}|gh[pousr]_[A-Za-z0-9]{20,255})"),
        Rule("OpenAI-style API key", FindingSeverity.Critical,
            @"(?<secret>sk-(?:proj-)?[A-Za-z0-9_-]{20,})"),
        Rule("AWS access key", FindingSeverity.Critical,
            @"(?<secret>(?:AKIA|ASIA)[0-9A-Z]{16})"),
        Rule("Google API key", FindingSeverity.Critical,
            @"(?<secret>AIza[0-9A-Za-z_-]{35})"),
        Rule("Slack token", FindingSeverity.Critical,
            @"(?<secret>xox[baprs]-[0-9A-Za-z-]{10,})"),
        Rule("JWT bearer token", FindingSeverity.Warning,
            @"(?<secret>eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})"),
        Rule("Private key block", FindingSeverity.Critical,
            @"(?<secret>-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----)"),
        new SecretRule(
            "Quoted secret-like assignment",
            FindingSeverity.Warning,
            new Regex(
                """(?ix)(?:api[_-]?key|client[_-]?secret|access[_-]?token|auth[_-]?token|secret|password|passwd|pwd|connection[_-]?string)\s*[=:]\s*["'](?<secret>[^"'\r\n]{8,})["']""",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            "Value replaced in the temporary staging copy",
            "A literal value assigned to a secret-like setting was found. The value itself is never displayed or logged."),
        new SecretRule(
            "Unquoted secret-like assignment",
            FindingSeverity.Warning,
            new Regex(
                """(?ix)(?:api[_-]?key|client[_-]?secret|access[_-]?token|auth[_-]?token|secret|password|passwd|pwd|connection[_-]?string)\s*[=:]\s*(?<secret>[A-Za-z0-9_+/=-]{12,})(?=\s|[,;}]|$)""",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            "Value replaced in the temporary staging copy",
            "An unquoted value assigned to a secret-like setting was found. The value itself is never displayed or logged.")
    ];

    public async Task<IReadOnlyList<SecurityFinding>> ScanDirectoryAsync(
        string sourceRoot,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<SecurityFinding>();
        foreach (var file in UploadPolicy.EnumerateFiles(sourceRoot, onSkipped: (relative, reason) =>
                 findings.Add(new SecurityFinding(relative, null, FindingSeverity.Info, "Excluded item", "Not uploaded", reason))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);

            if (UploadPolicy.IsEnvironmentFile(file))
            {
                findings.Add(new SecurityFinding(
                    relative,
                    null,
                    FindingSeverity.Critical,
                    "Environment file",
                    "Excluded; safe example generated",
                    "Environment values will not be uploaded. Only variable names are copied to an example file."));
                continue;
            }

            if (UploadPolicy.IsSensitiveFile(file))
            {
                findings.Add(new SecurityFinding(
                    relative,
                    null,
                    FindingSeverity.Critical,
                    "Sensitive file type",
                    "Excluded from upload",
                    "This credential, certificate, private-key, or keystore file is never copied to staging."));
                continue;
            }

            var info = new FileInfo(file);
            if (info.Length > UploadPolicy.MaximumUploadFileBytes)
            {
                findings.Add(new SecurityFinding(
                    relative,
                    null,
                    FindingSeverity.Warning,
                    "Large file",
                    "Excluded from upload",
                    $"File exceeds the safe per-file limit of {UploadPolicy.MaximumUploadFileBytes / 1_048_576} MB."));
                continue;
            }

            if (!UploadPolicy.IsProbablyText(file)) continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                findings.Add(new SecurityFinding(
                    relative, null, FindingSeverity.Warning, "Unreadable file", "Excluded if copying fails", ex.Message));
                continue;
            }

            foreach (var match in FindMatches(content, file))
            {
                findings.Add(new SecurityFinding(
                    relative,
                    GetLineNumber(content, match.Index),
                    match.Rule.Severity,
                    match.Rule.Kind,
                    match.Rule.Action,
                    match.Rule.Description));
            }
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line)
            .ToArray();
    }

    public (string Content, int RedactionCount) RedactContent(string content, string? sourcePath = null)
    {
        var redactions = 0;
        foreach (var rule in Rules)
        {
            if (rule.Kind == "Unquoted secret-like assignment" && IsSourceCodeFile(sourcePath)) continue;
            content = rule.Pattern.Replace(content, match =>
            {
                var secret = match.Groups["secret"];
                if (!secret.Success || IsPlaceholder(secret.Value)) return match.Value;
                redactions++;
                var offset = secret.Index - match.Index;
                return match.Value[..offset] + "<REDACTED>" + match.Value[(offset + secret.Length)..];
            });
        }
        return (content, redactions);
    }

    private static IEnumerable<(SecretRule Rule, int Index)> FindMatches(string content, string? sourcePath)
    {
        foreach (var rule in Rules)
        {
            if (rule.Kind == "Unquoted secret-like assignment" && IsSourceCodeFile(sourcePath)) continue;
            foreach (Match match in rule.Pattern.Matches(content))
            {
                var secret = match.Groups["secret"];
                if (secret.Success && !IsPlaceholder(secret.Value))
                    yield return (rule, secret.Index);
            }
        }
    }

    private static bool IsSourceCodeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is
            ".cs" or ".fs" or ".vb" or ".js" or ".jsx" or ".ts" or ".tsx" or
            ".mjs" or ".cjs" or ".vue" or ".svelte" or ".py" or ".pyi" or
            ".java" or ".kt" or ".kts" or ".go" or ".rs" or ".rb" or ".php" or
            ".swift" or ".c" or ".h" or ".cpp" or ".hpp" or ".xaml";
    }

    private static int GetLineNumber(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    private static bool IsPlaceholder(string value)
    {
        var normalized = value.Trim().Trim('"', '\'', '<', '>', '[', ']').ToLowerInvariant();
        return normalized.Length < 8 ||
               normalized.Contains('{') ||
               normalized.Contains('}') ||
               normalized.Contains("your_") ||
               normalized.Contains("your-") ||
               normalized.Contains("example") ||
               normalized.Contains("placeholder") ||
               normalized.Contains("fake") ||
               normalized.Contains("dummy") ||
               normalized.Contains("not-a-real") ||
               normalized.Contains("never-upload") ||
               normalized.Contains("actual-super-secret") ||
               normalized.Contains("changeme") ||
               normalized.Contains("replace_me") ||
               normalized.Contains("set_in_local") ||
               normalized.Contains("redacted") ||
               normalized.All(c => c is 'x' or '*' or '-');
    }

    private static SecretRule Rule(string kind, FindingSeverity severity, string pattern) => new(
        kind,
        severity,
        new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)),
        "Value replaced in the temporary staging copy",
        "A credential pattern was found. The secret value itself is never displayed or logged.");
}

public static class UploadPolicy
{
    public const long MaximumUploadFileBytes = 25L * 1024 * 1024;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", ".vscode-test",
        "node_modules", "bin", "obj", "dist", "build", "out", "target", "artifacts",
        "coverage", ".coverage", ".next", ".nuxt", ".svelte-kit", ".turbo",
        ".cache", ".parcel-cache", ".pytest_cache", "__pycache__", ".venv", "venv"
    };

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519",
        "credentials.json", "service-account.json", "serviceaccountkey.json",
        "secrets.json", ".npmrc", ".pypirc", ".netrc"
    };

    private static readonly HashSet<string> SensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pfx", ".p12", ".key", ".pem", ".jks", ".keystore", ".kdbx", ".mobileprovision"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".pdf",
        ".zip", ".7z", ".rar", ".gz", ".tgz", ".tar", ".nupkg", ".jar",
        ".exe", ".dll", ".so", ".dylib", ".class", ".wasm",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".wav", ".ogg", ".flac", ".mp4", ".mov", ".avi", ".mkv",
        ".db", ".sqlite", ".sqlite3", ".pyc"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonc", ".xml", ".config", ".ini", ".toml", ".yaml", ".yml",
        ".cs", ".csproj", ".sln", ".props", ".targets", ".xaml", ".resx",
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".svelte",
        ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ".py", ".pyi", ".java", ".kt", ".kts", ".go", ".rs", ".rb", ".php", ".swift",
        ".c", ".h", ".cpp", ".hpp", ".sh", ".bash", ".zsh", ".ps1", ".bat", ".cmd",
        ".sql", ".graphql", ".gql", ".env", ".example", ".sample", ".gitignore", ".gitattributes"
    };

    public static bool IsExcludedDirectory(string path) =>
        ExcludedDirectories.Contains(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)));

    public static bool IsEnvironmentFile(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return false;
        return !(name.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(".sample", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(".template", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSensitiveFile(string path) =>
        SensitiveNames.Contains(Path.GetFileName(path)) ||
        SensitiveExtensions.Contains(Path.GetExtension(path));

    public static bool IsProbablyText(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        if (name is "Dockerfile" or "Makefile" or "Procfile" or "LICENSE") return true;
        if (BinaryExtensions.Contains(extension)) return false;
        if (TextExtensions.Contains(extension)) return true;

        try
        {
            using var stream = File.OpenRead(path);
            var sample = new byte[Math.Min(8192, (int)Math.Min(stream.Length, 8192))];
            var count = stream.Read(sample, 0, sample.Length);
            if (count == 0) return true;

            var suspiciousControls = 0;
            for (var i = 0; i < count; i++)
            {
                if (sample[i] == 0) return false;
                if (sample[i] < 9 || sample[i] is > 13 and < 32) suspiciousControls++;
            }
            if (suspiciousControls > Math.Max(1, count / 100)) return false;

            _ = new UTF8Encoding(false, true).GetString(sample, 0, count);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> EnumerateFiles(
        string root,
        Action<string, string>? onSkipped = null)
    {
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        pending.Push(fullRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try
            {
                directories = Directory.EnumerateDirectories(directory).ToArray();
                files = Directory.EnumerateFiles(directory).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                onSkipped?.Invoke(Path.GetRelativePath(fullRoot, directory), "Directory could not be read.");
                continue;
            }

            foreach (var child in directories)
            {
                var relative = Path.GetRelativePath(fullRoot, child);
                try
                {
                    var attributes = File.GetAttributes(child);
                    if (IsExcludedDirectory(child))
                    {
                        onSkipped?.Invoke(relative, "Generated/dependency folder is excluded.");
                        continue;
                    }
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        onSkipped?.Invoke(relative, "Symbolic links and junctions are not followed.");
                        continue;
                    }
                    pending.Push(child);
                }
                catch
                {
                    onSkipped?.Invoke(relative, "Directory attributes could not be verified.");
                }
            }

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(fullRoot, file);
                var safeToYield = false;
                try
                {
                    var attributes = File.GetAttributes(file);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        onSkipped?.Invoke(relative, "Symbolic links are not uploaded.");
                    }
                    else
                    {
                        safeToYield = true;
                    }
                }
                catch
                {
                    onSkipped?.Invoke(relative, "File attributes could not be verified.");
                }

                if (safeToYield) yield return file;
            }
        }
    }
}
