using Tandem.Core.Logging;
using Tandem.Core.Profiles;
using Tandem.Core.Safety;
using Tandem.Core.Settings;

namespace Tandem.Core.Claude;

/// <summary>
/// Starting an account the way every entry point should: an added account first gets a working
/// Claude Code program if it has none, and when it is closed, the original account's current app
/// setup (if that option is on); then the official app starts with the account's folder.
/// </summary>
public static class AccountLauncher
{
    /// <param name="prepare">False when Tandem must not write files (started inside Claude's package).</param>
    public static LaunchResult Launch(
        Profile profile, IReadOnlyList<Profile> profiles, AppPaths paths, AppSettings settings, string? link = null, bool prepare = true)
    {
        if (prepare)
        {
            Prepare(profile, profiles, paths, settings);
        }

        return ClaudeLauncher.Launch(profile, link);
    }

    /// <summary>Gets an added account ready to start. Never blocks the launch.</summary>
    public static void Prepare(Profile profile, IReadOnlyList<Profile> profiles, AppPaths paths, AppSettings settings)
    {
        if (profile.IsDefault)
        {
            return;
        }

        // Only adds a missing build, so it is safe while the account runs too.
        try
        {
            ClaudeCodeShare.Provide(profile, profiles);
        }
        catch (Exception ex)
        {
            Log.Warn($"Giving profile {profile.Id} Claude Code failed; starting it anyway.", ex);
        }

        if (!settings.SyncSetupFromOriginal || IsRunning(profile, profiles))
        {
            return;
        }

        var original = profiles.First(p => p.IsDefault);
        try
        {
            ProfileSetup.Sync(original, profile, paths.ProfileBackupDir(profile.Id));
        }
        catch (VirtualizedFolderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"Setup sync for profile {profile.Id} failed; starting it anyway.", ex);
        }
    }

    public static bool IsRunning(Profile profile, IReadOnlyList<Profile> profiles) =>
        ClaudeProcessInspector.FindMainProcesses().Any(p => ProfileMatcher.Match(profiles, p.UserDataDir)?.Id == profile.Id);
}
