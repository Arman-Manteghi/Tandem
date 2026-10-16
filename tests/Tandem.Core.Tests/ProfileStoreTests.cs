using System.Text.Json;
using Tandem.Core.Profiles;

namespace Tandem.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDir _temp = new();
    private readonly AppPaths _paths;
    private readonly FixedClock _clock = new(T0);

    public ProfileStoreTests() => _paths = _temp.CreatePaths();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void First_load_creates_file_with_only_the_default_profile()
    {
        var store = ProfileStore.Load(_paths, _clock);

        var only = Assert.Single(store.Profiles);
        Assert.True(only.IsDefault);
        Assert.Null(only.DataFolder);
        Assert.Equal("Default", only.DisplayName);
        Assert.True(File.Exists(_paths.ProfilesFile));
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void Add_assigns_id_folder_and_created_date_and_persists()
    {
        var store = ProfileStore.Load(_paths, _clock);

        var work = store.Add("  Work   account ", "#3b82c4");

        Assert.Matches("^[a-z0-9]{8}$", work.Id);
        Assert.Equal("Work account", work.DisplayName);
        Assert.Equal("#3B82C4", work.Color);
        Assert.Equal(_paths.ProfileDataDir(work.Id), work.DataFolder);
        Assert.Equal(T0, work.Created);
        Assert.False(Directory.Exists(work.DataFolder!)); // the store never creates or touches data folders

        var reloaded = ProfileStore.Load(_paths, _clock);
        Assert.Equal(store.Profiles, reloaded.Profiles);
    }

    [Fact]
    public void Persisted_json_has_the_documented_fields()
    {
        var store = ProfileStore.Load(_paths, _clock);
        store.Add("Work", "#2A9D8F");

        using var doc = JsonDocument.Parse(File.ReadAllText(_paths.ProfilesFile));
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        var work = doc.RootElement.GetProperty("profiles")[1];
        foreach (var field in new[] { "id", "displayName", "color", "dataFolder", "created" })
        {
            Assert.True(work.TryGetProperty(field, out _), $"missing {field}");
        }

        var defaultEntry = doc.RootElement.GetProperty("profiles")[0];
        Assert.Equal("default", defaultEntry.GetProperty("id").GetString());
        Assert.False(defaultEntry.TryGetProperty("dataFolder", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This name is far too long to be accepted by Tandem")]
    [InlineData("bell\u0007char")]
    public void Add_rejects_invalid_names(string name)
    {
        var store = ProfileStore.Load(_paths, _clock);

        Assert.Throws<ProfileValidationException>(() => store.Add(name, "#3B82C4"));
        Assert.Single(store.Profiles);
    }

    [Fact]
    public void Add_collapses_whitespace_in_names()
    {
        var store = ProfileStore.Load(_paths, _clock);

        Assert.Equal("Work stuff", store.Add("Work\t \n stuff", "#3B82C4").DisplayName);
    }

    [Fact]
    public void Add_rejects_duplicate_names_ignoring_case()
    {
        var store = ProfileStore.Load(_paths, _clock);
        store.Add("Work", "#3B82C4");

        Assert.Throws<ProfileValidationException>(() => store.Add("WORK", "#2A9D8F"));
        Assert.Throws<ProfileValidationException>(() => store.Add("default", "#2A9D8F"));
    }

    [Theory]
    [InlineData("blue")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Add_rejects_invalid_colors(string color)
    {
        var store = ProfileStore.Load(_paths, _clock);

        Assert.Throws<ProfileValidationException>(() => store.Add("Work", color));
    }

    [Fact]
    public void Update_renames_and_recolors_and_keeps_id_and_folder()
    {
        var store = ProfileStore.Load(_paths, _clock);
        var work = store.Add("Work", "#3B82C4");

        var renamed = store.Update(work.Id, displayName: "Job");
        var recolored = store.Update(work.Id, color: "#d9487a");

        Assert.Equal("Job", renamed.DisplayName);
        Assert.Equal("#D9487A", recolored.Color);
        Assert.Equal(work.Id, recolored.Id);
        Assert.Equal(work.DataFolder, recolored.DataFolder);
        Assert.Equal(recolored, ProfileStore.Load(_paths, _clock).Find(work.Id));
    }

    [Fact]
    public void Default_profile_can_be_renamed_and_recolored_but_not_removed()
    {
        var store = ProfileStore.Load(_paths, _clock);

        var updated = store.Update(Profile.DefaultId, "Personal", "#64748B");

        Assert.Equal("Personal", updated.DisplayName);
        Assert.Null(updated.DataFolder);
        Assert.Throws<ProfileValidationException>(() => store.Remove(Profile.DefaultId));
    }

    [Fact]
    public void Remove_deletes_only_the_entry()
    {
        var store = ProfileStore.Load(_paths, _clock);
        var work = store.Add("Work", "#3B82C4");
        Directory.CreateDirectory(work.DataFolder!);

        store.Remove(work.Id);

        Assert.Null(store.Find(work.Id));
        Assert.Single(ProfileStore.Load(_paths, _clock).Profiles);
        Assert.True(Directory.Exists(work.DataFolder!)); // the folder is the caller's business (Recycle Bin)
    }

    [Fact]
    public void Changed_is_raised_for_every_mutation()
    {
        var store = ProfileStore.Load(_paths, _clock);
        var count = 0;
        store.Changed += (_, _) => count++;

        var work = store.Add("Work", "#3B82C4");
        store.Update(work.Id, displayName: "Job");
        store.Remove(work.Id);

        Assert.Equal(3, count);
    }

    [Fact]
    public void Ids_are_unique_and_skip_existing_folders()
    {
        var store = ProfileStore.Load(_paths, _clock);

        var ids = Enumerable.Range(0, 30).Select(i => store.Add($"Account {i}", "#3B82C4").Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Corrupt_file_is_kept_aside_and_profiles_are_recovered_from_folders()
    {
        Directory.CreateDirectory(_paths.RoamingDir);
        File.WriteAllText(_paths.ProfilesFile, "{ this is not json");
        Directory.CreateDirectory(_paths.ProfileDataDir("abcdefgh"));
        Directory.CreateDirectory(_paths.ProfileDataDir("Not-A-Valid-Id"));

        var store = ProfileStore.Load(_paths, _clock);

        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(_paths.RoamingDir, "profiles.corrupt-*.json"));
        Assert.Collection(
            store.Profiles,
            p => Assert.True(p.IsDefault),
            p =>
            {
                Assert.Equal("abcdefgh", p.Id);
                Assert.Equal(_paths.ProfileDataDir("abcdefgh"), p.DataFolder);
            });
    }

    [Fact]
    public void Load_repairs_hand_edited_files()
    {
        Directory.CreateDirectory(_paths.RoamingDir);
        File.WriteAllText(_paths.ProfilesFile, """
            {
              "version": 1,
              "profiles": [
                { "id": "abcdefgh", "displayName": "Work", "color": "not-a-color" },
                { "id": "abcdefgh", "displayName": "Duplicate", "color": "#3B82C4" },
                { "id": "../escape", "displayName": "Bad id", "color": "#3B82C4" },
                { "id": "default", "displayName": "Mine", "color": "#64748b", "dataFolder": "C:\\elsewhere" }
              ]
            }
            """);

        var store = ProfileStore.Load(_paths, _clock);

        Assert.Equal(["default", "abcdefgh"], store.Profiles.Select(p => p.Id));
        Assert.Equal("Mine", store.Default.DisplayName);
        Assert.Equal("#64748B", store.Default.Color);
        Assert.Null(store.Default.DataFolder);
        var work = store.Profiles[1];
        Assert.NotNull(ProfilePalette.Normalize(work.Color));
        Assert.Equal(_paths.ProfileDataDir("abcdefgh"), work.DataFolder);
    }

    [Fact]
    public void An_added_account_always_uses_its_own_folder()
    {
        // A folder edited into the file must never become a target for sync, sharing or delete.
        var original = Path.Combine(_temp.Path, "Roaming", "Claude");
        Directory.CreateDirectory(_paths.RoamingDir);
        File.WriteAllText(_paths.ProfilesFile, JsonSerializer.Serialize(new
        {
            version = 1,
            profiles = new[] { new { id = "abcdefgh", displayName = "Work", color = "#3B82C4", dataFolder = original } },
        }));

        var store = ProfileStore.Load(_paths, _clock);

        Assert.Equal(_paths.ProfileDataDir("abcdefgh"), store.Find("abcdefgh")!.DataFolder);
        Assert.Equal(_paths.ProfileDataDir("abcdefgh"), ProfileStore.Load(_paths, _clock).Find("abcdefgh")!.DataFolder);
    }

    [Fact]
    public void Saving_works_while_another_process_reads_the_file()
    {
        var store = ProfileStore.Load(_paths, _clock);
        using (new FileStream(_paths.ProfilesFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            store.Add("Work", "#3B82C4");
        }

        Assert.Equal(2, ProfileStore.Load(_paths, _clock).Profiles.Count);
    }

    [Fact]
    public void Failed_save_leaves_the_list_unchanged()
    {
        var store = ProfileStore.Load(_paths, _clock);
        using (File.Open(_paths.ProfilesFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => store.Add("Work", "#3B82C4"));
        }

        Assert.Single(store.Profiles);
    }

    [Fact]
    public void Suggested_color_is_the_first_unused_palette_color()
    {
        var store = ProfileStore.Load(_paths, _clock);

        Assert.Equal(ProfilePalette.Colors[1].Hex, store.SuggestColor());
        store.Add("Work", ProfilePalette.Colors[1].Hex);
        Assert.Equal(ProfilePalette.Colors[2].Hex, store.SuggestColor());
    }

    [Fact]
    public void Managed_folder_check_accepts_only_direct_children_of_the_profiles_root()
    {
        Assert.True(_paths.IsManagedProfileFolder(_paths.ProfileDataDir("abcdefgh")));
        Assert.False(_paths.IsManagedProfileFolder(_paths.ProfilesRoot));
        Assert.False(_paths.IsManagedProfileFolder(Path.Combine(_paths.ProfilesRoot, "a", "b")));
        Assert.False(_paths.IsManagedProfileFolder(_paths.LocalDir));
        Assert.False(_paths.IsManagedProfileFolder(Path.Combine(_paths.ProfilesRoot, "..", "x")));
        Assert.False(_paths.IsManagedProfileFolder(null));
    }
}
