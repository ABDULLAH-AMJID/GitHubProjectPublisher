# NEON GIT // Project Publisher v0.2

A native Windows GitHub publishing application with a professional cyberpunk WPF interface and a real command-driven Git backend.

Select a project folder, review its security report, enter repository details, and execute the same safe workflow you would normally type in a terminal:

```text
git init
git status
git add --all
git commit -m "..."
git remote add/set-url origin ...
git branch -M main
git push --set-upstream origin main
```

The live terminal panel streams every non-secret command, warning, and result.

## Product mode

Version 0.2 uses **Direct Workspace Mode**:

- `.git` is created/updated inside the selected project.
- Local Git identity is configured only when the repository does not already have one.
- `.gitattributes` is generated when missing to normalize line endings.
- Security rules are appended to `.gitignore` without replacing existing rules.
- Value-free `.env.example` files are generated when missing.
- A README and reviewed screenshot can optionally be added to the project.
- Secret-bearing source files themselves are not edited or deleted.

This differs from the older staging-only prototype: direct mode intentionally creates Git metadata and project support files in the selected folder.

## Security behavior

Before `git add`, Project Publisher scans the workspace. `.env`, private keys, certificates, keystores, credential files, oversized files, and files containing detected secret values become **local-only protected paths**.

For every protected path the app:

1. Adds a local rule to `.git/info/exclude`.
2. Runs `git rm --cached -f --ignore-unmatch -- <path>` so an already-tracked copy is removed from the next commit while the working file remains.
3. Excludes it from the commit.
4. Displays the path and safe action without displaying the matched secret.

The original secret value is never rewritten. Repository-wide rules for common credential files are committed through `.gitignore`.

> Secret scanning is defense-in-depth. Do not manually run `git add -f` on protected files. If a credential was committed previously, revoke/rotate it and clean Git history; a later deletion does not erase old commits.

## Features

- Native C# / .NET 8 WPF desktop application
- Dark glass cyberpunk UI with cyan/violet status accents
- Real `git.exe` command execution with structured argument lists
- Live stdout/stderr terminal and cancellable processes
- GitHub OAuth Device Flow
- OAuth token in Windows Credential Manager
- GitHub repository lookup, creation, and description update through REST API
- Git init/status/stage/commit/branch/remote/push pipeline
- Status, fetch, safe pull-with-rebase, and 25-entry history actions
- Public/private new repositories
- Existing origin validation; replacement requires explicit approval
- No automatic force-push, hard reset, or destructive conflict resolution
- Direct push authentication held in process memory; token is not placed in arguments, remote URLs, settings, or terminal logs
- Actual web/desktop interface screenshots
- README generation when missing
- `.env.example` generation with placeholders only
- x64 and ARM64 self-contained builds
- Inno Setup definition and Windows GitHub Actions workflow

## Requirements

### Running the app

- Windows 10 2004+ or Windows 11
- [Git for Windows](https://git-scm.com/download/win) available as `git.exe` on PATH
- Microsoft Edge for automatic web screenshots
- Project-specific build tools only when automatic screenshot capture needs them (for example Node/npm or a matching .NET SDK)

### Building the app

- .NET 8 SDK
- PowerShell

Check prerequisites:

```powershell
git --version
dotnet --version
```

## Build

```powershell
cd GitHubProjectPublisher
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\build.ps1
```

Output:

```text
artifacts\win-x64\ProjectPublisher.exe
```

ARM64:

```powershell
.\build.ps1 -Runtime win-arm64
```

## GitHub OAuth setup

1. Open `https://github.com/settings/developers`.
2. Select **OAuth Apps → New OAuth App**.
3. Use:
   - Application name: `NEON GIT Project Publisher`
   - Homepage URL: your GitHub profile or product repository URL
   - Authorization callback URL: `http://127.0.0.1/`
4. Enable **Device Flow**.
5. Register and copy the **Client ID**.
6. Paste only that Client ID into Project Publisher and click **Connect GitHub**.

Never embed a Client Secret in this desktop app. The current device flow requests `repo read:user` so it can create/update public and private repositories and identify the commit author.

## Publishing workflow

1. Connect GitHub.
2. Select the exact project root.
3. Click **Scan workspace + detect Git**.
4. Review local-only paths in the security table.
5. Set owner, repository name, description, branch, and commit message.
6. Optionally capture/select an interface screenshot.
7. Click **EXECUTE SECURE GIT PIPELINE**.
8. Review the confirmation showing direct-mode filesystem and Git effects.
9. Watch real commands in **LIVE GIT TERMINAL**.

For a new folder the app performs approximately:

```text
git --version
git rev-parse --show-toplevel
git init
git rm --cached -f --ignore-unmatch -- <protected paths>
git status --short
git add --all -- .
git diff --cached --quiet
git config --local user.name ...       # only when missing
git config --local user.email ...      # only when missing
git commit -m "..."
git remote add origin ...              # or validated set-url
git branch -M main
git push --set-upstream origin main
```

Repository creation occurs through the GitHub API before the remote/push stage.

## Safe update tools

- **Status:** `git status --short --branch`
- **Fetch:** `git fetch origin --prune`
- **Pull:** `git pull --rebase origin <branch>` after explicit confirmation
- **History:** `git log -25`

Pull stops on conflicts. The app does not auto-resolve, reset, or force-push.

## LF/CRLF warnings

Warnings such as “LF will be replaced by CRLF” are not Git failures. When no custom `.gitattributes` exists, Project Publisher creates a conservative policy:

```gitattributes
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
```

Existing `.gitattributes` is never replaced.

## Command security design

- `git.exe` is launched directly; the app does not build a `cmd.exe /c` string.
- Every argument is supplied through `ProcessStartInfo.ArgumentList`.
- Folder paths and commit messages therefore remain separate arguments, including paths containing spaces.
- Push authentication is supplied as ephemeral process environment configuration, not in the displayed command or Git remote.
- `GIT_TERMINAL_PROMPT=0` prevents invisible credential prompts from hanging the UI.
- Output passes through the secret redactor before display.
- Cancellation terminates the entire child process tree.

## Screenshot warning

Automatic screenshots run a sanitized temporary project copy so builds do not contaminate the selected workspace. This is not an OS sandbox: trusted project code still runs with the current Windows user's permissions.

## Project structure

```text
src/ProjectPublisher.App/
  MainWindow.xaml                       Cyberpunk native UI
  Services/GitCommandService.cs        Secure git.exe process runner
  Services/GitCliPublishService.cs     Direct command pipeline
  Services/GitHubAuthService.cs        OAuth Device Flow
  Services/GitHubApiService.cs         Repository API
  Services/CredentialVault.cs          Windows Credential Manager
  Services/SecretScanner.cs            Secret/file policy
  Services/ScreenshotService.cs        Actual UI capture
  Services/ReadmeGenerator.cs           Documentation generation

tests/ProjectPublisher.Tests/
  SecurityPipelineTests.cs             Sanitized-copy tests
  GitCommandPipelineTests.cs           Real local Git init/commit/push test
```

See [SECURITY.md](SECURITY.md), [architecture.md](docs/architecture.md), and [VALIDATION.md](VALIDATION.md).
