using Tandem.Core.Profiles;

namespace Tandem.Core.Claude;

public static class ProfileMatcher
{
    /// <summary>
    /// Maps a running instance's <c>--user-data-dir</c> to a profile: none → Default; a known
    /// folder → that profile; anything else → null (a Claude window Tandem does not manage).
    /// </summary>
    public static Profile? Match(IReadOnlyList<Profile> profiles, string? userDataDir)
    {
        if (string.IsNullOrWhiteSpace(userDataDir))
        {
            return profiles.FirstOrDefault(p => p.IsDefault);
        }

        return profiles.FirstOrDefault(p => !p.IsDefault && PathUtil.AreSame(p.DataFolder, userDataDir));
    }
}
