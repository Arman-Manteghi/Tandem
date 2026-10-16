using Tandem.Core.Logging;

namespace Tandem.Core.Safety;

public enum ProfileFolderStatus
{
    Existing,
    Created,
}

/// <summary>Thrown when a profile folder would end up inside Claude's private package storage.</summary>
public sealed class VirtualizedFolderException(string folder, string mirror)
    : Exception("This profile folder is being redirected into the Claude app's private storage by Windows. " +
                "This happens when Tandem is started from inside Claude (for example from its terminal). " +
                "Start Tandem from the Start menu or Explorer instead.")
{
    public string Folder { get; } = folder;

    public string Mirror { get; } = mirror;
}

public static class ProfileFolder
{
    /// <summary>
    /// Creates the folder from Tandem's own process before Claude ever sees it, then checks that
    /// Windows did not virtualize it. Never opens anything inside the folder.
    /// </summary>
    public static ProfileFolderStatus EnsureReady(string dataFolder)
    {
        var full = PathUtil.Normalize(dataFolder);
        var existed = Directory.Exists(full);
        if (!existed)
        {
            Directory.CreateDirectory(full);
        }

        var mirror = MsixVirtualization.GetPackageMirror(full);
        if (mirror is not null && Directory.Exists(mirror))
        {
            Log.Warn("A profile folder has a mirror in Claude's package storage; refusing to launch.");
            throw new VirtualizedFolderException(full, mirror);
        }

        if (!existed)
        {
            Log.Info("Created a profile data folder.");
        }

        return existed ? ProfileFolderStatus.Existing : ProfileFolderStatus.Created;
    }
}
