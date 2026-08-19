using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ProjectPublisher.Models;
using ProjectPublisher.Services;

namespace ProjectPublisher;

public partial class MainWindow : Window
{
    private readonly SecretScanner _secretScanner = new();
    private readonly SettingsService _settingsService = new();
    private readonly CredentialVault _credentialVault = new();
    private readonly GitHubApiService _gitHubApi = new();

    private AppSettings _settings = new();
    private GitHubAuthService _auth = null!;
    private ProjectAnalyzer _analyzer = null!;
    private ScreenshotService _screenshots = null!;
    private GitCommandService _gitCommands = null!;
    private GitCliPublishService _gitPublisher = null!;
    private TokenBundle? _token;
    private GitHubUser? _user;
    private ProjectAnalysis? _analysis;
    private string? _screenshotPath;
    private CancellationTokenSource? _operationCts;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        var staging = new StagingService(_secretScanner);
        _auth = new GitHubAuthService(_credentialVault);
        _analyzer = new ProjectAnalyzer(_secretScanner);
        _screenshots = new ScreenshotService(staging, new ProcessRunner());
        _gitCommands = new GitCommandService(_secretScanner);
        _gitPublisher = new GitCliPublishService(_gitCommands, _gitHubApi, new ReadmeGenerator());

        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            _operationCts?.Cancel();
            TryDeleteGeneratedPreview();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AppPaths.CleanupAbandonedWorkspaces();
        _settings = await _settingsService.LoadAsync();
        ClientIdBox.Text = _settings.GitHubClientId;
        FolderBox.Text = _settings.LastProjectFolder;
        OwnerBox.Text = _settings.LastOwner;
        ScreenshotCommandBox.Text = _settings.ScreenshotCommand;
        PreviewUrlBox.Text = _settings.PreviewUrl;
        GenerateReadmeCheck.IsChecked = _settings.GenerateReadme;
        ReplaceOriginCheck.IsChecked = _settings.ReplaceOrigin;
        BranchNameBox.Text = string.IsNullOrWhiteSpace(_settings.DefaultBranch) ? "main" : _settings.DefaultBranch;
        CommitMessageBox.Text = string.IsNullOrWhiteSpace(_settings.LastCommitMessage)
            ? "Publish project update"
            : _settings.LastCommitMessage;
        OpenAfterPublishCheck.IsChecked = _settings.OpenRepositoryAfterPublish;
        Log("NEON GIT engine booted. DIRECT MODE is active.");
        Log("Protected files remain local; force-push and silent remote replacement are disabled.");

        try
        {
            var version = await _gitCommands.CheckVersionAsync(null, CancellationToken.None);
            Log($"Git engine detected: {version.StandardOutput}");
        }
        catch (Exception ex)
        {
            Log($"Git preflight warning: {ex.Message}");
            StatusText.Text = "Git for Windows required";
        }

        if (!string.IsNullOrWhiteSpace(_settings.GitHubClientId))
        {
            try
            {
                _token = await _auth.GetSavedValidTokenAsync(_settings.GitHubClientId, CancellationToken.None);
                if (_token is not null)
                {
                    _user = await _gitHubApi.GetCurrentUserAsync(_token.AccessToken, CancellationToken.None);
                    SetConnectedState();
                    Log($"Secure GitHub session restored for {_user.Login}.");
                }
            }
            catch (Exception ex)
            {
                Log($"Saved GitHub session could not be restored: {ex.Message}");
            }
        }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var clientId = ClientIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            MessageBox.Show(this,
                "Enter the Client ID from your GitHub OAuth App and enable Device Flow in its settings.",
                "Client ID required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DeviceLoginWindow? loginWindow = null;
        try
        {
            BeginOperation("Starting GitHub device authorization...");
            _settings.GitHubClientId = clientId;
            await SaveSettingsAsync();
            var deviceCode = await _auth.BeginDeviceFlowAsync(clientId, _operationCts!.Token);
            loginWindow = new DeviceLoginWindow(deviceCode) { Owner = this };
            loginWindow.Show();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _operationCts.Token, loginWindow.Cancellation.Token);
            _token = await _auth.CompleteDeviceFlowAsync(clientId, deviceCode, linked.Token);
            _user = await _gitHubApi.GetCurrentUserAsync(_token.AccessToken, linked.Token);
            loginWindow.MarkCompleted();
            loginWindow.Close();
            SetConnectedState();
            OwnerBox.Text = _user.Login;
            Log($"GitHub linked as {_user.Login}.");
            StatusText.Text = "GitHub linked";
        }
        catch (OperationCanceledException)
        {
            Log("GitHub authorization cancelled.");
        }
        catch (Exception ex)
        {
            loginWindow?.Close();
            ShowError("GitHub sign-in failed", ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            _auth.SignOut();
            _token = null;
            _user = null;
            ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(241, 169, 78));
            ConnectionText.Text = "GitHub not connected";
            ConnectButton.Content = "Connect GitHub";
            DisconnectButton.IsEnabled = false;
            PublishButton.IsEnabled = false;
            Log("OAuth credential removed from Windows Credential Manager.");
        }
        catch (Exception ex)
        {
            ShowError("Sign out failed", ex);
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a project workspace",
            Multiselect = false,
            InitialDirectory = Directory.Exists(FolderBox.Text) ? FolderBox.Text : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
            _analysis = null;
            PublishButton.IsEnabled = false;
            StatusText.Text = "Workspace selected — run security scan";
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            BeginOperation("Scanning workspace and Git state...");
            _analysis = await _analyzer.AnalyzeAsync(FolderBox.Text, _operationCts!.Token);
            DisplayAnalysis(_analysis);
            RepoNameBox.Text = _analysis.SuggestedRepositoryName;
            DescriptionBox.Text = _analysis.SuggestedDescription;
            if (string.IsNullOrWhiteSpace(OwnerBox.Text) && _user is not null) OwnerBox.Text = _user.Login;
            ScreenshotCommandBox.Text = _analysis.SuggestedRunCommand ?? string.Empty;
            PreviewUrlBox.Text = _analysis.SuggestedPreviewUrl ?? string.Empty;
            CommitMessageBox.Text = Directory.Exists(Path.Combine(_analysis.SourcePath, ".git"))
                ? "Update project files"
                : "Initial secure project publish";
            PublishButton.IsEnabled = _user is not null;

            _settings.LastProjectFolder = _analysis.SourcePath;
            await SaveSettingsAsync();
            var protectedCount = ProtectedCount(_analysis);
            Log($"Scan complete: {_analysis.FileCount} candidate files, {protectedCount} local-only protected path(s).");

            try
            {
                var state = await _gitPublisher.StatusAsync(_analysis.SourcePath, CreateProgress(), _operationCts.Token);
                Log(state);
            }
            catch (InvalidOperationException)
            {
                Log("No local Git repository yet. The publish pipeline will run git init.");
            }
            StatusText.Text = "Workspace scan complete";
        }
        catch (OperationCanceledException)
        {
            Log("Workspace scan cancelled.");
        }
        catch (Exception ex)
        {
            ShowError("Workspace analysis failed", ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void GitStatus_Click(object sender, RoutedEventArgs e) =>
        await RunGitUtilityAsync("Reading Git status...", async (progress, token) =>
        {
            var status = await _gitPublisher.StatusAsync(FolderBox.Text, progress, token);
            Log(status);
        });

    private async void Fetch_Click(object sender, RoutedEventArgs e) =>
        await RunAuthenticatedGitUtilityAsync("Fetching origin...", (progress, token) =>
            _gitPublisher.FetchAsync(FolderBox.Text, _user!, _token!.AccessToken, progress, token));

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        var approval = MessageBox.Show(this,
            "This runs git pull --rebase for the selected branch. It will stop and report if conflicts occur. Continue?",
            "Confirm safe pull", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (approval != MessageBoxResult.Yes) return;
        await RunAuthenticatedGitUtilityAsync("Pulling with rebase...", (progress, token) =>
            _gitPublisher.PullRebaseAsync(
                FolderBox.Text,
                string.IsNullOrWhiteSpace(BranchNameBox.Text) ? "main" : BranchNameBox.Text.Trim(),
                _user!, _token!.AccessToken, progress, token));
    }

    private async void History_Click(object sender, RoutedEventArgs e) =>
        await RunGitUtilityAsync("Loading commit history...", async (progress, token) =>
        {
            var history = await _gitPublisher.HistoryAsync(FolderBox.Text, progress, token);
            if (!string.IsNullOrWhiteSpace(history)) Log(history);
        });

    private async Task RunAuthenticatedGitUtilityAsync(
        string status,
        Func<IProgress<string>, CancellationToken, Task> operation)
    {
        if (_user is null || _token is null)
        {
            MessageBox.Show(this, "Connect GitHub first.", "GitHub connection required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RunGitUtilityAsync(status, operation);
    }

    private async Task RunGitUtilityAsync(
        string status,
        Func<IProgress<string>, CancellationToken, Task> operation)
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(FolderBox.Text) || !Directory.Exists(FolderBox.Text))
        {
            MessageBox.Show(this, "Select a valid project workspace first.", "Workspace required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            BeginOperation(status);
            await operation(CreateProgress(), _operationCts!.Token);
            StatusText.Text = "Git operation complete";
        }
        catch (OperationCanceledException)
        {
            Log("Git operation cancelled.");
        }
        catch (Exception ex)
        {
            ShowError("Git operation failed", ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_analysis is null)
        {
            MessageBox.Show(this, "Scan the workspace first.", "Workspace scan required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var consent = MessageBox.Show(this,
            "Automatic capture builds/runs a sanitized temporary copy. This is not an OS sandbox; run only trusted project code. Continue?",
            "Run trusted project for screenshot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (consent != MessageBoxResult.Yes) return;

        try
        {
            BeginOperation("Capturing actual project interface...");
            var capturedPath = await _screenshots.CaptureAsync(
                _analysis,
                ScreenshotCommandBox.Text,
                PreviewUrlBox.Text,
                CreateProgress(),
                _operationCts!.Token);
            TryDeleteGeneratedPreview();
            _screenshotPath = capturedPath;
            LoadPreview(_screenshotPath);
            Log("Actual interface captured. It will be copied to images/project-preview.png during publish.");
            StatusText.Text = "Screenshot ready — review before publish";
        }
        catch (OperationCanceledException)
        {
            Log("Screenshot capture cancelled.");
        }
        catch (Exception ex)
        {
            ShowError("Automatic screenshot failed", ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private void SelectImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a project interface screenshot",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            TryDeleteGeneratedPreview();
            _screenshotPath = dialog.FileName;
            LoadPreview(_screenshotPath);
            Log("Manual screenshot selected; review visible data before publishing.");
        }
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        TryDeleteGeneratedPreview();
        _screenshotPath = null;
        PreviewImage.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }

    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_analysis is null || _user is null || _token is null)
        {
            MessageBox.Show(this, "Connect GitHub and scan a workspace first.", "Pipeline not ready",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var owner = OwnerBox.Text.Trim();
        var repositoryName = RepoNameBox.Text.Trim();
        var protectedCount = ProtectedCount(_analysis);
        var originWarning = ReplaceOriginCheck.IsChecked == true
            ? "\n\nORIGIN REPLACEMENT IS ENABLED. A different existing origin may be changed after validation."
            : string.Empty;
        var confirmation = MessageBox.Show(this,
            $"Execute Git pipeline for {owner}/{repositoryName}?\n\n" +
            "• git init/status/add/commit/branch/remote/push will run in the selected folder\n" +
            $"• {protectedCount} sensitive or unsafe path(s) will remain local and be removed from the Git index\n" +
            "• .git metadata will be created/updated; optional README and image may be added\n" +
            "• OAuth token will not appear in commands, remote URL, or terminal logs\n" +
            "• Force-push is disabled" + originWarning,
            "Confirm command pipeline", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            BeginOperation("Executing secure Git command pipeline...");
            await SaveSettingsAsync();
            // Re-scan immediately before staging so newly-added secrets are protected.
            _analysis = await _analyzer.AnalyzeAsync(FolderBox.Text, _operationCts!.Token);
            DisplayAnalysis(_analysis);
            var result = await _gitPublisher.PublishAsync(
                _analysis,
                new GitCliPublishOptions
                {
                    Owner = owner,
                    RepositoryName = repositoryName,
                    Description = DescriptionBox.Text.Trim(),
                    CommitMessage = CommitMessageBox.Text.Trim(),
                    BranchName = string.IsNullOrWhiteSpace(BranchNameBox.Text) ? "main" : BranchNameBox.Text.Trim(),
                    IsPrivate = PrivateRepoCheck.IsChecked == true,
                    GenerateReadmeWhenMissing = GenerateReadmeCheck.IsChecked == true,
                    ReplaceOrigin = ReplaceOriginCheck.IsChecked == true,
                    ScreenshotPath = _screenshotPath
                },
                _user,
                _token.AccessToken,
                CreateProgress(),
                _operationCts.Token);

            var action = result.RepositoryCreated ? "created and pushed" : "updated and pushed";
            Log($"PIPELINE COMPLETE // {action} // commit {ShortSha(result.CommitSha)} // local-only paths {result.ExcludedFiles}");
            StatusText.Text = $"Push complete — {result.RepositoryUrl}";
            MessageBox.Show(this,
                $"Repository {action}.\n\n{result.RepositoryUrl}\n\n" +
                $"Commit: {ShortSha(result.CommitSha)}\nProtected local-only paths: {result.ExcludedFiles}",
                "Git pipeline complete", MessageBoxButton.OK, MessageBoxImage.Information);
            if (OpenAfterPublishCheck.IsChecked == true)
                Process.Start(new ProcessStartInfo(result.RepositoryUrl) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            Log("Pipeline cancelled. Completed Git steps are not automatically rolled back.");
        }
        catch (Exception ex)
        {
            ShowError("GitHub pipeline failed", ex);
        }
        finally
        {
            EndOperation();
        }
    }

    private void DisplayAnalysis(ProjectAnalysis analysis)
    {
        FindingsGrid.ItemsSource = analysis.Findings.Select(f =>
            f.Severity is FindingSeverity.Warning or FindingSeverity.Critical
                ? f with { Action = "Keep local; remove from Git index" }
                : f).ToArray();
        AnalysisSubtitle.Text = analysis.SourcePath;
        TypeStat.Text = analysis.ProjectType;
        FilesStat.Text = analysis.FileCount.ToString("N0");
        SizeStat.Text = analysis.SizeText;
        FindingsStat.Text = ProtectedCount(analysis).ToString("N0");
        TechnologyText.Text = analysis.Languages.Count == 0
            ? "No primary source language detected."
            : "Detected: " + string.Join("  •  ", analysis.Languages.Take(8).Select(x => $"{x.Key} ({x.Value})"));
    }

    private IProgress<string> CreateProgress() => new Progress<string>(message =>
    {
        Log(message);
        StatusText.Text = message.Length > 110 ? message[..110] + "…" : message;
    });

    private static int ProtectedCount(ProjectAnalysis analysis) =>
        analysis.Findings
            .Where(f => f.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
            .Select(f => f.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operationCts?.Cancel();

    private void BeginOperation(string status)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _busy = true;
        BusyBar.IsIndeterminate = true;
        CancelButton.IsEnabled = true;
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        AnalyzeButton.IsEnabled = false;
        CaptureButton.IsEnabled = false;
        PublishButton.IsEnabled = false;
        GitStatusButton.IsEnabled = false;
        FetchButton.IsEnabled = false;
        PullButton.IsEnabled = false;
        HistoryButton.IsEnabled = false;
        StatusText.Text = status;
        Log(status);
    }

    private void EndOperation()
    {
        _busy = false;
        BusyBar.IsIndeterminate = false;
        CancelButton.IsEnabled = false;
        ConnectButton.IsEnabled = true;
        AnalyzeButton.IsEnabled = true;
        CaptureButton.IsEnabled = true;
        GitStatusButton.IsEnabled = true;
        HistoryButton.IsEnabled = true;
        FetchButton.IsEnabled = _user is not null;
        PullButton.IsEnabled = _user is not null;
        DisconnectButton.IsEnabled = _user is not null;
        PublishButton.IsEnabled = _user is not null && _analysis is not null;
        _operationCts?.Dispose();
        _operationCts = null;
    }

    private void SetConnectedState()
    {
        if (_user is null) return;
        ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(57, 213, 197));
        ConnectionText.Text = $"LINKED // {_user.Login}";
        ConnectButton.Content = "Reconnect";
        DisconnectButton.IsEnabled = true;
        FetchButton.IsEnabled = true;
        PullButton.IsEnabled = true;
        PublishButton.IsEnabled = _analysis is not null;
    }

    private void TryDeleteGeneratedPreview()
    {
        if (string.IsNullOrWhiteSpace(_screenshotPath)) return;
        try
        {
            var previewRoot = Path.GetFullPath(AppPaths.PreviewRoot)
                              .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(_screenshotPath);
            if (candidate.StartsWith(previewRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                File.Delete(candidate);
        }
        catch { /* Preview cleanup is retried by age on a future startup. */ }
    }

    private void LoadPreview(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    private async Task SaveSettingsAsync()
    {
        _settings.GitHubClientId = ClientIdBox.Text.Trim();
        _settings.LastProjectFolder = FolderBox.Text;
        _settings.LastOwner = OwnerBox.Text.Trim();
        _settings.ScreenshotCommand = ScreenshotCommandBox.Text.Trim();
        _settings.PreviewUrl = PreviewUrlBox.Text.Trim();
        _settings.GenerateReadme = GenerateReadmeCheck.IsChecked == true;
        _settings.ReplaceOrigin = ReplaceOriginCheck.IsChecked == true;
        _settings.DefaultBranch = BranchNameBox.Text.Trim();
        _settings.LastCommitMessage = CommitMessageBox.Text.Trim();
        _settings.OpenRepositoryAfterPublish = OpenAfterPublishCheck.IsChecked == true;
        await _settingsService.SaveAsync(_settings);
    }

    private void Log(string message)
    {
        var safeMessage = _secretScanner.RedactContent(message).Content;
        ActivityLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {safeMessage}{Environment.NewLine}");
        ActivityLog.ScrollToEnd();
    }

    private void ShowError(string title, Exception exception)
    {
        var safeMessage = _secretScanner.RedactContent(exception.Message).Content;
        Log($"ERROR // {title}: {safeMessage}");
        StatusText.Text = title;
        MessageBox.Show(this, safeMessage, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string ShortSha(string sha) => string.IsNullOrWhiteSpace(sha)
        ? "n/a"
        : sha[..Math.Min(7, sha.Length)];
}
