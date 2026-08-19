using System.Text.Json;
using System.Text.RegularExpressions;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class ProjectAnalyzer
{
    private readonly SecretScanner _scanner;

    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".fs"] = "F#", [".vb"] = "Visual Basic",
        [".js"] = "JavaScript", [".jsx"] = "JavaScript", [".mjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript", [".vue"] = "Vue", [".svelte"] = "Svelte",
        [".py"] = "Python", [".java"] = "Java", [".kt"] = "Kotlin", [".go"] = "Go", [".rs"] = "Rust",
        [".php"] = "PHP", [".rb"] = "Ruby", [".swift"] = "Swift",
        [".c"] = "C", [".h"] = "C/C++", [".cpp"] = "C++", [".hpp"] = "C++",
        [".html"] = "HTML", [".css"] = "CSS", [".scss"] = "SCSS", [".xaml"] = "XAML",
        [".sql"] = "SQL", [".sh"] = "Shell", [".ps1"] = "PowerShell"
    };

    public ProjectAnalyzer(SecretScanner scanner) => _scanner = scanner;

    public async Task<ProjectAnalysis> AnalyzeAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException("Choose a valid project folder.");

        sourcePath = Path.GetFullPath(sourcePath);
        var files = UploadPolicy.EnumerateFiles(sourcePath).ToArray();
        var languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        var includedCount = 0;
        var excludedCount = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (UploadPolicy.IsEnvironmentFile(file) || UploadPolicy.IsSensitiveFile(file) ||
                info.Length > UploadPolicy.MaximumUploadFileBytes)
            {
                excludedCount++;
                continue;
            }

            includedCount++;
            totalBytes += info.Length;
            if (LanguageByExtension.TryGetValue(Path.GetExtension(file), out var language))
                languages[language] = languages.GetValueOrDefault(language) + 1;
        }

        var detection = await DetectProjectAsync(sourcePath, files, cancellationToken);
        var findings = await _scanner.ScanDirectoryAsync(sourcePath, cancellationToken);
        var folderName = new DirectoryInfo(sourcePath).Name;
        var repositoryName = Regex.Replace(folderName.Trim(), @"[^A-Za-z0-9._-]+", "-").Trim('-', '.');
        if (string.IsNullOrWhiteSpace(repositoryName)) repositoryName = "my-project";

        return new ProjectAnalysis
        {
            SourcePath = sourcePath,
            ProjectName = folderName,
            SuggestedRepositoryName = repositoryName,
            SuggestedDescription = $"{detection.Type} project published securely with Project Publisher.",
            ProjectType = detection.Type,
            PrimaryProjectFile = detection.ProjectFile,
            SuggestedRunCommand = detection.RunCommand,
            SuggestedPreviewUrl = detection.PreviewUrl,
            Languages = languages.OrderByDescending(pair => pair.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Findings = findings,
            TotalBytes = totalBytes,
            FileCount = includedCount,
            ExcludedFileCount = excludedCount
        };
    }

    private static async Task<(string Type, string? ProjectFile, string? RunCommand, string? PreviewUrl)>
        DetectProjectAsync(string root, string[] files, CancellationToken cancellationToken)
    {
        var packageJson = files.FirstOrDefault(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (packageJson is not null)
        {
            try
            {
                await using var stream = File.OpenRead(packageJson);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var allPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var propertyName in new[] { "dependencies", "devDependencies" })
                {
                    if (json.RootElement.TryGetProperty(propertyName, out var dependencies))
                        foreach (var property in dependencies.EnumerateObject()) allPackages.Add(property.Name);
                }

                var type = allPackages.Contains("next") ? "Next.js web app" :
                    allPackages.Contains("@angular/core") ? "Angular web app" :
                    allPackages.Contains("vue") ? "Vue web app" :
                    allPackages.Contains("svelte") ? "Svelte web app" :
                    allPackages.Contains("react") ? "React web app" : "Node.js project";

                string? script = null;
                if (json.RootElement.TryGetProperty("scripts", out var scripts))
                {
                    if (scripts.TryGetProperty("dev", out _)) script = "npm run dev";
                    else if (scripts.TryGetProperty("start", out _)) script = "npm start";
                }

                if (script == "npm run dev" && type is not "Next.js web app")
                    script += " -- --host 127.0.0.1";

                var url = type switch
                {
                    "Next.js web app" => "http://127.0.0.1:3000",
                    "Angular web app" => "http://127.0.0.1:4200",
                    "React web app" when script == "npm start" => "http://127.0.0.1:3000",
                    _ => "http://127.0.0.1:5173"
                };
                return (type, Path.GetRelativePath(root, packageJson), script, url);
            }
            catch
            {
                return ("Node.js project", Path.GetRelativePath(root, packageJson), "npm run dev", "http://127.0.0.1:5173");
            }
        }

        var dotNetProjects = new List<(int Priority, string Type, string Relative, string? RunCommand, string? PreviewUrl)>();
        foreach (var projectFile in files.Where(f =>
                     Path.GetExtension(f).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var text = await File.ReadAllTextAsync(projectFile, cancellationToken);
            var relative = Path.GetRelativePath(root, projectFile);
            var testPenalty = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                             part.Equals("tests", StringComparison.OrdinalIgnoreCase)) ? 50 : 0;

            if (text.Contains("<UseWPF>true", StringComparison.OrdinalIgnoreCase))
                dotNetProjects.Add((100 - testPenalty, "WPF desktop app", relative, null, null));
            else if (text.Contains("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase))
                dotNetProjects.Add((95 - testPenalty, "WinUI 3 desktop app", relative, null, null));
            else if (text.Contains("<UseWindowsForms>true", StringComparison.OrdinalIgnoreCase))
                dotNetProjects.Add((90 - testPenalty, "Windows Forms desktop app", relative, null, null));
            else if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                dotNetProjects.Add((85 - testPenalty, "ASP.NET Core web app", relative,
                    $"dotnet run --project \"{relative}\" --no-launch-profile", "http://127.0.0.1:5199"));
            else
                dotNetProjects.Add((10 - testPenalty, ".NET project", relative, null, null));
        }

        if (dotNetProjects.Count > 0)
        {
            var selected = dotNetProjects.OrderByDescending(project => project.Priority).First();
            return (selected.Type, selected.Relative, selected.RunCommand, selected.PreviewUrl);
        }

        var html = files.FirstOrDefault(f => Path.GetFileName(f).Equals("index.html", StringComparison.OrdinalIgnoreCase));
        if (html is not null)
            return ("Static website", Path.GetRelativePath(root, html), null, new Uri(html).AbsoluteUri);

        if (files.Any(f => Path.GetFileName(f).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetFileName(f).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
            return ("Python project", null, null, null);

        if (files.Any(f => Path.GetFileName(f).Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)))
            return ("Rust project", "Cargo.toml", null, null);

        if (files.Any(f => Path.GetFileName(f).Equals("go.mod", StringComparison.OrdinalIgnoreCase)))
            return ("Go project", "go.mod", null, null);

        return ("Software project", null, null, null);
    }
}
