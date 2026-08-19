# Validation record — v0.2.2

Validation performed in the workspace on 2026-08-17.

## OAuth workflow-scope fix

Version 0.2.2 requests and records `repo read:user workflow`. Saved tokens that do not contain all required scopes are no longer silently restored. This allows the Git command pipeline to add/update `.github/workflows/*.yml` files after the user reconnects and approves the new permission.

The scanner also skips `artifacts/` and narrows generic assignment matching to reduce code-reference/test-fixture false positives.

## Playwright single-file packaging fix

Version 0.2.1 explicitly extracts bundled Playwright content for the self-contained executable and removes the standalone `playwright.ps1` from publish output. That script requires a loose `Microsoft.Playwright.dll` and is not usable beside a single-file executable. The app targets installed Microsoft Edge directly, so end users do not run a browser-install script.

Validated publish output contains `ProjectPublisher.exe` and symbols only; no broken standalone script remains.

## Windows-targeted solution build

```text
dotnet build GitHubProjectPublisher.sln -c Release --no-restore
Build succeeded.
0 Warning(s)
0 Error(s)
```

The WPF application and both test sources compile with .NET 8.

## Real Git CLI pipeline integration

A platform-independent harness linked the production command/publisher source and ran against the installed Git executable, a temporary direct project, a fake GitHub API handler, and a local bare remote.

Executed behavior included:

- bare remote creation
- workspace `git init`
- local security exclude creation
- repository `.gitignore` security block
- value-free `.env.example` generation
- `.gitattributes` generation
- protected-path index removal
- `git add --all`
- local identity configuration
- commit
- origin configuration
- branch rename to `main`
- authenticated push path (local transport ignored the ephemeral HTTP header)
- remote tree inspection

Result:

```text
GIT CLI PIPELINE PASS
```

Verified remote tree:

```text
.env.example
.gitattributes
.gitignore
README.md
app.py
```

Verified separately:

- `.env` was absent from the commit and unchanged locally.
- A source file containing a shaped fake API key was absent from the commit and still present locally.
- No credential was included in command arguments or remote URL.

## Security scanner/staging smoke test

The screenshot/staging pipeline was also tested independently for environment exclusion, value-free example generation, token redaction, private-key exclusion, dependency-folder exclusion, and source integrity.

## Windows acceptance still required

Before a signed public release, run the xUnit suite and manual UI acceptance on Windows 10/11 with Git for Windows:

- OAuth Device Flow and token refresh
- new/existing public and private repositories
- GitHub organization/SSO behavior
- non-fast-forward and protected-branch failures
- origin mismatch approval
- cancellation at each command stage
- fetch/pull conflict handling
- WPF/WinUI/Windows Forms and web screenshot paths
- installer, code-signing, x64, and ARM64 release artifacts
