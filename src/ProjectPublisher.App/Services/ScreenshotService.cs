using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Playwright;
using ProjectPublisher.Models;

namespace ProjectPublisher.Services;

public sealed class ScreenshotService
{
    private readonly StagingService _staging;
    private readonly ProcessRunner _processRunner;

    public ScreenshotService(StagingService staging, ProcessRunner processRunner)
    {
        _staging = staging;
        _processRunner = processRunner;
    }

    public async Task<string> CaptureAsync(
        ProjectAnalysis analysis,
        string? customCommand,
        string? customUrl,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var work = AppPaths.CreateTemporaryDirectory("screenshot-work");
        var destination = Path.Combine(AppPaths.PreviewRoot,
            $"{analysis.SuggestedRepositoryName}-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        try
        {
            progress?.Report("Creating an isolated, sanitized preview copy...");
            await _staging.CopySanitizedAsync(analysis.SourcePath, work, progress, cancellationToken);

            if (analysis.ProjectType.Contains("web", StringComparison.OrdinalIgnoreCase) ||
                analysis.ProjectType.Equals("Static website", StringComparison.OrdinalIgnoreCase))
            {
                await CaptureWebAsync(analysis, work, customCommand, customUrl, destination, progress, cancellationToken);
            }
            else if (analysis.ProjectType.Contains("desktop", StringComparison.OrdinalIgnoreCase))
            {
                await CaptureDesktopAsync(analysis, work, destination, progress, cancellationToken);
            }
            else
            {
                throw new NotSupportedException(
                    "Automatic capture currently supports detected web, WPF, WinUI 3, and Windows Forms projects. You can select an image manually.");
            }

            return destination;
        }
        finally
        {
            AppPaths.TryDeleteTree(work);
        }
    }

    private async Task CaptureWebAsync(
        ProjectAnalysis analysis,
        string work,
        string? customCommand,
        string? customUrl,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Process? server = null;
        try
        {
            string targetUrl;
            if (analysis.ProjectType.Equals("Static website", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(analysis.PrimaryProjectFile))
                    throw new InvalidOperationException("No index.html was detected.");
                targetUrl = new Uri(Path.Combine(work, analysis.PrimaryProjectFile)).AbsoluteUri;
            }
            else
            {
                if (analysis.ProjectType.Contains("Node", StringComparison.OrdinalIgnoreCase) ||
                    analysis.ProjectType.Contains("React", StringComparison.OrdinalIgnoreCase) ||
                    analysis.ProjectType.Contains("Next", StringComparison.OrdinalIgnoreCase) ||
                    analysis.ProjectType.Contains("Angular", StringComparison.OrdinalIgnoreCase) ||
                    analysis.ProjectType.Contains("Vue", StringComparison.OrdinalIgnoreCase) ||
                    analysis.ProjectType.Contains("Svelte", StringComparison.OrdinalIgnoreCase))
                {
                    var install = File.Exists(Path.Combine(work, "package-lock.json"))
                        ? "npm ci --no-audit --no-fund"
                        : "npm install --no-audit --no-fund";
                    progress?.Report("Installing web dependencies in the temporary copy...");
                    await _processRunner.RunToCompletionAsync(
                        install, work, progress, TimeSpan.FromMinutes(10), cancellationToken);
                }

                var command = string.IsNullOrWhiteSpace(customCommand)
                    ? analysis.SuggestedRunCommand
                    : customCommand.Trim();
                if (string.IsNullOrWhiteSpace(command))
                    throw new InvalidOperationException("No web start command was detected. Enter one in Screenshot settings.");

                targetUrl = string.IsNullOrWhiteSpace(customUrl)
                    ? analysis.SuggestedPreviewUrl ?? string.Empty
                    : customUrl.Trim();
                if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
                    throw new InvalidOperationException("Enter a valid local Preview URL, for example http://127.0.0.1:5173.");

                var environment = analysis.ProjectType.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase)
                    ? new Dictionary<string, string> { ["ASPNETCORE_URLS"] = targetUrl }
                    : null;
                progress?.Report($"Starting preview: {command}");
                server = _processRunner.StartBackground(command, work, progress, environment);
                await WaitForUrlAsync(targetUrl, server, progress, cancellationToken);
            }

            progress?.Report("Capturing the real interface with Microsoft Edge...");
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "msedge",
                Headless = true
            });
            var page = await browser.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                DeviceScaleFactor = 1
            });
            await page.GotoAsync(targetUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });
            await page.WaitForTimeoutAsync(1500);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = destination,
                FullPage = true,
                Type = ScreenshotType.Png
            });
        }
        finally
        {
            ProcessRunner.TryKill(server);
            server?.Dispose();
        }
    }

    private async Task CaptureDesktopAsync(
        ProjectAnalysis analysis,
        string work,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(analysis.PrimaryProjectFile))
            throw new InvalidOperationException("No desktop project file was detected.");

        var projectPath = Path.Combine(work, analysis.PrimaryProjectFile);
        progress?.Report("Building the desktop app in the temporary copy...");
        await _processRunner.RunToCompletionAsync(
            $"dotnet build \"{projectPath}\" -c Release --nologo",
            work,
            progress,
            TimeSpan.FromMinutes(10),
            cancellationToken);

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var expectedName = Path.GetFileNameWithoutExtension(projectPath) + ".exe";
        var executable = Directory.EnumerateFiles(Path.Combine(projectDirectory, "bin", "Release"), "*.exe", SearchOption.AllDirectories)
            .OrderByDescending(path => Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (executable is null)
            throw new FileNotFoundException("The desktop build completed, but no runnable .exe was found.");

        progress?.Report($"Launching isolated preview: {Path.GetFileName(executable)}");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not launch the desktop preview.");

        try
        {
            var handle = await WaitForMainWindowAsync(process, cancellationToken);
            await Task.Delay(800, cancellationToken);
            if (!GetWindowRect(handle, out var rectangle))
                throw new InvalidOperationException("Could not read the desktop window bounds.");

            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            if (width <= 20 || height <= 20)
                throw new InvalidOperationException("The desktop app window has invalid dimensions.");

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(rectangle.Left, rectangle.Top, 0, 0, new System.Drawing.Size(width, height));
            bitmap.Save(destination, ImageFormat.Png);
        }
        finally
        {
            ProcessRunner.TryKill(process);
        }
    }

    private static async Task WaitForUrlAsync(
        string url,
        Process process,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        }) { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"The preview server stopped with exit code {process.ExitCode}.");
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if ((int)response.StatusCode < 500) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Server is still starting.
            }
            progress?.Report("Waiting for the local preview server...");
            await Task.Delay(1500, cancellationToken);
        }
        throw new TimeoutException("The local preview URL did not become ready within two minutes.");
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException("The desktop preview closed before a window appeared.");
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("No desktop window appeared within 45 seconds.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rectangle);
}
