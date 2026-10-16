using Tandem.Core.Claude;
using Tandem.Core.Processes;

namespace Tandem.Core.Safety;

public static class ProcessAncestry
{
    /// <summary>
    /// True when this process was started (directly or indirectly) by the Claude app, e.g. from
    /// its built-in terminal. Such processes inherit the package's file-system virtualization,
    /// so Tandem must not create profile folders from there.
    /// </summary>
    public static bool IsStartedFromClaude()
    {
        var current = Environment.ProcessId;
        DateTime? childStart = null;
        using (var self = ProcessQuery.Open(current))
        {
            if (self is not null)
            {
                childStart = ProcessQuery.GetStartTimeUtc(self);
            }
        }

        for (var depth = 0; depth < 16; depth++)
        {
            int? parentId;
            using (var process = ProcessQuery.Open(current))
            {
                parentId = process is null ? null : ProcessQuery.GetParentProcessId(process);
            }

            if (parentId is null or <= 4)
            {
                return false;
            }

            using var parent = ProcessQuery.Open(parentId.Value);
            if (parent is null)
            {
                return false;
            }

            // A parent id can be reused after the parent exits: a real parent started earlier.
            var parentStart = ProcessQuery.GetStartTimeUtc(parent);
            if (parentStart is null || (childStart is not null && parentStart > childStart))
            {
                return false;
            }

            if (ClaudeProcessInspector.IsClaudeAppProcess(parentId.Value))
            {
                return true;
            }

            current = parentId.Value;
            childStart = parentStart;
        }

        return false;
    }
}
