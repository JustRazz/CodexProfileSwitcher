# Profile Switcher Public

A portable Windows utility for switching Codex between an OpenAI account and Routera, with an editor for TOML configuration files.

## Getting started

1. Extract the ZIP into any folder you own and run **ProfileSwitcherPublic.exe**. No installation is required. Windows 10/11 with .NET Framework 4.8 is supported. This is an unsigned community utility, not an official OpenAI application.
2. The app detects `CODEX_HOME` from the process or Windows user environment; otherwise it suggests `%USERPROFILE%\.codex\config.toml`. Use **Browse** or type another absolute `.toml` path and click **Load path**. The default `.codex` folder is supported.
3. Close the Codex clients that use that file. Configure your Routera endpoint and model ID, then select **Use Routera**. No model is preselected. The endpoint must support the OpenAI Responses API.
4. Reopen the affected clients and start a new task. To return, select **Use OpenAI account** and restart those clients. Sign in through Codex if you do not already have a valid account session.

The app and CLI are both affected only when they use the same selected configuration. Other installations or apps that share that file will also see its changes. The switcher never modifies a different client configuration automatically.

## Open, edit and change locations

- **Browse / Load path:** choose an existing TOML file. The choice is remembered only in your local Windows profile.
- **New config:** create a new OpenAI account configuration in an existing folder. It never overwrites an existing file.
- **Open / edit:** inspect and edit the complete TOML file. Ctrl+S saves; Ctrl+Shift+S opens Save As. The editor prompts before discarding unsaved edits.
- **Save:** back up the current file and write your edits. External changes made while the editor is open cause the save to stop, avoiding an accidental overwrite. Basic scalar and multiline structure is checked; this is not a complete TOML or Codex schema validator.
- **Save As:** create a new copy at a different location and select it in the switcher. It does not delete or move the original. Choose a new filename; existing destinations are not overwritten. Local profile metadata is carried over so a Routera copy can still switch back to OpenAI on the same computer.
- **Open folder / Open backups:** inspect the selected configuration folder or its backup history.

Choosing or copying a file **does not change where Codex loads configuration**. For a new configuration home, separately configure the relevant clients to use that `CODEX_HOME` and place the active file there as `config.toml`. Arbitrary `.toml` filenames can be edited here but are not automatically loaded by Codex. Moving only configuration does not move account sign-in, history, or other Codex state. No system or user environment variables are changed by this utility.

## Credentials and private data

The public package contains no credentials, personal configuration, account identifiers, machine-specific paths, history, or saved login sessions. On first use, users supply their own settings:

- **Environment variable:** enter a variable name such as `ROUTERA_API_KEY`. The app checks that it exists, but does not copy its value into the configuration or app folder. Codex must inherit that variable; restart clients and terminals after setting it.
- **Encrypted key:** enter a key once. Windows DPAPI encrypts it for the current Windows account. A local credential helper supplies it to Codex. The configuration stores only the helper path, never the key. Recent Codex versions with command-backed provider authentication are required.

Runtime data is stored outside the app folder in `%LOCALAPPDATA%\CodexProfileSwitcherPublic`:

- `preferences.json`: last selected configuration path.
- `profiles\<path-hash>\settings.json`: Routera settings and original OpenAI root settings for that configuration path.
- `profiles\<path-hash>\backups\`: complete configuration backups.
- `profiles\<path-hash>\*.dpapi` and `CredentialHelper.exe`: encrypted keys and helper, only when encrypted authentication is used.

That runtime directory is private data. Backups can contain secrets users put in their configuration. Do not share it. Share the original ZIP, which contains only the program, public artwork, documentation and source. Windows-encrypted keys are not transferable to another computer/account. Keep the private data directory while Routera is active.

The switcher makes no network requests. Codex sends requests to the selected provider when it runs. The switcher does not read or modify `auth.json`, OS account credentials, browser sessions, or account logins. Both configurations use whichever OpenAI sign-in already belongs to the selected Codex home.

## Profile behavior

The first Routera switch captures the current normal OpenAI root settings. A return to OpenAI restores those settings and selects ChatGPT account authentication. Provider-independent settings, project tables and plugin entries are preserved.

If the file starts with another provider or a custom account URL, choose **Use OpenAI account** first. That creates a standard OpenAI baseline with automatic model selection. The previous complete configuration is backed up. Configuration authored by a different switcher has no shared saved profile metadata with this edition.

Routera uses the managed provider `routera_public_switcher`. Use the main form to change generated Routera settings; edits inside its managed block may be replaced by the next switch. Full-file edits elsewhere are retained. Linked configuration/storage paths and unusual managed scalar forms are rejected rather than rewritten ambiguously.

## Build and recovery

Run `Build.ps1` with Windows PowerShell to rebuild from `src`. No network download or SDK is required; it uses the .NET Framework compiler included with Windows.

Run `Test.ps1` for the isolated regression checks. All test configurations and synthetic credentials are created under a fresh temporary folder, outside the app folder and real Codex settings.

To recover manually, close Codex and copy a chosen backup over the selected TOML file. Encrypted-provider backups still depend on the local credential helper and encrypted key files referenced by that configuration.

Provider format: [official Codex configuration documentation](https://learn.chatgpt.com/docs/config-file/config-advanced). Account storage: [official authentication documentation](https://learn.chatgpt.com/docs/auth).
