# NEON GIT Project Publisher — Complete Beginner Guide

**Applies to:** NEON GIT Project Publisher v0.2.2  
**Operating system:** Windows 10 or Windows 11  
**Purpose:** Publish and update local project folders on GitHub through a native graphical application

---

## Table of contents

1. [What this application does](#1-what-this-application-does)
2. [Basic Git terms for beginners](#2-basic-git-terms-for-beginners)
3. [Before you begin](#3-before-you-begin)
4. [Install Git for Windows](#4-install-git-for-windows)
5. [Install the .NET 8 SDK](#5-install-the-net-8-sdk)
6. [Extract and build NEON GIT](#6-extract-and-build-neon-git)
7. [Create a GitHub OAuth App](#7-create-a-github-oauth-app)
8. [Connect your GitHub account](#8-connect-your-github-account)
9. [Understand the application interface](#9-understand-the-application-interface)
10. [Publish your first project](#10-publish-your-first-project)
11. [Understand the security report](#11-understand-the-security-report)
12. [Add a real project screenshot](#12-add-a-real-project-screenshot)
13. [Update an existing project](#13-update-an-existing-project)
14. [Use Status, Fetch, Pull, and History](#14-use-status-fetch-pull-and-history)
15. [Work with an existing GitHub repository](#15-work-with-an-existing-github-repository)
16. [Understand files created by the app](#16-understand-files-created-by-the-app)
17. [Troubleshooting](#17-troubleshooting)
18. [Safe conflict recovery](#18-safe-conflict-recovery)
19. [Sign out, reset, or uninstall](#19-sign-out-reset-or-uninstall)
20. [Beginner safety checklist](#20-beginner-safety-checklist)
21. [Frequently asked questions](#21-frequently-asked-questions)

---

# 1. What this application does

NEON GIT Project Publisher is a native Windows user interface for common Git and GitHub operations.

Instead of manually typing all of these commands:

```text
git init
git status
git add --all
git commit -m "Initial commit"
git remote add origin https://github.com/USERNAME/REPOSITORY.git
git branch -M main
git push --set-upstream origin main
```

you can:

1. Select a project folder.
2. Scan it for sensitive files.
3. Enter repository details.
4. Click one publish button.
5. Watch each real Git command in the live terminal panel.

The app also provides buttons for:

- Git Status
- Fetch
- Pull with rebase
- Commit history
- New GitHub repository creation
- Existing repository updates
- README generation
- Actual interface screenshots
- Secret and `.env` protection

## Important: Direct Workspace Mode

The app works directly inside the selected project folder. It intentionally creates and updates Git-related files there.

It may create:

```text
.git/
.gitignore
.gitattributes
.env.example
README.md
images/project-preview.png
```

It does **not** rewrite or delete your `.env` file or source file containing a detected secret. Such files are kept local and removed from the Git index.

---

# 2. Basic Git terms for beginners

Understanding these terms will make the app easier to use.

## Project folder

The folder on your computer containing your source code and project files.

Example:

```text
E:\personal tools\C_drive_cleanup
```

## Local repository

A project folder that contains a hidden `.git` directory. This directory stores local Git history and configuration.

## GitHub repository

The online copy of the project on GitHub.

Example:

```text
https://github.com/ABDULLAH-AMJID/SpaceMedic
```

## Commit

A saved snapshot of staged project changes.

Example commit message:

```text
Add project security documentation
```

## Branch

A development line. This app uses `main` by default.

## Stage

Selecting changes that should be included in the next commit. The app performs this with:

```text
git add --all
```

Protected files are removed from the stage before the commit.

## Push

Sending local commits to GitHub.

## Fetch

Downloading information about remote commits without changing your current files.

## Pull

Downloading remote commits and applying them to your current branch.

## Origin

The standard name for the main remote GitHub repository.

## OAuth

A secure authorization system. You approve the app in your browser without giving the app your GitHub password.

---

# 3. Before you begin

You need:

- Windows 10 or Windows 11
- A GitHub account
- An internet connection
- Git for Windows
- .NET 8 SDK for building the app
- Microsoft Edge for automatic web screenshots

## Check Windows architecture

Most Intel and AMD computers use x64.

In PowerShell:

```powershell
$env:PROCESSOR_ARCHITECTURE
```

Common results:

```text
AMD64   = use win-x64
ARM64   = use win-arm64
```

## Open PowerShell

1. Press the Windows key.
2. Type `PowerShell` or `Windows Terminal`.
3. Open it normally.
4. Administrator mode is usually not required for building or running this app.

---

# 4. Install Git for Windows

The app executes the real installed `git.exe`, so Git for Windows is required.

Official download:

- [Git for Windows](https://git-scm.com/install/windows)

Official command-line installation:

```powershell
winget install --id Git.Git -e --source winget
```

The official Git site documents this WinGet package and provides x64 and ARM64 installers.

## Installer choices

For a beginner, the default installer options are normally suitable. Make sure Git is available from the command line or third-party software.

After installation:

1. Close PowerShell completely.
2. Open a new PowerShell window.
3. Run:

```powershell
git --version
```

Expected result:

```text
git version 2.x.x.windows.x
```

Also check its path:

```powershell
where.exe git
```

A normal result looks similar to:

```text
C:\Program Files\Git\cmd\git.exe
```

If `git` is not recognized, restart Windows or reinstall Git with command-line integration enabled.

---

# 5. Install the .NET 8 SDK

You need the **SDK**, not only the Runtime, to build the application.

Official download:

- [.NET 8 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

## Install using WinGet

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget --accept-package-agreements --accept-source-agreements
```

Or download the Windows x64/ARM64 **SDK installer** from the official page.

After installation:

1. Close all PowerShell windows.
2. Open a new PowerShell window.
3. Run:

```powershell
dotnet --version
```

Expected result:

```text
8.0.xxx
```

See all installed SDKs:

```powershell
dotnet --list-sdks
```

Check the executable path:

```powershell
where.exe dotnet
```

A normal location is:

```text
C:\Program Files\dotnet\dotnet.exe
```

---

# 6. Extract and build NEON GIT

## Step 1: Extract the ZIP

Extract:

```text
NeonGit-ProjectPublisher-v0.2.2.zip
```

Use a simple location such as:

```text
E:\personal tools\GitHubProjectPublisher
```

Paths containing spaces are supported.

## Step 2: Open the project folder

```powershell
cd "E:\personal tools\GitHubProjectPublisher"
```

Quotes are important when a path contains spaces.

Confirm the files:

```powershell
Get-ChildItem
```

You should see:

```text
GitHubProjectPublisher.sln
build.ps1
src
README.md
SECURITY.md
```

## Step 3: Unblock the downloaded script

```powershell
Unblock-File -LiteralPath .\build.ps1
```

## Step 4: Allow the script only in this PowerShell window

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
```

This does not permanently weaken the system policy. It ends when the PowerShell window closes.

## Step 5: Build

```powershell
.\build.ps1
```

The script will:

1. Check that Git exists.
2. Check that the .NET SDK exists.
3. Restore NuGet packages.
4. Compile the solution.
5. Run Windows tests.
6. Restore the selected Windows runtime pack.
7. Publish a self-contained Windows executable.

The first build can take several minutes because dependencies are downloaded.

## Step 6: Find the executable

For x64:

```text
E:\personal tools\GitHubProjectPublisher\artifacts\win-x64\ProjectPublisher.exe
```

Run it:

```powershell
.\artifacts\win-x64\ProjectPublisher.exe
```

For ARM64:

```powershell
.\build.ps1 -Runtime win-arm64
```

## Important Playwright note

Do **not** run `playwright.ps1` from the self-contained publish folder. Version 0.2.1 removes that script from publish output because it expects a loose `Microsoft.Playwright.dll` while this application bundles Playwright inside the executable.

For normal use, run only:

```powershell
.\ProjectPublisher.exe
```

The app uses installed Microsoft Edge for web screenshots.

---

# 7. Create a GitHub OAuth App

An OAuth App gives NEON GIT a GitHub Client ID and lets users authorize it through GitHub's browser page.

Official GitHub guide:

- [Creating an OAuth App](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app)
- [Authorizing OAuth Apps with Device Flow](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps)

## Step 1: Sign in to GitHub

Open:

```text
https://github.com
```

Sign in to the account that will own or manage repositories.

## Step 2: Open Developer Settings

Direct link:

```text
https://github.com/settings/developers
```

Or navigate manually:

1. Click your profile picture.
2. Click **Settings**.
3. Scroll down the left sidebar.
4. Click **Developer settings**.
5. Click **OAuth Apps**.

## Step 3: Create the OAuth App

Click:

```text
New OAuth App
```

If this is your first app, GitHub may show:

```text
Register a new application
```

## Step 4: Fill the form

Use these beginner-friendly values:

| Field | Value |
|---|---|
| Application name | `NEON GIT Project Publisher` |
| Homepage URL | `https://github.com/YOUR-USERNAME` |
| Application description | `Native Windows app for securely publishing local projects to GitHub.` |
| Authorization callback URL | `http://127.0.0.1/` |

Replace `YOUR-USERNAME` with your actual GitHub username.

Example:

```text
https://github.com/ABDULLAH-AMJID
```

Device Flow does not use the callback for the login result, but GitHub's OAuth App registration still asks for a callback URL.

## Step 5: Enable Device Flow

Find and enable:

```text
Enable Device Flow
```

This is essential. If it is disabled, the app will receive:

```text
device_flow_disabled
```

## Step 6: Keep expiring tokens enabled

GitHub may show:

```text
Expire user access tokens
```

You can leave it enabled. NEON GIT supports refresh-token data returned by the Device Flow.

## Step 7: Register

Click:

```text
Register application
```

## Step 8: Copy the correct identifier

Copy the value labelled:

```text
Client ID
```

It may look similar to:

```text
Ov23liXXXXXXXXXXXXXXXXXX
```

Do not confuse these values:

| Value | Use it in NEON GIT? |
|---|---|
| Client ID | Yes |
| Client Secret | No |
| App ID | No |
| Device code | Only on GitHub's temporary login page |

### Never embed a Client Secret

A desktop app cannot securely hide a permanent Client Secret. NEON GIT's Device Flow only needs the Client ID.

---

# 8. Connect your GitHub account

## Step 1: Start the app

```powershell
.\artifacts\win-x64\ProjectPublisher.exe
```

## Step 2: Find GitHub connection

In card **1 — GitHub connection**, find:

```text
OAuth App Client ID
```

Paste the Client ID copied from GitHub.

## Step 3: Click Connect GitHub

Click:

```text
Connect GitHub
```

The app requests a device code from GitHub.

## Step 4: Use the device code

A window will show a code similar to:

```text
ABCD-1234
```

The code is copied automatically when possible. The app opens:

```text
https://github.com/login/device
```

If the browser does not open, manually visit that URL.

## Step 5: Authorize

1. Paste or type the code.
2. Click **Continue**.
3. Review the app name and requested access.
4. Click **Authorize**.
5. Return to NEON GIT.

The status should change to:

```text
LINKED // YOUR-USERNAME
```

## Permissions requested

The app currently requests:

```text
repo read:user workflow
```

- `repo` allows public and private repository creation/update.
- `read:user` allows the app to identify the account and create a local commit identity.
- `workflow` allows commits that add or update files under `.github/workflows/`, such as the included Windows build workflow.

The app does not receive your GitHub password.

## Where the token is stored

The OAuth token is stored in Windows Credential Manager under a Generic Credential similar to:

```text
ProjectPublisher/GitHubOAuth
```

It is not stored in:

- `settings.json`
- Git remote URLs
- command-line arguments
- terminal logs
- source files

## Sign out

Click:

```text
Sign out
```

This removes the saved OAuth credential from Windows Credential Manager.

---

# 9. Understand the application interface

## Top bar

Shows:

- App name
- GitHub connection state
- Connected username

## Card 1 — GitHub connection

Contains:

- OAuth Client ID
- Connect/Reconnect button
- Sign-out button

## Card 2 — Select project workspace

Contains:

- Selected folder path
- Browse button
- Scan button
- Status
- Fetch
- Pull
- History

## Card 3 — Repository details

Contains:

- Owner
- Repository name
- Description
- Branch
- Commit message
- Private repository option
- README generation option
- Origin replacement option

## Card 4 — Interface screenshot

Contains:

- Web start command
- Local preview URL
- Auto capture
- Select image
- Clear image
- Screenshot preview

## Project analysis panel

Displays:

- Detected project type
- Candidate file count
- Source size
- Local-only protected path count
- Detected programming languages

## Security report

Displays:

- Severity
- File path
- Line number
- Finding category
- Safe action

Matched secret values are not displayed.

## Live Git Terminal

Shows actual commands and their output, for example:

```text
❯ git init
❯ git status --short
❯ git add --all -- .
❯ git commit -m "Initial secure project publish"
❯ git branch -M main
❯ git push --set-upstream origin main
```

The OAuth token is not shown.

## Bottom status bar

Shows the active pipeline stage and a **Cancel operation** button.

Cancellation stops the current process, but Git steps completed before cancellation are not automatically reversed.

---

# 10. Publish your first project

This section demonstrates a new local folder and a new GitHub repository.

## Step 1: Prepare your project

Example folder:

```text
E:\personal tools\C_drive_cleanup
```

The folder can contain source code, documentation, images, tests, workflows, and installer files.

Do not select a broad parent folder such as:

```text
E:\personal tools
```

Select the exact project root.

## Step 2: Browse

Click:

```text
Browse
```

Choose the project folder.

## Step 3: Scan

Click:

```text
Scan workspace + detect Git
```

The scan:

1. Detects the framework/project type.
2. Counts files and languages.
3. Finds `.env` and sensitive files.
4. Detects token-shaped values and secret assignments.
5. Checks whether the folder already contains `.git`.
6. Reads Git status if it is already a repository.

Review the security table before continuing.

## Step 4: Enter owner

For a personal repository, Owner should be your GitHub username.

Example:

```text
ABDULLAH-AMJID
```

For an organization repository, enter the organization name. Your account must have permission to create repositories there, and organization OAuth/SSO policy may require approval.

## Step 5: Enter repository name

Example:

```text
SpaceMedic
```

Good repository names:

```text
space-medic
windows-cleanup-tool
portfolio-dashboard
```

Avoid slashes and invalid Git reference characters.

## Step 6: Enter description

Example:

```text
A Windows disk analysis and cleanup utility with safe diagnostics and duplicate detection.
```

This description is used when creating a new repository and may update an existing repository description.

## Step 7: Choose branch

Default:

```text
main
```

Beginners should normally keep `main`.

## Step 8: Enter commit message

For the first publish:

```text
Initial secure project publish
```

For later updates:

```text
Add screenshot and improve documentation
```

A commit message should briefly explain what changed.

## Step 9: Select visibility

Check:

```text
Private repository (new repositories only)
```

to make a newly-created repository private.

If unchecked, a new repository is public.

For an existing repository, the app keeps its existing visibility.

## Step 10: README option

Check:

```text
Generate README in the project if one is missing
```

The app will not replace an existing README.

## Step 11: Origin replacement option

Leave this unchecked for your first attempt:

```text
Replace origin when it points to a different repository
```

Only enable it after reviewing an origin-mismatch warning and confirming that the current remote should be changed.

## Step 12: Optional screenshot

You can skip the screenshot for the first publish or follow the screenshot section later.

## Step 13: Open-after-publish option

Check:

```text
Open repository after publishing
```

if you want GitHub to open after success.

## Step 14: Execute

Click:

```text
EXECUTE SECURE GIT PIPELINE
```

Read the confirmation carefully. It explains that:

- Git commands will run in the selected folder.
- `.git` metadata will be created or updated.
- Protected files remain local.
- Optional support files may be created.
- Force-push is disabled.

Click **Yes** only after reviewing the details.

## Step 15: Watch the terminal

For a new project, you should see commands similar to:

```text
git --version
git check-ref-format --branch main
git rev-parse --show-toplevel
git init
git rm --cached -f --ignore-unmatch -- <protected file>
git status --short
git add --all -- .
git diff --cached --quiet
git config --local user.name <name>
git config --local user.email <noreply email>
git commit -m <message>
git remote add origin <GitHub URL>
git branch -M main
git push --set-upstream origin main
```

## Step 16: Confirm success

The success dialog shows:

- Repository URL
- Short commit SHA
- Number of protected local-only paths

Open the repository and verify:

- Source files are present.
- `.env` is absent.
- `.env.example` contains placeholders only.
- No private key or credential file is present.
- README and screenshot look correct.

---

# 11. Understand the security report

## Severity levels

### Critical

Examples:

- `.env`
- Private key
- GitHub token pattern
- Cloud access key
- Credential file

### Warning

Examples:

- Generic password assignment
- Connection string
- JWT
- Large file

### Info

Examples:

- Dependency folder excluded from scanning/upload policy
- Symbolic link not followed
- Generated directory skipped

## What “Keep local; remove from Git index” means

Suppose this local file exists:

```text
config.py
```

and contains a detected secret.

The app does not delete or rewrite `config.py`. Instead it executes the equivalent of:

```text
git rm --cached -f --ignore-unmatch -- config.py
```

and places the path in local Git exclude rules.

Results:

- Local file remains on your computer.
- File is absent from the new commit.
- If previously tracked, the next commit removes the repository copy.
- Old commits may still contain the previous version.

## `.env` behavior

Input:

```env
API_KEY=example-not-a-real-key
DATABASE_PASSWORD=example-not-a-real-password
PUBLIC_URL=https://example.com
```

The app keeps `.env` local and creates, when missing:

```env
API_KEY=__SET_IN_LOCAL_ENV__
DATABASE_PASSWORD=__SET_IN_LOCAL_ENV__
PUBLIC_URL=__SET_IN_LOCAL_ENV__
```

in:

```text
.env.example
```

The original `.env` is unchanged.

## If a real secret was already pushed

Removing it in a new commit does not remove it from old history.

Immediately:

1. Revoke or rotate the credential.
2. Do not rely only on deleting the file.
3. Clean Git history using a reviewed procedure.
4. Notify collaborators if necessary.

---

# 12. Add a real project screenshot

The app adds a real interface screenshot—not an AI-generated project image.

## Option A: Select an existing image

1. Click **Select image**.
2. Choose PNG, JPG, JPEG, BMP, or WebP.
3. Review the image preview.
4. Make sure no email, token, customer data, internal URL, or private information is visible.
5. Publish.

The image is re-encoded as PNG and added as:

```text
images/project-preview.png
```

## Option B: Automatically capture a web app

Supported detection includes common:

- React
- Next.js
- Vue
- Angular
- Svelte
- Node web projects
- ASP.NET Core
- Static websites

### Start command

Examples:

```text
npm run dev -- --host 127.0.0.1
npm start
dotnet run --project "MyWebApp.csproj" --no-launch-profile
```

The app attempts to suggest a command.

### Preview URL

Examples:

```text
http://127.0.0.1:5173
http://127.0.0.1:3000
http://127.0.0.1:4200
http://127.0.0.1:5199
```

### Capture procedure

1. Scan the workspace first.
2. Review/edit the start command.
3. Review/edit the preview URL.
4. Click **Auto capture**.
5. Read the trusted-code warning.
6. Click **Yes** only for your own/trusted project.
7. Wait for temporary dependency installation and startup.
8. Review the captured image.

The build/run occurs in a sanitized temporary copy, not the selected project's working tree.

## Option C: Automatically capture a desktop app

The app can attempt to build and capture detected:

- WPF
- WinUI 3
- Windows Forms

It builds a temporary copy, starts the produced executable, waits for its main window, and captures that window.

Some packaged WinUI projects may require manual capture.

## Security warning

The temporary copy protects the selected folder from build artifacts, but it is not Windows Sandbox. Running project code still uses your Windows account permissions and may access the network or computer.

## Playwright script error

Do not run:

```powershell
.\playwright.ps1
```

from the single-file publish folder. Run the app itself. The app uses installed Microsoft Edge.

Check Edge:

```powershell
Test-Path "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
Test-Path "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
```

At least one should normally return `True`.

---

# 13. Update an existing project

After editing your project:

1. Start NEON GIT.
2. Select the same project folder.
3. Click **Scan workspace + detect Git** again.
4. Review new security findings.
5. Keep the same Owner and repository name.
6. Enter a new commit message.
7. Click **EXECUTE SECURE GIT PIPELINE**.

Example commit messages:

```text
Fix duplicate scanner performance
Add project screenshots
Update installation guide
Release version 1.2.0
```

The app detects the existing `.git` repository and existing origin.

## If there are no changes

The app may report that no new staged changes exist and push the existing commit if necessary.

Check Status to confirm:

```text
Working tree clean.
```

## Always scan again

The app re-scans immediately before staging, even if you scanned earlier. This protects files added between the first scan and publish click.

---

# 14. Use Status, Fetch, Pull, and History

## Status

Click:

```text
Status
```

Equivalent command:

```text
git status --short --branch
```

Common markers:

| Marker | Meaning |
|---|---|
| `?? file` | Untracked file |
| `M file` | Modified file |
| `A file` | Added/staged file |
| `D file` | Deleted file |
| `## main...origin/main` | Branch/tracking status |

## Fetch

Click:

```text
Fetch
```

Equivalent command:

```text
git fetch origin --prune
```

Fetch downloads remote branch information but normally does not rewrite your working files.

Use Fetch before Pull when you want to inspect remote state.

## Pull

Click:

```text
Pull
```

The app asks for confirmation, then performs:

```text
git pull --rebase origin main
```

Replace `main` with the selected branch value.

A rebase places your local commits after newly-downloaded remote commits. If conflicts occur, the app stops and reports them.

## History

Click:

```text
History
```

The terminal displays up to 25 commits with:

- Short SHA
- Date
- Author
- Commit message

Equivalent command:

```text
git --no-pager log -25 --date=short --pretty=...
```

---

# 15. Work with an existing GitHub repository

## Case A: Local folder already points to the correct repository

The app reads:

```text
git remote get-url origin
```

If it matches the selected Owner/Repository, publishing continues.

## Case B: No origin exists

The app runs:

```text
git remote add origin <repository-url>
```

## Case C: Origin points somewhere else

Publishing stops with a warning.

Example:

```text
Origin already points to https://github.com/OLD-OWNER/OLD-REPO.git
```

Only if you are certain:

1. Review both repository URLs.
2. Enable **Replace origin when it points to a different repository**.
3. Publish again.

The app then performs:

```text
git remote set-url origin <new-url>
```

Disable the option again afterward if you no longer need it.

## Case D: Remote repository already has commits

If GitHub contains commits not present locally, Push may fail with:

```text
non-fast-forward
```

This refusal protects remote history. GitHub's official guidance is to fetch/pull the remote changes before pushing.

In the app:

1. Click **Fetch**.
2. Click **Pull**.
3. Resolve conflicts if reported.
4. Scan again.
5. Publish again.

Official reference:

- [GitHub: Dealing with non-fast-forward errors](https://docs.github.com/en/get-started/using-git/dealing-with-non-fast-forward-errors)

Do not solve this by force-pushing unless you fully understand the history impact. NEON GIT intentionally does not provide a force-push button.

---

# 16. Understand files created by the app

## `.git/`

Hidden local Git database containing:

- Commits
- Branches
- Index
- Local configuration
- Remote configuration
- Local exclude rules

Do not manually edit internal `.git` files.

## `.gitignore`

Repository-wide ignored-file rules. The app appends a marked security block and preserves existing contents.

Example:

```gitignore
# Project Publisher security rules
.env
.env.*
!.env.example
!.env.*.example
*.pfx
*.p12
*.pem
*.key
credentials.json
secrets.json
node_modules/
.venv/
bin/
obj/
```

## `.git/info/exclude`

Local-only ignore rules. The app adds exact protected paths here. This file is not committed to GitHub.

## `.gitattributes`

Controls line-ending normalization. Created only when missing. Existing files are not replaced.

## `.env.example`

A value-free template containing environment variable names.

## `README.md`

Project documentation. Generated only if missing and the option is enabled.

## `images/project-preview.png`

Reviewed project interface image copied during publishing.

---

# 17. Troubleshooting

## Error: build.ps1 is not digitally signed

Run:

```powershell
Unblock-File -LiteralPath .\build.ps1
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\build.ps1
```

Avoid permanently setting the entire machine to `Unrestricted`.

## Error: dotnet is not recognized

Install the .NET 8 SDK, restart PowerShell, and verify:

```powershell
dotnet --version
where.exe dotnet
```

If it exists but PATH is not refreshed:

```powershell
$env:Path = "C:\Program Files\dotnet;$env:Path"
dotnet --version
```

## Error: git is not recognized

Install Git for Windows, restart PowerShell, and verify:

```powershell
git --version
where.exe git
```

## Error: device_flow_disabled

Open your OAuth App settings and enable:

```text
Enable Device Flow
```

Then reconnect.

## Error: incorrect_client_credentials

Verify that you pasted the **Client ID**, not:

- Client Secret
- App ID
- Device code

Remove spaces before/after the Client ID.

## Device code expired

Click **Connect GitHub** again and use the new code. Device codes are temporary.

## Authentication failed / HTTP 401

1. Click Sign out.
2. Connect GitHub again.
3. Re-authorize the OAuth App.
4. Verify that the repository still exists.

## HTTP 403 or organization access denied

Possible reasons:

- You do not have write permission.
- The organization blocks unapproved OAuth Apps.
- SAML SSO authorization is required.
- The organization restricts repository creation.

Ask an organization owner to approve access if necessary.

## Error: refusing to allow an OAuth App to create or update workflow without workflow scope

The project contains a GitHub Actions file under `.github/workflows/`, but the saved OAuth token was issued without the `workflow` scope.

1. Install/build NEON GIT v0.2.2 or newer.
2. Open the new executable.
3. Reconnect GitHub and approve the new `workflow` permission. Old tokens cannot gain new scopes automatically.
4. Select the same project folder.
5. Scan it again.
6. Click **EXECUTE SECURE GIT PIPELINE** again.

The previous local commit remains safe. Do not delete `.git`, recreate the repository, or force-push.

## Repository not found

Check:

- Owner spelling
- Repository name spelling
- Account access
- Repository visibility
- Organization permissions

## Origin already exists

This is not automatically overwritten for safety. Review the existing remote:

```powershell
cd "YOUR-PROJECT-FOLDER"
git remote -v
```

Enable Replace Origin only if the old URL should truly be changed.

## Push rejected: non-fast-forward

Use:

1. Fetch
2. Pull
3. Resolve any conflicts
4. Publish again

## Nothing to commit

No safe staged changes exist. Use Status to check whether:

- Working tree is already clean.
- All new files were protected/ignored.
- You selected the correct folder.

## Selected folder is inside another Git repository

The app requires the exact repository root.

Find it in PowerShell:

```powershell
cd "YOUR-SELECTED-FOLDER"
git rev-parse --show-toplevel
```

Select the returned folder in the app.

## Invalid branch name

Use a simple branch such as:

```text
main
develop
feature/dashboard
```

Avoid spaces, `..`, trailing dots, and special Git reference sequences.

## LF will be replaced by CRLF

This is usually a warning, not a failed command. The app creates `.gitattributes` when missing to make line-ending behavior more consistent.

Do not change global Git line-ending settings unless you understand their effect on all repositories.

## playwright.ps1 cannot find Microsoft.Playwright.dll

Do not run the script from `artifacts\win-x64`. It is not required for the self-contained app. Build v0.2.2 or newer and run:

```powershell
.\ProjectPublisher.exe
```

## Auto screenshot cannot find Edge

Check:

```powershell
Test-Path "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
Test-Path "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
```

Install/update Microsoft Edge if both return `False`.

## Web screenshot waits forever

Check:

- Start command is correct.
- Dependencies install successfully.
- Preview URL and port match the project.
- Another application is not using the port.
- The app does not require missing private environment values.

Try the command manually in a trusted copy to verify the correct URL.

---

# 18. Safe conflict recovery

A conflict can occur when local and GitHub changes edit the same part of a file.

## Identify the conflict

Open PowerShell in the project:

```powershell
cd "E:\path\to\project"
git status
```

Conflicted files may contain markers:

```text
<<<<<<< HEAD
local content
=======
remote content
>>>>>>> remote-commit
```

## Option A: Resolve and continue

1. Open each conflicted file.
2. Decide what final content should remain.
3. Delete all conflict markers.
4. Save the file.
5. Stage the resolved file:

```powershell
git add "path\to\file"
```

6. Continue rebase:

```powershell
git rebase --continue
```

7. Repeat if another conflict appears.
8. Return to NEON GIT, scan, and publish.

## Option B: Abort pull/rebase

If you are unsure:

```powershell
git rebase --abort
```

Then:

```powershell
git status
```

This is safer than randomly deleting files or force-pushing.

## Never do this blindly

Avoid these commands unless you fully understand their consequences:

```text
git reset --hard
git clean -fd
git push --force
git push --force-with-lease
```

They can destroy local or remote work.

---

# 19. Sign out, reset, or uninstall

## Sign out of GitHub

Use the app's **Sign out** button.

This removes the saved OAuth credential.

You can also inspect Windows Credential Manager:

1. Open Start.
2. Search for **Credential Manager**.
3. Open **Windows Credentials**.
4. Look under **Generic Credentials** for:

```text
ProjectPublisher/GitHubOAuth
```

Prefer the app's Sign out button for normal removal.

## Local app data

Settings and previews are stored under approximately:

```text
%LocalAppData%\ProjectPublisher
```

Open it:

```powershell
explorer "$env:LOCALAPPDATA\ProjectPublisher"
```

Do not manually copy token data between computers.

## Stop managing a project with Git

Deleting `.git` removes local Git history and configuration but leaves normal project files.

Do this only if you are certain and have a backup:

```powershell
Remove-Item -LiteralPath ".git" -Recurse -Force
```

This does not delete the GitHub repository.

## Delete a GitHub repository

NEON GIT does not provide repository deletion. This prevents accidental destructive actions.

Repository deletion must be performed through GitHub settings with GitHub's confirmation process.

## Uninstall the app

If installed through the Inno Setup installer, use Windows **Installed apps**.

For a portable build, close the app and delete the build folder. Sign out first if you also want to remove the saved OAuth credential.

---

# 20. Beginner safety checklist

Before every first publish:

- [ ] I selected the exact project root.
- [ ] GitHub shows the correct connected account.
- [ ] Owner and repository name are correct.
- [ ] Public/private selection is correct.
- [ ] Branch is `main` unless I intentionally use another branch.
- [ ] Commit message explains the change.
- [ ] I reviewed every Critical and Warning finding.
- [ ] `.env` is marked local-only.
- [ ] No certificate/private key will be uploaded.
- [ ] Screenshot contains no private information.
- [ ] Replace Origin is disabled unless intentionally required.
- [ ] I understand that direct mode creates `.git` and support files.
- [ ] I will not force-push to solve an error I do not understand.

After publishing:

- [ ] Repository opens successfully.
- [ ] `.env` is absent on GitHub.
- [ ] `.env.example` contains placeholders only.
- [ ] No token/password/private key is visible.
- [ ] README and screenshot are correct.
- [ ] GitHub Actions do not reveal secrets in logs.

---

# 21. Frequently asked questions

## Do I need to type Git commands manually?

Usually no. The app runs the common commands and shows them in the live terminal.

## Does the app include Git?

No. The selected product configuration uses installed Git for Windows.

## Do users need the .NET SDK to run the self-contained EXE?

No. The SDK is required to build from source. The published self-contained executable carries its required .NET runtime.

## Do users need their own OAuth App?

For personal testing, you can create your own OAuth App and enter its Client ID. For a distributed product, the publisher normally registers one production OAuth App and preconfigures its public Client ID so end users only authorize it.

## Is the Client ID secret?

No. The Client ID identifies the OAuth App. The app must never contain a Client Secret.

## Why does the app request `repo` access?

Because it supports creating and updating both public and private repositories. A public-only edition could request `public_repo read:user workflow` instead, but private repository publishing would stop working.

## Does the app change my original project?

Direct Workspace Mode intentionally creates Git metadata and may add support files such as `.gitignore`, `.gitattributes`, README, env examples, and a screenshot. It does not rewrite detected secret-bearing source files.

## Does the app remove API keys from source files?

No. In direct mode, the entire detected file is kept local and excluded from the commit. This avoids changing your working source while preventing the detected value from being uploaded.

## Why is a whole file excluded instead of only replacing one value?

Committing a different redacted version while keeping an unmodified local working file creates confusing Git-index differences and can leak on a later manual stage. Excluding the file is safer for the direct-mode beginner workflow.

## Can I publish an existing Git repository?

Yes. Select its exact root. The app reads status and origin, stages safe changes, commits when needed, and pushes.

## Can the app overwrite the wrong origin?

Not silently. A different origin stops the pipeline unless you explicitly enable Replace Origin.

## Can the app force-push?

No. This is intentionally outside the beginner-safe publisher scope.

## Can I use an organization repository?

Yes, if your account has permission and the organization allows/approves the OAuth App. Organization SSO rules may require additional authorization.

## Can I use spaces in folder paths?

Yes. Git arguments are passed as structured process arguments rather than one shell string.

## Is automatic screenshot execution fully sandboxed?

No. It runs a temporary sanitized copy but still uses your Windows account permissions. Only run trusted project code.

## What should I do if I am unsure about an error?

Stop, copy the terminal output, run `git status` in the project, and ask for help before using reset, clean, or force-push commands.

---

## Quick first-run command summary

```powershell
# Install prerequisites once
winget install --id Git.Git -e --source winget
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget

# Restart PowerShell, then verify
git --version
dotnet --version

# Build
cd "E:\personal tools\GitHubProjectPublisher"
Unblock-File .\build.ps1
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\build.ps1

# Run
.\artifacts\win-x64\ProjectPublisher.exe
```

Then in the app:

```text
Enter OAuth Client ID
→ Connect GitHub
→ Select exact project folder
→ Scan workspace + detect Git
→ Review protected paths
→ Enter repository details
→ Optional screenshot
→ Execute secure Git pipeline
→ Verify the repository on GitHub
```
