using System.Text.Json;
using Tandem.Core.Logging;
using Tandem.Core.Storage;

namespace Tandem.Core.Profiles;

/// <summary>
/// The list of profiles, persisted to %APPDATA%\Tandem\profiles.json.
/// The Default profile always exists, always comes first and cannot be removed.
/// The store never touches profile data folders; it only records their paths.
/// </summary>
public sealed class ProfileStore
{
    public const int MaxNameLength = 40;
    private const int CurrentVersion = 1;

    private readonly AppPaths _paths;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private List<Profile> _profiles;

    private ProfileStore(AppPaths paths, TimeProvider clock, List<Profile> profiles, string? loadWarning)
    {
        _paths = paths;
        _clock = clock;
        _profiles = profiles;
        LoadWarning = loadWarning;
    }

    public event EventHandler? Changed;

    /// <summary>Set when profiles.json was unreadable and had to be recovered; shown to the user once.</summary>
    public string? LoadWarning { get; }

    public IReadOnlyList<Profile> Profiles
    {
        get
        {
            lock (_gate)
            {
                return _profiles.ToArray();
            }
        }
    }

    public Profile Default => Profiles[0];

    public static ProfileStore Load(AppPaths paths, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        string? warning = null;
        List<Profile> raw = [];
        var fileExists = File.Exists(paths.ProfilesFile);

        if (fileExists)
        {
            try
            {
                var document = JsonFile.Read<ProfilesDocument>(paths.ProfilesFile)
                    ?? throw new JsonException("The file is empty.");
                raw = document.Profiles ?? [];
            }
            catch (JsonException ex)
            {
                var aside = JsonFile.Quarantine(paths.ProfilesFile, clock.GetLocalNow());
                Log.Warn($"profiles.json was unreadable and was renamed to {Path.GetFileName(aside)}.", ex);
                raw = RecoverFromFolders(paths, clock);
                warning = $"Your profile list could not be read. It was kept as \"{Path.GetFileName(aside)}\" " +
                          $"and {raw.Count} account folder(s) were recovered. You may want to rename them.";
                fileExists = false;
            }
        }

        var normalized = Normalize(raw, paths, clock);
        var store = new ProfileStore(paths, clock, normalized, warning);
        if (!fileExists || !normalized.SequenceEqual(raw))
        {
            store.Persist(normalized);
        }

        return store;
    }

    public Profile? Find(string? id)
    {
        if (id is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public Profile Add(string displayName, string color, AccountPlan plan = AccountPlan.Unknown)
    {
        Profile profile;
        lock (_gate)
        {
            var name = ValidateName(displayName, ignoreId: null);
            var hex = ValidateColor(color);
            var id = ProfileIds.NewUnique(
                _profiles.Select(p => p.Id),
                candidate => Directory.Exists(_paths.ProfileDataDir(candidate)));
            profile = new Profile
            {
                Id = id,
                DisplayName = name,
                Color = hex,
                DataFolder = _paths.ProfileDataDir(id),
                Created = _clock.GetUtcNow(),
                Plan = Enum.IsDefined(plan) ? plan : AccountPlan.Unknown,
            };
            Commit([.. _profiles, profile]);
        }

        Log.Info($"Profile {profile.Id} added.");
        OnChanged();
        return profile;
    }

    public Profile Update(string id, string? displayName = null, string? color = null, AccountPlan? plan = null)
    {
        Profile updated;
        lock (_gate)
        {
            var index = IndexOf(id);
            var current = _profiles[index];
            updated = current with
            {
                DisplayName = displayName is null ? current.DisplayName : ValidateName(displayName, ignoreId: current.Id),
                Color = color is null ? current.Color : ValidateColor(color),
                Plan = plan is { } p && Enum.IsDefined(p) ? p : current.Plan,
            };
            if (updated == current)
            {
                return current;
            }

            var next = _profiles.ToList();
            next[index] = updated;
            Commit(next);
        }

        Log.Info($"Profile {updated.Id} updated.");
        OnChanged();
        return updated;
    }

    /// <summary>Removes the entry only. Moving its folder to the Recycle Bin is the caller's job.</summary>
    public Profile Remove(string id)
    {
        Profile removed;
        lock (_gate)
        {
            var index = IndexOf(id);
            removed = _profiles[index];
            if (removed.IsDefault)
            {
                throw new ProfileValidationException("The Default profile cannot be removed.");
            }

            var next = _profiles.ToList();
            next.RemoveAt(index);
            Commit(next);
        }

        Log.Info($"Profile {removed.Id} removed from the list.");
        OnChanged();
        return removed;
    }

    /// <summary>Validates a name and color without changing anything; returns a user-facing error or null.</summary>
    public string? Check(string displayName, string color, string? ignoreId = null)
    {
        lock (_gate)
        {
            try
            {
                ValidateName(displayName, ignoreId);
                ValidateColor(color);
                return null;
            }
            catch (ProfileValidationException ex)
            {
                return ex.Message;
            }
        }
    }

    public string SuggestColor()
    {
        lock (_gate)
        {
            return ProfilePalette.NextUnused(_profiles.Select(p => p.Color));
        }
    }

    private int IndexOf(string id)
    {
        var index = _profiles.FindIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new ProfileValidationException("This account no longer exists.");
    }

    private string ValidateName(string displayName, string? ignoreId)
    {
        var name = string.Join(' ', (displayName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            throw new ProfileValidationException("Enter a name.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ProfileValidationException($"Use at most {MaxNameLength} characters.");
        }

        if (name.Any(char.IsControl))
        {
            throw new ProfileValidationException("The name contains characters that are not allowed.");
        }

        if (_profiles.Any(p => p.Id != ignoreId && string.Equals(p.DisplayName, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new ProfileValidationException("Another account already uses this name.");
        }

        return name;
    }

    private static string ValidateColor(string color) =>
        ProfilePalette.Normalize(color) ?? throw new ProfileValidationException("Choose a valid color.");

    /// <summary>Saves first, then swaps the in-memory list, so a failed save changes nothing.</summary>
    private void Commit(List<Profile> next)
    {
        Persist(next);
        _profiles = next;
    }

    private void Persist(List<Profile> profiles) =>
        JsonFile.Write(_paths.ProfilesFile, new ProfilesDocument { Version = CurrentVersion, Profiles = profiles });

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    internal static List<Profile> Normalize(IReadOnlyList<Profile> raw, AppPaths paths, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var result = new List<Profile>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var storedDefault = raw.FirstOrDefault(p => p.IsDefault);
        result.Add(new Profile
        {
            Id = Profile.DefaultId,
            DisplayName = CleanName(storedDefault?.DisplayName) ?? "Default",
            Color = ProfilePalette.Normalize(storedDefault?.Color) ?? ProfilePalette.DefaultColor,
            DataFolder = null,
            Created = storedDefault?.Created is { } created && created != default ? created : now,
            Plan = storedDefault?.Plan ?? AccountPlan.Unknown,
        });
        seenIds.Add(Profile.DefaultId);

        foreach (var profile in raw)
        {
            if (profile.IsDefault || !ProfileIds.IsValidCustomId(profile.Id) || !seenIds.Add(profile.Id))
            {
                continue;
            }

            // An added account always lives in its own folder under Tandem's profiles root. A path
            // edited into the file by hand is not trusted: sync, sharing and delete write there.
            var folder = paths.ProfileDataDir(profile.Id);
            if (profile.DataFolder is not null && !PathUtil.AreSame(profile.DataFolder, folder))
            {
                Log.Warn($"Profile {profile.Id} pointed at a folder outside Tandem's profiles; using its own folder.");
            }

            result.Add(profile with
            {
                DisplayName = CleanName(profile.DisplayName) ?? "Account",
                Color = ProfilePalette.Normalize(profile.Color)
                        ?? ProfilePalette.NextUnused(result.Select(p => p.Color)),
                DataFolder = folder,
                Created = profile.Created == default ? now : profile.Created,
            });
        }

        return result;
    }

    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MaxNameLength)
        {
            cleaned = cleaned[..MaxNameLength].TrimEnd();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    private static List<Profile> RecoverFromFolders(AppPaths paths, TimeProvider clock)
    {
        var recovered = new List<Profile>();
        if (!Directory.Exists(paths.ProfilesRoot))
        {
            return recovered;
        }

        foreach (var dir in Directory.EnumerateDirectories(paths.ProfilesRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(dir);
            if (!ProfileIds.IsValidCustomId(id))
            {
                continue;
            }

            var created = Directory.GetCreationTimeUtc(dir);
            recovered.Add(new Profile
            {
                Id = id,
                DisplayName = $"Recovered account {recovered.Count + 1}",
                Color = ProfilePalette.NextUnused(recovered.Select(p => p.Color).Append(ProfilePalette.DefaultColor)),
                DataFolder = dir,
                Created = created == default ? clock.GetUtcNow() : new DateTimeOffset(created, TimeSpan.Zero),
            });
        }

        return recovered;
    }

    private sealed class ProfilesDocument
    {
        public int Version { get; set; } = CurrentVersion;

        public List<Profile>? Profiles { get; set; }
    }
}
