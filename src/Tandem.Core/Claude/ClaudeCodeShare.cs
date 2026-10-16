using System.Security.Cryptography;
using System.Text.Json;
using Tandem.Core.Interop;
using Tandem.Core.Logging;
using Tandem.Core.Profiles;

namespace Tandem.Core.Claude;

/// <summary>A verified Claude Code build in a data folder: <c>claude-code\&lt;version&gt;\&lt;id&gt;\</c>.</summary>
public sealed record ClaudeCodeBuild(Version Version, string VersionText, string Id, string Folder, string Sha256, long Size);

/// <summary>
/// The Claude app downloads its Claude Code program into each data folder
/// (<c>claude-code\&lt;version&gt;\&lt;id&gt;\claude.exe</c>, with <c>.payload</c> holding its SHA-256
/// and size and <c>.verified</c> written once it checked out) and falls back to an older build on
/// disk when a download fails. Where the download server refuses the network (it answers 403 to
/// some regions and VPNs), a new account has no build at all and its Code tab cannot start.
///
/// Tandem gives an added account the newest verified build that another account already has:
/// the same bytes, checked against the recorded SHA-256 first, hard-linked (no extra disk space)
/// or copied. It only adds a missing build folder; it never replaces or removes anything, and the
/// original account's folder is only read.
/// </summary>
public static class ClaudeCodeShare
{
    public const string FolderName = "claude-code";
    private const string ProgramFile = "claude.exe";
    private const string PayloadFile = ".payload";
    private const string VerifiedFile = ".verified";
    private const long MaxMarkerBytes = 4096;
    private const string MutexName = @"Local\Tandem.ClaudeCode";

    /// <summary>The verified builds in a data folder, newest first.</summary>
    public static IReadOnlyList<ClaudeCodeBuild> Find(string dataRoot)
    {
        var root = Path.Combine(dataRoot, FolderName);
        var builds = new List<ClaudeCodeBuild>();
        if (!Directory.Exists(root))
        {
            return builds;
        }

        try
        {
            foreach (var versionDir in Directory.EnumerateDirectories(root))
            {
                var versionText = Path.GetFileName(versionDir);
                if (!Version.TryParse(versionText, out var version))
                {
                    continue;
                }

                foreach (var buildDir in Directory.EnumerateDirectories(versionDir))
                {
                    if (ReadVerified(buildDir) is { } payload)
                    {
                        builds.Add(new ClaudeCodeBuild(version, versionText, Path.GetFileName(buildDir), buildDir, payload.Sha256, payload.Size));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not list Claude Code builds: {ex.GetType().Name}.");
        }

        return builds.OrderByDescending(b => b.Version).ToList();
    }

    /// <summary>
    /// Gives an added account the newest verified build found in <paramref name="sourceRoots"/>,
    /// unless it already has one at least as new. Returns the build added, or null.
    /// </summary>
    public static ClaudeCodeBuild? Provide(Profile target, IEnumerable<string> sourceRoots)
    {
        if (target.IsDefault || string.IsNullOrWhiteSpace(target.DataFolder))
        {
            return null;
        }

        var have = Find(target.DataFolder).FirstOrDefault();
        var best = sourceRoots
            .Where(root => !PathUtil.AreSame(root, target.DataFolder))
            .SelectMany(Find)
            .OrderByDescending(b => b.Version)
            .FirstOrDefault();
        if (best is null || (have is not null && have.Version >= best.Version))
        {
            return null;
        }

        // One Tandem process at a time (the tray app and a shortcut can start together).
        using var mutex = new Mutex(false, MutexName);
        var owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            return null;
        }

        try
        {
            return Place(target, best);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>Provides from every other account's data folder (the original one included, read-only).</summary>
    public static ClaudeCodeBuild? Provide(Profile target, IEnumerable<Profile> profiles) =>
        Provide(target, profiles.Where(p => p.Id != target.Id).SelectMany(ProfileSetup.SourceRoots));

    private static ClaudeCodeBuild? Place(Profile target, ClaudeCodeBuild best)
    {
        var destination = Path.Combine(target.DataFolder!, FolderName, best.VersionText, best.Id);
        var program = Path.Combine(destination, ProgramFile);
        if (ReadVerified(destination) is not null)
        {
            return null;
        }

        // Only an empty folder, or one holding exactly these bytes (an earlier attempt that was
        // interrupted), is completed. Anything else is Claude's own and left alone.
        var entries = Directory.Exists(destination) ? Directory.GetFileSystemEntries(destination) : [];
        var hasProgram = entries.Length == 1 && string.Equals(Path.GetFileName(entries[0]), ProgramFile, StringComparison.OrdinalIgnoreCase)
            && HashMatches(program, best.Sha256);
        if (entries.Length > 0 && !hasProgram)
        {
            Log.Info($"Claude Code {best.VersionText} for profile {target.Id}: its folder is not empty, left as it is.");
            return null;
        }

        var source = Path.Combine(best.Folder, ProgramFile);
        var method = "completed";
        if (!hasProgram)
        {
            if (!HashMatches(source, best.Sha256))
            {
                Log.Warn($"Claude Code {best.VersionText} in another account does not match its checksum; not shared.");
                return null;
            }

            Directory.CreateDirectory(destination);
            method = "linked";
            if (!Kernel32.CreateHardLinkW(program, source, IntPtr.Zero))
            {
                // Another drive, or a file system without hard links: a checked copy instead.
                method = "copied";
                ProfileSetup.CopyShared(source, program);
                if (!HashMatches(program, best.Sha256))
                {
                    File.Delete(program);
                    Log.Warn($"Copying Claude Code {best.VersionText} for profile {target.Id} gave a different file; removed the copy.");
                    return null;
                }
            }
        }

        // The markers last, so Claude never sees a "verified" build without its program.
        ProfileSetup.CopyShared(Path.Combine(best.Folder, PayloadFile), Path.Combine(destination, PayloadFile));
        ProfileSetup.CopyShared(Path.Combine(best.Folder, VerifiedFile), Path.Combine(destination, VerifiedFile));
        Log.Info($"Gave profile {target.Id} Claude Code {best.VersionText} from another account ({method}).");
        return best with { Folder = destination };
    }

    private static (string Sha256, long Size)? ReadVerified(string buildDir)
    {
        try
        {
            var verified = new FileInfo(Path.Combine(buildDir, VerifiedFile));
            var payloadFile = new FileInfo(Path.Combine(buildDir, PayloadFile));
            var program = new FileInfo(Path.Combine(buildDir, ProgramFile));
            if (!verified.Exists || verified.Length is 0 or > MaxMarkerBytes
                || !payloadFile.Exists || payloadFile.Length is 0 or > MaxMarkerBytes || !program.Exists)
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(payloadFile.FullName));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes))
            {
                return null;
            }

            var hash = sha.GetString()!;
            return hash.Length == 64 && hash.All(Uri.IsHexDigit) && bytes == program.Length ? (hash, bytes) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool HashMatches(string file, string sha256)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), sha256, StringComparison.OrdinalIgnoreCase);
    }
}
