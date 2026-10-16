using Tandem.Core.Claude;
using Tandem.Core.Logging;
using Tandem.Core.Safety;
using Tandem.Core.Shell;

namespace Tandem.Core.Profiles;

/// <summary>One allow-listed piece of the desktop app's local data.</summary>
/// <param name="ShortName">Phrase used inside sentences, e.g. "MCP tool toggles".</param>
public sealed record SetupItem(string RelativePath, bool IsFolder, string Description, string ShortName);

public sealed record SetupCopyReport(IReadOnlyList<SetupItem> Copied, int ReplacedCount);

/// <param name="Updated">Files copied because they were new or changed in the source.</param>
/// <param name="Skipped">Files that could not be read or written (for example locked); left as they were.</param>
public sealed record FileTransferReport(int Updated, int Skipped);

/// <summary>
/// Makes an added account's app look like the original one. Two explicit allowlists, copied
/// byte for byte and never parsed (see docs/FINDINGS.md for how they were chosen):
/// <list type="bullet">
/// <item><see cref="Allowlist"/>: the app's local setup (MCP servers, extensions, preferences…),
/// kept the same as the original account before every launch.</item>
/// <item><see cref="HistoryItems"/>: Claude Code and Cowork history, which Claude stores per
/// account inside the data folder; brought over once so it shows up like it does in the original app.</item>
/// </list>
/// Sign-in data, cookies, tokens, web storage and device identity are on neither list, so they
/// are never copied. Nothing is ever deleted: replaced setup files are backed up first.
/// </summary>
public static class ProfileSetup
{
    private const int CompareBytesLimit = 4 * 1024 * 1024;

    public static IReadOnlyList<SetupItem> Allowlist { get; } =
    [
        new("claude_desktop_config.json", false, "MCP servers and app preferences", "MCP servers and preferences"),
        new("mcp-user-tool-toggles.json", false, "Which MCP tools are switched on", "MCP tool toggles"),
        new("Claude Extensions", true, "Installed desktop extensions", "desktop extensions"),
        new("Claude Extensions Settings", true, "Settings of desktop extensions", "extension settings"),
        new("ssh_configs.json", false, "SSH connections for the Code tab", "SSH connections"),
        new("ccd-session-presets.json", false, "Code tab session presets", "Code tab presets"),
    ];

    /// <summary>Per-account history. Claude keeps one sub-folder per account inside these folders.</summary>
    public static IReadOnlyList<SetupItem> HistoryItems { get; } =
    [
        new("claude-code-sessions", true, "Claude Code sessions", "Claude Code sessions"),
        new("local-agent-mode-sessions", true, "Cowork sessions and skills", "Cowork sessions"),
        new("git-worktrees.json", false, "Git worktrees used by Code sessions", "Code worktrees"),
    ];

    /// <summary>The setup items that exist in <paramref name="source"/>.</summary>
    public static IReadOnlyList<SetupItem> Available(Profile source) =>
        Allowlist.Where(item => Locate(source, item) is not null).ToList();

    public static bool HasHistory(Profile source) =>
        HistoryItems.Any(item => Locate(source, item) is not null);

    /// <summary>
    /// Manual "copy app setup from another account": replaces the target's setup items, moving
    /// the old ones to the Recycle Bin. The target must not be running.
    /// </summary>
    public static SetupCopyReport Copy(Profile source, Profile target, IntPtr ownerWindow = default)
    {
        RequireDistinctAddedTarget(source, target);
        ProfileFolder.EnsureReady(target.DataFolder!);
        var copied = new List<SetupItem>();
        var replaced = 0;

        foreach (var item in Allowlist)
        {
            var from = Locate(source, item);
            if (from is null)
            {
                continue;
            }

            var to = Path.Combine(target.DataFolder!, item.RelativePath);
            if (File.Exists(to) || Directory.Exists(to))
            {
                if (RecycleBin.Move(to, ownerWindow) != RecycleResult.Moved)
                {
                    throw new IOException($"\"{item.RelativePath}\" in the target account could not be moved to the Recycle Bin, so it was not replaced.");
                }

                replaced++;
            }

            if (item.IsFolder)
            {
                Merge(from, to, overwrite: (_, _) => false, backup: null);
            }
            else
            {
                CopyShared(from, to);
            }

            copied.Add(item);
        }

        Log.Info($"Copied setup from profile {source.Id} to {target.Id}: {string.Join(", ", copied.Select(c => c.RelativePath))}; replaced {replaced}.");
        return new SetupCopyReport(copied, replaced);
    }

    /// <summary>
    /// Automatic sync before a launch: every setup item that is new or different in the original
    /// account is copied into the target. The previous version of each replaced file is kept in
    /// <paramref name="backupDir"/>. Files that exist only in the target are left alone.
    /// The target must not be running.
    /// </summary>
    public static FileTransferReport Sync(Profile original, Profile target, string backupDir)
    {
        RequireDistinctAddedTarget(original, target);
        ProfileFolder.EnsureReady(target.DataFolder!);
        var updated = 0;
        var skipped = 0;

        foreach (var item in Allowlist)
        {
            var from = Locate(original, item);
            if (from is null)
            {
                continue;
            }

            var to = Path.Combine(target.DataFolder!, item.RelativePath);
            var backup = Path.Combine(backupDir, item.RelativePath);
            if (item.IsFolder)
            {
                var (u, s) = Merge(from, to, overwrite: Differs, backup: backup);
                updated += u;
                skipped += s;
            }
            else if (!File.Exists(to) || !SameContent(from, to))
            {
                if (TryTransfer(from, to, File.Exists(to) ? backup : null))
                {
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        if (updated > 0 || skipped > 0)
        {
            Log.Info($"Synced setup from profile {original.Id} to {target.Id}: {updated} updated, {skipped} skipped.");
        }

        return new FileTransferReport(updated, skipped);
    }

    /// <summary>
    /// Brings Claude Code and Cowork history from the original account's folder. Files missing in
    /// the target are copied; a file present in both is only replaced by a newer and longer copy.
    /// Single files (such as the worktree list) are copied only when the target has none.
    /// The target must not be running.
    /// </summary>
    public static FileTransferReport ImportHistory(Profile original, Profile target)
    {
        RequireDistinctAddedTarget(original, target);
        ProfileFolder.EnsureReady(target.DataFolder!);
        var updated = 0;
        var skipped = 0;

        foreach (var item in HistoryItems)
        {
            var from = Locate(original, item);
            if (from is null)
            {
                continue;
            }

            var to = Path.Combine(target.DataFolder!, item.RelativePath);
            if (item.IsFolder)
            {
                var (u, s) = Merge(from, to, overwrite: NewerAndLonger, backup: null);
                updated += u;
                skipped += s;
            }
            else if (!File.Exists(to))
            {
                if (TryTransfer(from, to, null))
                {
                    updated++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        Log.Info($"Imported history from profile {original.Id} to {target.Id}: {updated} files, {skipped} skipped.");
        return new FileTransferReport(updated, skipped);
    }

    /// <summary>
    /// Data folder roots to look in. For Default this is Claude's own folder; on some machines
    /// MSIX keeps it (or part of it) in the package's LocalCache instead of %APPDATA%.
    /// </summary>
    internal static IEnumerable<string> SourceRoots(Profile profile)
    {
        if (!profile.IsDefault)
        {
            yield return profile.DataFolder!;
            yield break;
        }

        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Packages", ClaudePackage.FamilyName, "LocalCache", "Roaming", "Claude");
        yield return Path.Combine(roaming, "Claude");
        yield return Path.Combine(local, "Claude-Data");
    }

    private static void RequireDistinctAddedTarget(Profile source, Profile target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsDefault || string.IsNullOrWhiteSpace(target.DataFolder))
        {
            throw new ProfileValidationException("Settings can only be copied into an added account.");
        }

        if (source.Id == target.Id)
        {
            throw new ProfileValidationException("Choose a different account to copy from.");
        }
    }

    private static string? Locate(Profile profile, SetupItem item)
    {
        foreach (var root in SourceRoots(profile))
        {
            var path = Path.Combine(root, item.RelativePath);
            if (item.IsFolder ? Directory.Exists(path) : File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Copies files from <paramref name="from"/> into <paramref name="to"/>; never deletes anything.</summary>
    private static (int Updated, int Skipped) Merge(string from, string to, Func<FileInfo, FileInfo, bool> overwrite, string? backup)
    {
        var updated = 0;
        var skipped = 0;
        IEnumerable<FileSystemInfo> entries;
        try
        {
            Directory.CreateDirectory(to);
            entries = new DirectoryInfo(from).EnumerateFileSystemInfos().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 1);
        }

        foreach (var entry in entries)
        {
            // Never follow links or junctions out of the folder.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var destination = Path.Combine(to, entry.Name);
            if (entry is DirectoryInfo dir)
            {
                var (u, s) = Merge(dir.FullName, destination, overwrite, backup is null ? null : Path.Combine(backup, entry.Name));
                updated += u;
                skipped += s;
                continue;
            }

            var source = (FileInfo)entry;
            var target = new FileInfo(destination);
            if (target.Exists && !overwrite(source, target))
            {
                continue;
            }

            if (TryTransfer(source.FullName, destination, target.Exists && backup is not null ? Path.Combine(backup, entry.Name) : null))
            {
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        return (updated, skipped);
    }

    private static bool Differs(FileInfo source, FileInfo target) =>
        source.Length != target.Length || source.LastWriteTimeUtc > target.LastWriteTimeUtc.AddSeconds(1);

    private static bool NewerAndLonger(FileInfo source, FileInfo target) =>
        source.LastWriteTimeUtc > target.LastWriteTimeUtc.AddSeconds(1) && source.Length >= target.Length;

    private static bool SameContent(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length)
        {
            return false;
        }

        if (fa.Length > CompareBytesLimit)
        {
            return fa.LastWriteTimeUtc <= fb.LastWriteTimeUtc.AddSeconds(1);
        }

        try
        {
            return ReadShared(a).AsSpan().SequenceEqual(ReadShared(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // Unreadable right now: leave the target alone.
        }
    }

    private static bool TryTransfer(string from, string to, string? backup)
    {
        try
        {
            if (backup is not null && File.Exists(to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(to, backup, overwrite: true);
            }

            CopyShared(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies a file the original app may have open (shared read), through a temporary file so the
    /// target is never half-written, and keeps the source's modification time.
    /// </summary>
    internal static void CopyShared(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        var temp = to + ".tandem-tmp";
        var written = File.GetLastWriteTimeUtc(from);
        try
        {
            using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
            }

            File.Move(temp, to, overwrite: true);
            File.SetLastWriteTimeUtc(to, written);
        }
        finally
        {
            // Only Tandem's own temporary file, if the copy did not complete.
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
