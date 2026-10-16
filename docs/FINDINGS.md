# How the Claude desktop app behaves on Windows

These notes describe what Tandem relies on: how the official app is installed and started, how
it separates accounts, how sign-in links reach it, and the local files Tandem reads or copies.
They were gathered by observation only: process lists, the package manifest, folder names and
the app's own log, plus test launches with empty data folders. The app itself was never
modified.

Tested with the MSIX build of the app (2.37337 and later) on Windows 11.

## Summary

| Question | Answer |
|---|---|
| Install type | MSIX package `Claude_pzs8sxrjxfjjc`, signed by Anthropic, PBC |
| Update-proof launcher | The app execution alias `%LOCALAPPDATA%\Microsoft\WindowsApps\claude-desktop.exe`, declared by the package. Fallback: activate `Claude_pzs8sxrjxfjjc!Claude` with `IApplicationActivationManager`. Never the versioned path under `C:\Program Files\WindowsApps`. |
| Default data folder | `%APPDATA%\Claude` |
| Account isolation | `--user-data-dir="<folder>"` starts a separate instance with its own window and sign-in, side by side with the normal one. |
| Critical caveat | The folder must exist before the app starts, created by a process outside the app's package. Otherwise MSIX virtualization moves it into the package's private storage. |
| `claude://` links | Windows sends them to the package, which starts the app without `--user-data-dir`, so they always reach the original account. Starting the alias with an account's `--user-data-dir` and the link reaches that account. |
| Sign-in | Browser based. The browser hands the result back with a `claude://` link, so an added account needs a link handler that forwards it. |

## 1. Installation and launching

The package manifest declares:

- `Application Id="Claude" Executable="app\Claude.exe"`, so the app user model id is
  `Claude_pzs8sxrjxfjjc!Claude`.
- `uap5:AppExecutionAlias Alias="claude-desktop.exe"`. Windows creates the alias in
  `%LOCALAPPDATA%\Microsoft\WindowsApps\`, and it always points to the installed version.
  Starting it with `CreateProcess` returns the real main `Claude.exe` process.
- `uap3:Protocol Name="claude"`: protocol activation passes only the URL.
- File-system write virtualization for `AppData`, apart from a few excluded folders (section 3).
- A machine-wide `CoworkVMService`.

The install folder changes with every update, so Tandem resolves the launcher on every launch:

1. the app execution alias;
2. if the user turned the alias off, package activation by app user model id;
3. the older Squirrel install, `%LOCALAPPDATA%\AnthropicClaude\app-<version>\claude.exe`;
4. otherwise a "Claude is not installed" message.

## 2. One data folder per account

The original account keeps `%APPDATA%\Claude`. Its main process is started without switches,
and its child processes carry `--user-data-dir=%APPDATA%\Claude`.

Starting the alias with `--user-data-dir="<folder>"` from a process outside the package gives:

- a new main `Claude.exe` with its own GPU, renderer and utility processes, all carrying the
  same `--user-data-dir`, and full package identity;
- every file written into that folder;
- a separate window, signed out, while the original instance keeps running untouched.

The app's single-instance lock is keyed by the data folder. Starting the alias again with the
same folder focuses the running instance instead of opening a second one.

Window titles are the same (`Claude`) for every account, so Tandem maps windows to accounts by
process. A main process is one without a `--type=` switch, its `--user-data-dir` names the
account (none means the original one), and its visible top-level windows belong to it. Only
the main process's image path, start time, package and those two switches are read; command
lines are never stored or logged.

## 3. MSIX file-system virtualization

New files that a packaged process, or any process it starts, creates under `AppData` go to
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\…`, unless the folder already exists
in the real location. A program started from the app's built-in terminal inherits this. If
the app created an account's folder itself:

- the folder would be invisible outside the package;
- moving it to the Recycle Bin would only remove an empty shell;
- its data would be lost when the package is reset or removed.

So Tandem:

- creates each account folder (`%LOCALAPPDATA%\Tandem\Profiles\<id>`) from its own process
  before the first launch, and again if it went missing;
- checks afterwards that no mirror exists in the package's `LocalCache`, and refuses to launch
  if one does;
- detects when it was itself started from inside the app and restarts through Explorer;
- checks after a launch that the app created its `lockfile` in the requested folder, so a
  future app version that ignored the switch would be reported.

## 4. `claude://` links and sign-in

Windows resolves `claude://` through the hash-protected `UserChoice` key, which points to the
package's AppX ProgId. Protocol activation starts `app\Claude.exe "<url>"` without
`--user-data-dir`, so the link always reaches the original account. The classic key
`HKCU\Software\Classes\claude` is rewritten by the app on every start, and it is ignored while
`UserChoice` points to the package.

Signing in to a new account:

1. the signed-out window opens the system browser;
2. claude.ai finishes with "Finish sign-in in the Claude app", and the browser asks to open
   the app;
3. the page's button is a script, not a link, so there is nothing to copy;
4. choosing *Open* sends the link to the original account, and the new window stays signed out.

Starting `claude-desktop.exe --user-data-dir="<folder>" "<link>"` delivers the link to that
account's running instance. The sign-in link is tied to the instance that started the sign-in,
so the original account is never affected.

### The sign-in helper

- Tandem registers its own ProgID, `Capabilities` and `RegisteredApplications` entry under
  `HKCU`, pointing at `Tandem.exe --open-url "%1"`. Claude's keys are never changed, and no
  administrator rights are needed.
- Tandem opens Windows Settings on its own *Default apps* page, and the user picks Tandem for
  `CLAUDE` links. Windows reserves that choice for the user; Tandem never writes `UserChoice`.
- Each link goes, unchanged and in memory only, to: the account waiting for sign-in (for 30
  minutes after *Add account* or *Sign in again*), otherwise the account used last (or always
  the original one, a setting), otherwise the original account.
- Turning the helper off opens the same Settings page, so the user can choose Claude again;
  then Tandem removes its own keys.

## 5. What is per account

- **Server-side, per account:** chats, Projects, memory and connectors.
- **Per account even inside one data folder:** Code and Cowork history.
  `claude-code-sessions\` and `local-agent-mode-sessions\` hold one sub-folder per account or
  organization.
- **Shared by every account already:** the user's files and repositories, and Claude Code's own
  configuration in `%USERPROFILE%\.claude`.
- **Per data folder:** the desktop app's own setup (MCP servers and preferences in
  `claude_desktop_config.json`, MCP tool toggles, desktop extensions, SSH connections, Code
  presets), window state and device identity.

Tandem therefore keeps two fixed allowlists (`src/Tandem.Core/Profiles/ProfileSetup.cs`):

1. **App setup**, copied from the original account before an added account starts, when a file
   is new or different. The replaced version goes to `%LOCALAPPDATA%\Tandem\Backups`.
2. **Code and Cowork history**, brought over when an account is added, or on request. Missing
   files are copied, and a file is only replaced by a newer, longer copy.

Neither list contains sign-in data, cookies, tokens, web storage, `config.json` or device
identity (`ant-did`, `ant-device-registry.json`). Tests check this.

## 6. Side effects of several instances

- **Claude in Chrome:** every instance points the browser extension's native messaging host
  (`HKCU\Software\Google\Chrome\NativeMessagingHosts\com.anthropic.claude_browser_extension`)
  at its own data folder when it starts, so the extension talks to the account started last.
- **Global shortcuts** of the app, such as quick entry, can belong to one instance only.
- **Cowork's virtual machine service** is shared by all instances.
- Each data folder gets its own device identity, so the service sees each account as a
  separate installation.

## 7. Usage readings

The app keeps `plan-usage-history.json` in each data folder:

```json
{ "version": 2, "samples": [ { "org": "<uuid>", "t": 1792108800000, "u": { "fh": 45, "sd": 51 } } ] }
```

It adds a sample about every 15 minutes while the app runs, and keeps about 30 days. There is
no plan field, so the subscription is something the user picks in Tandem.

- `fh` is the percent used of the 5-hour limit. In the observed windows, usage fell back to
  zero exactly five hours after the first sample that showed use (not rounded to the hour).
- `sd` is the percent used of the weekly limit. It resets at the same time each week. A one-off
  reset in the middle of a week can also happen, for example after a plan change.
- `org` identifies the organization. Usage belongs to the organization, not to the folder: a
  folder can hold samples of several organizations after a different sign-in, and two folders
  can hold the same one.

Tandem groups samples by organization across all folders, and shows each account the
organization of its newest sample. The 5-hour reset is the first sample of the current window
plus five hours. The weekly reset comes from overlapping the intervals in which past resets
must have happened, at the time of week most of them agree on, and it shows as unknown until
a reset has been seen. The file is opened read-only, its values are never logged, and the
organization key is only compared, never shown or stored
(`src/Tandem.Core/Usage/UsageReader.cs`).

## 8. Claude Code downloads

The Code tab runs a Claude Code program that the app downloads into each data folder from
`https://downloads.claude.ai/claude-code-releases/<version>/win32-x64/claude.exe.zst`. Each
build folder `claude-code\<version>\<id>\` holds:

- `claude.exe`;
- `.payload`, with the program's SHA-256 and size;
- `.verified`, a 64-digit hash written once the download checked out. `<id>` is its first 12
  digits.

Some networks and VPNs get HTTP 403 for every file on that server. The app's log then shows the
download failing and the app falling back to an older verified build on disk
(`Using Claude Code binary at …\claude-code\<older version>\…`). An account added later has no
older build, so its Code tab shows *Couldn't download Claude Code*.

Tandem gives an added account the newest verified build another account already has
(`src/Tandem.Core/Claude/ClaudeCodeShare.cs`):

1. it checks `claude.exe` against the SHA-256 in `.payload`;
2. it hard-links the file, or makes a checked copy on another drive;
3. it copies `.payload` and `.verified` last.

Only a missing build folder is added, nothing is replaced or removed, and the original
account's folder is only read. The app then uses that build as its fallback, exactly as it
does in the original account.
