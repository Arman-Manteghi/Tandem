using Tandem.Core.Profiles;

namespace Tandem.Core.Tests;

public sealed class ProfileSetupTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private Profile NewProfile(string id) => new()
    {
        Id = id,
        DisplayName = id,
        Color = "#3B82C4",
        DataFolder = Path.Combine(_temp.Path, id),
    };

    private static string Write(Profile profile, string relative, string text, DateTime? written = null)
    {
        var path = Path.Combine(profile.DataFolder!, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (written is { } time)
        {
            File.SetLastWriteTimeUtc(path, time);
        }

        return path;
    }

    private static string Read(Profile profile, string relative) => File.ReadAllText(Path.Combine(profile.DataFolder!, relative));

    private static bool Exists(Profile profile, string relative) =>
        File.Exists(Path.Combine(profile.DataFolder!, relative)) || Directory.Exists(Path.Combine(profile.DataFolder!, relative));

    private static readonly string[] Forbidden =
    [
        "Cookies", "Local State", "Preferences", "Local Storage", "Session Storage", "IndexedDB", "Network",
        "config.json", "buddy-tokens.json", "ant-did", "ant-device-registry.json", "ccd-ids.json", "Partitions", "WebStorage",
    ];

    private void WriteSecrets(Profile source)
    {
        foreach (var secret in Forbidden)
        {
            Write(source, secret, "secret");
        }
    }

    [Fact]
    public void Manual_copy_takes_only_allow_listed_setup()
    {
        var source = NewProfile("sourcexx");
        var target = NewProfile("targetxx");
        Write(source, "claude_desktop_config.json", "{}");
        Write(source, Path.Combine("Claude Extensions", "ext1", "manifest.json"), "{}");
        Write(source, Path.Combine("claude-code-sessions", "org", "acct", "s.json"), "{}");
        WriteSecrets(source);

        var report = ProfileSetup.Copy(source, target);

        Assert.Equal(["claude_desktop_config.json", "Claude Extensions"], report.Copied.Select(c => c.RelativePath));
        Assert.True(Exists(target, Path.Combine("Claude Extensions", "ext1", "manifest.json")));
        Assert.All(Forbidden, f => Assert.False(Exists(target, f)));
        Assert.False(Exists(target, "claude-code-sessions"));
    }

    [Fact]
    public void Neither_list_contains_sign_in_or_identity_data()
    {
        var all = ProfileSetup.Allowlist.Concat(ProfileSetup.HistoryItems).Select(i => i.RelativePath);

        Assert.Empty(all.Intersect(Forbidden, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sync_copies_new_and_changed_setup_and_backs_up_what_it_replaces()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        var backup = Path.Combine(_temp.Path, "backup");
        Write(original, "claude_desktop_config.json", "{\"mcpServers\":{\"a\":{}}}");
        Write(original, "ssh_configs.json", "[1]");
        Write(target, "claude_desktop_config.json", "{}");
        Write(target, "ssh_configs.json", "[1]");
        WriteSecrets(original);

        var report = ProfileSetup.Sync(original, target, backup);

        Assert.Equal(1, report.Updated);
        Assert.Equal("{\"mcpServers\":{\"a\":{}}}", Read(target, "claude_desktop_config.json"));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(backup, "claude_desktop_config.json")));
        Assert.False(File.Exists(Path.Combine(backup, "ssh_configs.json"))); // unchanged, not touched
        Assert.All(Forbidden, f => Assert.False(Exists(target, f)));
    }

    [Fact]
    public void Sync_is_a_no_op_when_everything_already_matches()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        Write(original, "claude_desktop_config.json", "{}");
        ProfileSetup.Sync(original, target, Path.Combine(_temp.Path, "backup"));

        var second = ProfileSetup.Sync(original, target, Path.Combine(_temp.Path, "backup"));

        Assert.Equal(new FileTransferReport(0, 0), second);
    }

    [Fact]
    public void Sync_adds_to_folders_but_never_deletes_target_only_files()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        Write(original, Path.Combine("Claude Extensions", "shared", "manifest.json"), "v2", DateTime.UtcNow);
        Write(target, Path.Combine("Claude Extensions", "shared", "manifest.json"), "v1", DateTime.UtcNow.AddHours(-1));
        Write(target, Path.Combine("Claude Extensions", "only-here", "manifest.json"), "mine");

        ProfileSetup.Sync(original, target, Path.Combine(_temp.Path, "backup"));

        Assert.Equal("v2", Read(target, Path.Combine("Claude Extensions", "shared", "manifest.json")));
        Assert.Equal("mine", Read(target, Path.Combine("Claude Extensions", "only-here", "manifest.json")));
    }

    [Fact]
    public void Sync_and_import_refuse_the_original_account_as_target()
    {
        var source = NewProfile("sourcexx");
        var original = new Profile { Id = Profile.DefaultId, DisplayName = "Default", Color = "#E07A5F" };

        Assert.Throws<ProfileValidationException>(() => ProfileSetup.Sync(source, original, _temp.Path));
        Assert.Throws<ProfileValidationException>(() => ProfileSetup.ImportHistory(source, original));
        Assert.Throws<ProfileValidationException>(() => ProfileSetup.Copy(source, source));
    }

    [Fact]
    public void History_import_brings_code_and_cowork_sessions_but_nothing_else()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        Write(original, Path.Combine("claude-code-sessions", "org1", "acct2", "session.json"), "code");
        Write(original, Path.Combine("local-agent-mode-sessions", "org1", "acct2", "task.json"), "cowork");
        Write(original, "git-worktrees.json", "[\"w\"]");
        Write(original, "claude_desktop_config.json", "{}");
        WriteSecrets(original);

        var report = ProfileSetup.ImportHistory(original, target);

        Assert.Equal(3, report.Updated);
        Assert.Equal("code", Read(target, Path.Combine("claude-code-sessions", "org1", "acct2", "session.json")));
        Assert.Equal("cowork", Read(target, Path.Combine("local-agent-mode-sessions", "org1", "acct2", "task.json")));
        Assert.Equal("[\"w\"]", Read(target, "git-worktrees.json"));
        Assert.False(Exists(target, "claude_desktop_config.json"));
        Assert.All(Forbidden, f => Assert.False(Exists(target, f)));
        Assert.True(ProfileSetup.HasHistory(original));
    }

    [Fact]
    public void History_import_never_loses_newer_work_in_the_target()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        var now = DateTime.UtcNow;
        Write(original, Path.Combine("claude-code-sessions", "o", "a", "older-there.jsonl"), "short", now.AddHours(-2));
        Write(target, Path.Combine("claude-code-sessions", "o", "a", "older-there.jsonl"), "longer and newer", now);
        Write(original, Path.Combine("claude-code-sessions", "o", "a", "newer-there.jsonl"), "line1\nline2", now);
        Write(target, Path.Combine("claude-code-sessions", "o", "a", "newer-there.jsonl"), "line1", now.AddHours(-2));
        Write(original, "git-worktrees.json", "from original");
        Write(target, "git-worktrees.json", "target's own");

        ProfileSetup.ImportHistory(original, target);

        Assert.Equal("longer and newer", Read(target, Path.Combine("claude-code-sessions", "o", "a", "older-there.jsonl")));
        Assert.Equal("line1\nline2", Read(target, Path.Combine("claude-code-sessions", "o", "a", "newer-there.jsonl")));
        Assert.Equal("target's own", Read(target, "git-worktrees.json"));
    }

    [Fact]
    public void Files_the_original_app_has_open_are_still_copied()
    {
        var original = NewProfile("original");
        var target = NewProfile("targetxx");
        var path = Write(original, "claude_desktop_config.json", "{\"open\":true}");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            ProfileSetup.Sync(original, target, Path.Combine(_temp.Path, "backup"));
        }

        Assert.Equal("{\"open\":true}", Read(target, "claude_desktop_config.json"));
        Assert.Equal(File.GetLastWriteTimeUtc(path), File.GetLastWriteTimeUtc(Path.Combine(target.DataFolder!, "claude_desktop_config.json")));
        Assert.Empty(Directory.GetFiles(target.DataFolder!, "*.tandem-tmp", SearchOption.AllDirectories));
    }
}
