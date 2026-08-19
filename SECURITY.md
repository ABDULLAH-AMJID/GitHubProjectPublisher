# Security model — Direct Workspace Mode

## Trust boundaries

Project Publisher coordinates three privileged systems:

1. The selected local project and its Git repository.
2. The installed `git.exe` process.
3. GitHub OAuth/API access.

The application runs as the current Windows user and never requests elevation.

## Direct-mode filesystem effects

The selected folder is intentionally a working Git repository. The app may create/update:

- `.git/`
- `.gitignore` (append-only Project Publisher block)
- `.gitattributes` (only when missing)
- value-free `.env*.example` files (only when missing)
- `README.md` (only when missing and enabled)
- `images/project-preview.png` (when a reviewed screenshot is supplied)

It does not rewrite or delete a source file containing a detected secret. `git rm --cached` removes only its index entry; the working file remains.

## Protected paths

Critical/warning findings become local-only paths. The app writes exact local patterns into `.git/info/exclude`, applies repository-wide common-secret patterns to `.gitignore`, and removes matching tracked entries from the index before staging.

Known detection includes GitHub tokens, OpenAI-style keys, AWS IDs, Google keys, Slack tokens, JWTs, private-key blocks, generic key/token/secret/password assignments, `.env` variants, credential filenames, certificate/keystore extensions, symbolic links, and oversized files.

Reports and logs show category/path/action but not matched secret values.

## Git command execution

`git.exe` is invoked directly with `UseShellExecute=false`. Each operation uses `ProcessStartInfo.ArgumentList`; no command is assembled for `cmd.exe`, PowerShell, or a shell parser. This reduces command-injection and quoting risk for paths, URLs, branch names, and commit messages.

Force-push, hard reset, remote deletion, and automatic conflict resolution are not implemented. A mismatched `origin` is rejected unless the user explicitly enables replacement.

## Push authentication

GitHub access and refresh tokens are stored as a Generic Credential in Windows Credential Manager. For a push/fetch/pull, authorization is supplied to Git as ephemeral process environment configuration. It is not persisted in Git config, command arguments, remote URLs, settings JSON, or displayed logs.

A same-user process with sufficient access may inspect another process's memory/environment; this is equivalent to the trust boundary of the desktop app itself. A future hardened edition should use a dedicated `GIT_ASKPASS` broker with IPC and short-lived credentials.

## Residual risks

- Detection cannot guarantee identification of every secret format.
- Manually forcing an ignored file into Git can bypass app protection.
- Removing a leaked file in a new commit does not erase prior Git history.
- Direct mode changes Git metadata and support files in the selected project by design.
- Pull/rebase can produce conflicts; the app stops rather than resolving them.
- Child code executed for screenshots is not OS-sandboxed.
- Screenshots can visibly contain private data even after metadata is removed.
- Organization SSO/application policy may block OAuth access.

## Production hardening recommendations

- Add a staged diff/file-selection screen before commit.
- Add entropy scanning and organization-defined secret patterns.
- Add GitHub push-protection response parsing.
- Use a native askpass broker rather than an HTTP extra-header environment value.
- Add Windows Sandbox/VM execution for screenshots.
- Add signed builds, SBOM, dependency scanning, and reproducible release checks.
- Add an explicit history-cleaning assistant that requires credential rotation confirmation.
