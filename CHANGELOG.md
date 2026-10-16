# Changelog

All notable changes to Tandem are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-10-17

First public release.

### Accounts and switching

- Any number of Claude accounts, each in its own data folder, so each stays signed in.
- Switch from the tray icon, a global shortcut (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>A</kbd>, then
  <kbd>1</kbd>–<kbd>9</kbd>) or an optional button on Claude windows that can be dragged anywhere.
- An account that opens takes the place of the window you are in; the previous window can stay
  open, be minimized or be closed.
- **Open all** and **Arrange**: every account side by side, on one screen or across all screens.
- Status of every account (open, in the background, not open), the account in use, when each
  was last used, and its subscription.

### Usage

- 5-hour and weekly limits of every account, from the readings the Claude app saves, with
  reset times worked out from past readings.
- A 7-day chart per account, the account with the most room right now, and a notification when
  the account in use reaches a limit.
- A warning when two accounts are signed in to the same Claude account.

### The same Claude in every account

- The original account's app setup (MCP servers, tool toggles, extensions, SSH connections,
  Code presets) follows every added account automatically, with backups of replaced files.
- Claude Code and Cowork history can be brought over when an account is added, or later.
- Added accounts get a verified copy of Claude Code from another account when the download
  server refuses the network.

### Sign-in

- An optional, guided sign-in helper: the browser's sign-in reaches the account that is signing
  in. Tandem registers only its own keys; the user picks it in Windows Settings.

### Everything else

- Per-account desktop and Start menu shortcuts, start with Windows, light and dark themes.
- Deleting an account moves its folder to the Recycle Bin.
- One self-contained `Tandem.exe`; no installer, no administrator rights, no network
  connections, no telemetry.

[Unreleased]: https://github.com/Arman-Manteghi/Tandem/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Arman-Manteghi/Tandem/releases/tag/v0.1.0
