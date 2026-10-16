# Security policy

Tandem runs next to the Claude desktop app and handles account folders, so security reports
are taken seriously and answered first.

## Supported versions

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ Please update first |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report them privately through GitHub:
[**Report a vulnerability**](https://github.com/Arman-Manteghi/Tandem/security/advisories/new)
(Security → Advisories → Report a vulnerability).

Please include:

- what an attacker could do, and under which conditions;
- the steps to reproduce it, with the Tandem and Claude versions and the Windows version;
- whether it needs the attacker to already run code as your Windows user.

You will get an answer within **7 days**. A fix is released as soon as it is ready, and the
advisory credits you unless you prefer otherwise.

Never include sign-in data, tokens, cookies or the contents of a Claude data folder in a report.
Tandem's logs (`%LOCALAPPDATA%\Tandem\logs`) contain no personal data and can be attached.

## What Tandem promises

These are the properties a report can hold Tandem to:

- It never reads, copies, logs or sends sign-in data, cookies, tokens or session storage.
- It makes no network connections of its own and has no telemetry or automatic updates.
- It never modifies, patches or injects into the Claude desktop app.
- It writes only to its own folders (`%APPDATA%\Tandem`, `%LOCALAPPDATA%\Tandem`), to the
  account folders it created, to its own registry keys under `HKCU` for the optional
  sign-in helper, and to shortcuts the user asks for.
- It deletes nothing permanently; removed items go to the Recycle Bin.
- `claude://` links are passed on unchanged and are never logged or stored.

## Out of scope

- Weaknesses in the Claude desktop app itself: please report those to Anthropic.
- Attacks that require administrator rights, or code already running as the same Windows
  user with full access to that user's files.
- Windows SmartScreen warnings about unsigned releases.
