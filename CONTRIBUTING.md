# Contributing to Tandem

Thanks for helping. Bug reports, ideas and pull requests are all welcome.

## Ground rules

Tandem's users trust it with their accounts. Every change has to keep these rules; a pull
request that breaks one cannot be merged, however useful it is:

1. **The Claude app stays exactly as shipped.** No editing its files, no DLL or script
   injection, no DevTools or remote-debugging switches. Tandem only starts the official app
   with `--user-data-dir`, and only for added accounts.
2. **Sign-in data is never touched.** No reading, copying, logging, exporting or sending of
   cookies, tokens, web storage or session data from any data folder.
3. **No network connections, no telemetry, no auto-update.**
4. **No administrator rights.** Registry writes are limited to Tandem's own keys under `HKCU`,
   and only after the user agreed.
5. **Nothing is deleted permanently.** Use the Recycle Bin, and ask first.
6. **Logs hold no personal data:** account ids only, never names, links or user names.

New files that Tandem copies or reads must be added to the explicit allowlists in
[`ProfileSetup.cs`](src/Tandem.Core/Profiles/ProfileSetup.cs) (or have a dedicated, documented
reader such as the usage reader), with tests.

## Reporting a bug

Open an [issue](https://github.com/Arman-Manteghi/Tandem/issues/new/choose) with:

- the Tandem version (**About** page), the Claude app version and the Windows version;
- what you did, what you expected, and what happened;
- the relevant lines of `%LOCALAPPDATA%\Tandem\logs\tandem-<date>.log`, if any.

Security problems go through the [security policy](SECURITY.md) instead.

## Development setup

Requirements: Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
(the exact version is pinned in `global.json`). Any editor works; Visual Studio and Rider open
`Tandem.sln` directly.

```powershell
dotnet build                                           # everything
dotnet test                                            # unit tests
dotnet run --project src/Tandem.App                    # run the tray app
dotnet publish src/Tandem.App -c Release -o publish    # the single Tandem.exe
```

> [!IMPORTANT]
> Start Tandem from a terminal outside the Claude app. Processes started from Claude's own
> terminal share its package sandbox; Tandem detects that and restarts itself through Explorer.

`Tandem.exe --render-previews <folder> [dark]` renders every window with sample data, without
touching real accounts. Use it to check UI changes in both themes.

## Project layout

| Folder | Contents |
|---|---|
| `src/Tandem.Core` | Everything that can be tested without a UI: accounts, launching, process inspection, link routing, setup sync, usage, Claude Code sharing |
| `src/Tandem.App` | The WPF tray app |
| `tests/Tandem.Core.Tests` | xUnit tests for Core |
| `tools/` | The icon generator and the README image builder |
| `docs/FINDINGS.md` | How the Claude app behaves on Windows; read it before changing launching or folders |

## Pull requests

- Keep one change per pull request, with a short description of what and why.
- Add or update tests for anything in `Tandem.Core`.
- The build treats warnings as errors; `dotnet build` and `dotnet test` must pass.
- Follow the existing style (`.editorconfig`): file-scoped namespaces, `var` where the type is
  obvious, comments that explain *why* rather than *what*.
- User-facing text is short, plain English, and calls the first account "your original account".
- For UI changes, attach before and after screenshots from `--render-previews`.

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
