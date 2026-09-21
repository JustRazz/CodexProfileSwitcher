# Public edition validation

- 37 isolated checks passed using synthetic configuration and fake credentials.
- Profile switches preserve unrelated settings and OpenAI model settings.
- The normal `.codex` location and custom TOML file paths are supported.
- Editing, backups, Save As, independent state per location, and external-edit conflict detection were exercised.
- Save As leaves the original file unchanged; a copied encrypted Routera configuration can switch back to OpenAI and back again.
- Windows DPAPI encryption, credential-helper output, atomic writes, and actual Windows folder access restrictions were exercised.
- Standard account authentication files remain unchanged.
- The main window and editor were rendered and visually inspected. Mixed line endings are displayed consistently.
- Test execution uses a temporary directory and does not write to a real Codex configuration or account store.
- No live Routera inference request was made. Provider access, managed policy and compatibility still depend on each recipient's installation and account.
- The distributable was scanned as UTF-8 and UTF-16 for personal paths and known credential values. No private configuration, authentication stores, encrypted keys, preferences, account data or debug-symbol files are included.

Run `Test.ps1` to reproduce the isolated checks. It writes test outputs to a new temporary folder and prints that location.
