using System.Security.Cryptography;
using Tandem.Core.Claude;
using Tandem.Core.Profiles;

namespace Tandem.Core.Tests;

public sealed class ClaudeCodeShareTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Root(string name) => Directory.CreateDirectory(Path.Combine(_temp.Path, name)).FullName;

    private Profile Added(string name) => new() { Id = name.PadRight(8, 'x')[..8], DisplayName = name, Color = "#3B82C4", DataFolder = Root(name) };

    /// <summary>Lays out a build the way the Claude app does once a download checked out.</summary>
    private static string Build(string dataRoot, string version, string id, string content, bool verified = true, string? recordedHash = null)
    {
        var folder = Directory.CreateDirectory(Path.Combine(dataRoot, ClaudeCodeShare.FolderName, version, id)).FullName;
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(Path.Combine(folder, "claude.exe"), bytes);
        var hash = recordedHash ?? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(folder, ".payload"), $$"""{ "sha256": "{{hash}}", "size": {{bytes.Length}} }""");
        if (verified)
        {
            File.WriteAllText(Path.Combine(folder, ".verified"), id + new string('0', 52));
        }

        return folder;
    }

    [Fact]
    public void Only_complete_verified_builds_are_found_newest_first()
    {
        var root = Root("source");
        Build(root, "2.1.293", "aaaaaaaaaaaa", "old");
        Build(root, "2.1.295", "bbbbbbbbbbbb", "newer");
        Build(root, "2.1.299", "cccccccccccc", "unverified", verified: false);
        Directory.CreateDirectory(Path.Combine(root, ClaudeCodeShare.FolderName, "2.1.300", "dddddddddddd"));
        Build(root, "latest", "eeeeeeeeeeee", "not a version");
        var wrongSize = Build(root, "2.1.296", "ffffffffffff", "size");
        File.WriteAllText(Path.Combine(wrongSize, "claude.exe"), "a different length");

        var builds = ClaudeCodeShare.Find(root);

        Assert.Equal(new[] { "2.1.295", "2.1.293" }, builds.Select(b => b.VersionText));
        Assert.Empty(ClaudeCodeShare.Find(Root("empty")));
    }

    [Fact]
    public void An_account_without_a_build_gets_the_newest_one()
    {
        var source = Root("source");
        Build(source, "2.1.293", "aaaaaaaaaaaa", "old");
        var newest = Build(source, "2.1.295", "bbbbbbbbbbbb", "newer");
        var target = Added("target");
        // A failed download leaves an empty folder for the version it wanted.
        Directory.CreateDirectory(Path.Combine(target.DataFolder!, ClaudeCodeShare.FolderName, "2.1.299", "cccccccccccc"));

        var added = ClaudeCodeShare.Provide(target, [source]);

        Assert.NotNull(added);
        Assert.Equal("2.1.295", added.VersionText);
        var folder = Path.Combine(target.DataFolder!, ClaudeCodeShare.FolderName, "2.1.295", "bbbbbbbbbbbb");
        Assert.Equal(folder, added.Folder);
        foreach (var file in new[] { "claude.exe", ".payload", ".verified" })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(newest, file)), File.ReadAllBytes(Path.Combine(folder, file)));
        }

        Assert.Equal("2.1.295", ClaudeCodeShare.Find(target.DataFolder!).First().VersionText);

        // The source is left exactly as it was, and a second run changes nothing.
        Assert.Equal("newer", File.ReadAllText(Path.Combine(newest, "claude.exe")));
        Assert.Null(ClaudeCodeShare.Provide(target, [source]));
    }

    [Fact]
    public void An_account_with_the_same_or_a_newer_build_is_left_alone()
    {
        var source = Root("source");
        Build(source, "2.1.295", "bbbbbbbbbbbb", "newer");
        var same = Added("same");
        Build(same.DataFolder!, "2.1.295", "bbbbbbbbbbbb", "newer");
        var ahead = Added("ahead");
        Build(ahead.DataFolder!, "2.1.299", "cccccccccccc", "newest");

        Assert.Null(ClaudeCodeShare.Provide(same, [source]));
        Assert.Null(ClaudeCodeShare.Provide(ahead, [source]));
        Assert.Single(ClaudeCodeShare.Find(ahead.DataFolder!));
    }

    [Fact]
    public void A_build_that_does_not_match_its_checksum_is_not_shared()
    {
        var source = Root("source");
        Build(source, "2.1.295", "bbbbbbbbbbbb", "tampered", recordedHash: new string('a', 64));
        var target = Added("target");

        Assert.Null(ClaudeCodeShare.Provide(target, [source]));
        Assert.False(Directory.Exists(Path.Combine(target.DataFolder!, ClaudeCodeShare.FolderName)));
    }

    [Fact]
    public void A_folder_that_already_holds_files_is_never_overwritten()
    {
        var source = Root("source");
        Build(source, "2.1.295", "bbbbbbbbbbbb", "newer");
        var target = Added("target");
        var partial = Directory.CreateDirectory(Path.Combine(target.DataFolder!, ClaudeCodeShare.FolderName, "2.1.295", "bbbbbbbbbbbb")).FullName;
        File.WriteAllText(Path.Combine(partial, "claude.exe"), "half a download");

        Assert.Null(ClaudeCodeShare.Provide(target, [source]));
        Assert.Equal("half a download", File.ReadAllText(Path.Combine(partial, "claude.exe")));
        Assert.False(File.Exists(Path.Combine(partial, ".verified")));
    }

    [Fact]
    public void An_interrupted_copy_of_the_same_bytes_is_completed()
    {
        var source = Root("source");
        var newest = Build(source, "2.1.295", "bbbbbbbbbbbb", "newer");
        var target = Added("target");
        var folder = Directory.CreateDirectory(Path.Combine(target.DataFolder!, ClaudeCodeShare.FolderName, "2.1.295", "bbbbbbbbbbbb")).FullName;
        File.Copy(Path.Combine(newest, "claude.exe"), Path.Combine(folder, "claude.exe"));

        Assert.NotNull(ClaudeCodeShare.Provide(target, [source]));
        Assert.Equal("2.1.295", Assert.Single(ClaudeCodeShare.Find(target.DataFolder!)).VersionText);
    }

    [Fact]
    public void The_original_account_is_never_given_anything()
    {
        var source = Root("source");
        Build(source, "2.1.295", "bbbbbbbbbbbb", "newer");
        var original = new Profile { Id = Profile.DefaultId, DisplayName = "Original", Color = "#E07A5F" };

        Assert.Null(ClaudeCodeShare.Provide(original, [source]));
    }

    [Fact]
    public void The_newest_build_wins_across_accounts_and_a_target_never_copies_from_itself()
    {
        var first = Root("first");
        Build(first, "2.1.293", "aaaaaaaaaaaa", "old");
        var second = Root("second");
        Build(second, "2.1.296", "dddddddddddd", "newest");
        var target = Added("target");

        var added = ClaudeCodeShare.Provide(target, [first, target.DataFolder!, second]);

        Assert.Equal("2.1.296", added!.VersionText);
    }
}
