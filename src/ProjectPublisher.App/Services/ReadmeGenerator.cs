using System.Text;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class ReadmeGenerator
{
    public string Generate(ProjectAnalysis analysis, string description, bool hasScreenshot)
    {
        var title = ToTitle(analysis.ProjectName);
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(description) ? analysis.SuggestedDescription : description.Trim());
        builder.AppendLine();

        if (hasScreenshot)
        {
            builder.AppendLine("## Preview");
            builder.AppendLine();
            builder.AppendLine("![Project interface](docs/project-preview.png)");
            builder.AppendLine();
        }

        builder.AppendLine("## Overview");
        builder.AppendLine();
        builder.AppendLine($"- **Project type:** {analysis.ProjectType}");
        builder.AppendLine($"- **Source files:** {analysis.FileCount}");
        if (analysis.Languages.Count > 0)
            builder.AppendLine($"- **Technologies:** {string.Join(", ", analysis.Languages.Keys.Take(8))}");
        builder.AppendLine();

        AppendGettingStarted(builder, analysis);

        if (analysis.Languages.Count > 0)
        {
            builder.AppendLine("## Technology breakdown");
            builder.AppendLine();
            foreach (var language in analysis.Languages.Take(10))
                builder.AppendLine($"- {language.Key}: {language.Value} source file(s)");
            builder.AppendLine();
        }

        builder.AppendLine("## Configuration and security");
        builder.AppendLine();
        builder.AppendLine("Secrets are not stored in this repository. Copy any provided `.env.example` file to `.env` locally, then add your own values. Never commit API keys, access tokens, passwords, private keys, or production connection strings.");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("_Repository prepared with Project Publisher. Review and customize this README for your project._");
        return builder.ToString();
    }

    private static void AppendGettingStarted(StringBuilder builder, ProjectAnalysis analysis)
    {
        builder.AppendLine("## Getting started");
        builder.AppendLine();
        if (analysis.ProjectType.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
            analysis.ProjectType.Contains("web app", StringComparison.OrdinalIgnoreCase) &&
            !analysis.ProjectType.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("```bash");
            builder.AppendLine("npm install");
            builder.AppendLine(analysis.SuggestedRunCommand ?? "npm run dev");
            builder.AppendLine("```");
        }
        else if (analysis.ProjectType.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                 analysis.ProjectType.Contains("WPF", StringComparison.OrdinalIgnoreCase) ||
                 analysis.ProjectType.Contains("WinUI", StringComparison.OrdinalIgnoreCase) ||
                 analysis.ProjectType.Contains("Windows Forms", StringComparison.OrdinalIgnoreCase) ||
                 analysis.ProjectType.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("```powershell");
            builder.AppendLine("dotnet restore");
            builder.AppendLine(analysis.SuggestedRunCommand ??
                               (analysis.PrimaryProjectFile is null
                                   ? "dotnet run"
                                   : $"dotnet run --project \"{analysis.PrimaryProjectFile}\""));
            builder.AppendLine("```");
        }
        else if (analysis.ProjectType.Equals("Static website", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("Open `index.html` in a browser, or serve the folder with a local static-file server.");
        }
        else
        {
            builder.AppendLine("Install the toolchain required by the project, configure local environment values, and run the main project entry point.");
        }
        builder.AppendLine();
    }

    private static string ToTitle(string name)
    {
        var words = name.Replace('-', ' ').Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
        return string.Join(' ', words);
    }
}
