# Privacy

**Tandem collects nothing.** It has no telemetry, no analytics, no crash reporting, no
accounts and no automatic updates, and it makes no network connections of its own. Everything
it stores stays on your computer.

## What Tandem stores

| Where | What |
|---|---|
| `%APPDATA%\Tandem` | `profiles.json` (each account's name, color, subscription and id) and `settings.json` |
| `%LOCALAPPDATA%\Tandem\Profiles\<id>` | The data folder of each added account. The Claude app writes these, like its own folder. |
| `%LOCALAPPDATA%\Tandem\Backups` | The previous version of setup files replaced by the automatic sync |
| `%LOCALAPPDATA%\Tandem` | `state.json` (which account was used last, and when), logs and account icons |

**Logs** contain account ids, never names, `claude://` links or your user name, and are
deleted after 10 days.

## What Tandem reads

- **Two switches of running Claude processes,** `--type` and `--user-data-dir`, to know which
  window belongs to which account. The rest of the command line is discarded immediately and
  never stored or logged.
- **`plan-usage-history.json`** in each account's data folder, for the Usage page: usage
  percentages, times and an organization key. It is opened read-only, its values are never
  logged, and the organization key is only compared, never shown or stored.
- **Allow-listed setup and history files** of your original account, which it copies into
  added accounts (see the [README](README.md#the-same-claude-in-every-account)).

## What Tandem never touches

Sign-in data, cookies, tokens, web storage (`Local Storage`, `IndexedDB`, …), `config.json` and
device identity files of any account are never opened, copied, logged or sent.

## `claude://` links

With the optional sign-in helper, Windows hands `claude://` links to Tandem. They can carry
one-time sign-in codes, so Tandem passes each one on unchanged, in memory only, to the right
account. Links are never logged, stored or shown.

## Your choices

- Turn off the sign-in helper at any time (**Sign-in → Turn off**).
- Turn off the automatic setup sync (**General → Same app setup in every account**).
- Delete `%APPDATA%\Tandem` and `%LOCALAPPDATA%\Tandem` to remove everything Tandem stores.

Questions: open an [issue](https://github.com/Arman-Manteghi/Tandem/issues).
