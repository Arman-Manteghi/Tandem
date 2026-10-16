using Tandem.Core.Profiles;

namespace Tandem.Core.Claude;

/// <summary>
/// Builds the arguments for the official app. The only switch Tandem ever passes is
/// <c>--user-data-dir</c>; the Default profile gets no switches at all.
/// </summary>
public static class LaunchArguments
{
    public const string UserDataDirPrefix = "--" + ClaudePackage.UserDataDirSwitch + "=";

    public static IReadOnlyList<string> Build(Profile profile, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var args = new List<string>(2);

        if (!profile.IsDefault)
        {
            if (string.IsNullOrWhiteSpace(profile.DataFolder))
            {
                throw new InvalidOperationException($"Profile {profile.Id} has no data folder.");
            }

            args.Add(UserDataDirPrefix + PathUtil.Normalize(profile.DataFolder));
        }

        if (link is not null)
        {
            if (!ClaudeLink.IsValid(link))
            {
                throw new ArgumentException("Only claude:// links can be passed to the app.", nameof(link));
            }

            args.Add(link);
        }

        return args;
    }
}
