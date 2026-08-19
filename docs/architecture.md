# NEON GIT architecture

## Chosen product configuration

- Native WPF frontend
- Professional cyberpunk design system
- Direct selected-folder Git mode
- Installed Git for Windows command engine
- Publisher plus status/fetch/pull/history

## Runtime components

| Component | Responsibility |
|---|---|
| `MainWindow` | Cyberpunk workflow UI, confirmations, live terminal, cancellation |
| `GitCommandService` | Direct `git.exe` execution, structured args, output capture/redaction |
| `GitCliPublishService` | Init, security excludes, stage, commit, repository, remote, branch, push |
| `ProjectAnalyzer` | Framework/language detection and workspace statistics |
| `SecretScanner` | Value-free secret findings and upload policy |
| `GitHubAuthService` | OAuth Device Flow polling and refresh |
| `CredentialVault` | Windows Credential Manager token storage |
| `GitHubApiService` | Account/repository lookup, creation, metadata update |
| `ScreenshotService` | Sanitized temporary web/desktop build and actual interface capture |
| `ReadmeGenerator` | Project-aware README when missing |

## Publish state machine

```text
Select workspace
  └─ Analyze + classify protected paths
      └─ User confirmation
          └─ git --version
              └─ detect exact repository root / git init
                  └─ .git/info/exclude + .gitignore security policy
                      └─ generate safe env examples / line-ending policy
                          └─ untrack protected paths
                              └─ git status + git add --all
                                  └─ untrack protected paths again
                                      └─ staged-change test
                                          └─ local identity + commit (when needed)
                                              └─ GitHub repo lookup/create
                                                  └─ origin validation
                                                      └─ branch rename
                                                          └─ authenticated push
```

The second untrack pass is intentional: it ensures a protected tracked path cannot be reintroduced by `git add --all`.

## Command process boundary

```text
UI action
  → allowlisted workflow operation
  → IReadOnlyList<string> arguments
  → ProcessStartInfo.ArgumentList
  → git.exe (no shell)
  → redirected stdout/stderr
  → secret redactor
  → live terminal
```

The UI does not accept an arbitrary shell command for publishing. Screenshot startup commands are a separate explicitly-confirmed trusted-code feature.

## Direct security model

Common credentials are ignored repository-wide through `.gitignore`. Exact findings are additionally stored in local `.git/info/exclude`. `git rm --cached -f --ignore-unmatch` removes a tracked index entry without deleting the working file.

Direct mode favors a real reusable local repository over source-folder immutability. Security-bearing source values remain byte-for-byte unchanged.

## Authentication flow

```text
OAuth Client ID → Device Flow → GitHub user authorization
Access/refresh token → Windows Credential Manager
Token → Bearer header for GitHub REST API
Token → ephemeral Git process HTTP authorization environment
```

The displayed push remains:

```text
git push --set-upstream origin main
```

No credential appears in the command or origin URL.

## Update operations

- Status is read-only.
- Fetch prunes stale origin references.
- Pull uses rebase only after user confirmation and stops on conflicts.
- History uses a no-pager 25-commit format.
- Force push/reset are outside the publisher-plus scope.
