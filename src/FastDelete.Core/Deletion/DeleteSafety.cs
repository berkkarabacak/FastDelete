using FastDelete.Core.Interop;

namespace FastDelete.Core.Deletion;

/// <summary>How far the "what's inside" count has got. The confirm button stays off until this leaves <see cref="Checking"/>.</summary>
public enum ConfirmCountState
{
    Checking,
    Finished,
    Failed,
}

/// <summary>
/// Safety rules for the confirm dialog and for Retry.
/// Retry keeps the mode the user already chose. A cancelled run does not offer
/// to finish leftover folders. Drive roots and Windows system folders are refused.
/// </summary>
public static class DeleteSafety
{
    public const int MaxPathsShown = 20;

    /// <summary>Shown when the recursive count throws. The confirm button may then be used.</summary>
    public const string CountFailedMessage =
        "Could not check what's inside. Read the list above before you decide.";

    public readonly record struct RetryPlan(DeletionMode Mode, IReadOnlyList<string> Paths, bool NeedsConfirmation);

    public readonly record struct TargetGuard(bool Allowed, IReadOnlyList<string> BlockedPaths, string Explanation);

    private static readonly string[] WellKnownSystemFolders =
    [
        "Program Files (x86)",
        "Program Files",
        "Windows",
    ];

    /// <summary>Yes stays disabled while the count is still running, and forever when a target is blocked.</summary>
    public static bool CanAccept(ConfirmCountState countState, bool targetsBlocked)
        => !targetsBlocked && countState != ConfirmCountState.Checking;

    /// <summary>The paths themselves, not only a count. Long selections keep the first few and say how many more.</summary>
    public static string FormatPathList(IReadOnlyList<string> paths, int maxShown = MaxPathsShown)
    {
        if (paths.Count == 0)
            return "Nothing is selected.";

        int shown = Math.Min(paths.Count, Math.Max(1, maxShown));
        var lines = new string[shown + (paths.Count > shown ? 1 : 0)];
        for (int i = 0; i < shown; i++)
            lines[i] = paths[i];
        if (paths.Count > shown)
        {
            int extra = paths.Count - shown;
            lines[shown] = extra == 1 ? "and 1 more" : $"and {extra:N0} more";
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Retry uses <paramref name="originalMode"/>. It never switches a Recycle Bin
    /// delete to a permanent one. Directory-not-empty failures from a cancelled run
    /// are leftover folders the user just stopped, so they are not offered again.
    /// Any retry that still has paths needs a confirmation that names those paths and the mode.
    /// </summary>
    public static RetryPlan PlanRetry(DeletionMode originalMode, IReadOnlyList<DeleteFailure> failures, bool wasCancelled)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var failure in failures)
        {
            if (!failure.Retryable)
                continue;
            if (wasCancelled && failure.ErrorCode == Win32.ERROR_DIR_NOT_EMPTY)
                continue;
            if (seen.Add(failure.Path))
                paths.Add(failure.Path);
        }

        return new RetryPlan(originalMode, paths, NeedsConfirmation: paths.Count > 0);
    }

    public static TargetGuard CheckTargets(IReadOnlyList<string> paths)
        => CheckTargets(paths, DefaultProtectedDirectories());

    /// <param name="extraProtectedDirectories">
    /// Real Windows/Program Files locations from this PC (localized names included),
    /// plus any extra roots a caller wants treated the same way.
    /// </param>
    public static TargetGuard CheckTargets(IReadOnlyList<string> paths, IReadOnlyList<string> extraProtectedDirectories)
    {
        var blocked = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths)
        {
            string canonical = Canonical(raw);
            if (canonical.Length == 0)
                continue;
            bool block = IsVolumeRoot(canonical)
                || IsWellKnownSystemLocation(canonical)
                || extraProtectedDirectories.Any(dir => IsUnderDirectory(canonical, dir));
            if (block && seen.Add(canonical))
                blocked.Add(canonical);
        }

        if (blocked.Count == 0)
            return new TargetGuard(true, Array.Empty<string>(), string.Empty);

        return new TargetGuard(false, blocked, Explain(blocked));
    }

    public static string Summarize(DeletionResult result)
    {
        if (result.WasCancelled)
        {
            if (result.TotalItems == 0)
                return "Stopped. Nothing was deleted.";
            string items = result.TotalItems == 1 ? "1 item was" : $"{result.TotalItems:N0} items were";
            return $"Stopped. {items} already deleted and stay deleted. Everything else was left alone.";
        }

        if (result.Failures.Count == 0)
            return $"Done — deleted {result.TotalItems:N0} items in {result.Elapsed.TotalSeconds:F1} seconds.";

        return $"Done — deleted {result.TotalItems:N0} items in {result.Elapsed.TotalSeconds:F1} seconds; {result.Failures.Count:N0} could not be deleted.";
    }

    private static IReadOnlyList<string> DefaultProtectedDirectories()
    {
        var list = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(path))
                    list.Add(path);
            }
            catch
            {
                // Well-known folder names still apply when a special folder cannot be read.
            }
        }

        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        return list;
    }

    private static string Explain(IReadOnlyList<string> blocked)
    {
        if (blocked.Count == 1)
        {
            string path = blocked[0];
            if (IsVolumeRoot(path))
            {
                string kind = path.StartsWith(@"\\", StringComparison.Ordinal)
                    ? "a whole network location"
                    : "a whole drive";
                return $"{path} is {kind}. FastDelete will not delete it. Open the folder you actually want, select just that, and try again.";
            }

            return $"{path} is a Windows system folder. FastDelete will not delete it. Pick a normal folder you selected on purpose.";
        }

        int shown = Math.Min(blocked.Count, MaxPathsShown);
        var lineList = blocked.Take(shown).Select(p => "• " + p).ToList();
        if (blocked.Count > shown)
            lineList.Add($"• and {blocked.Count - shown:N0} more");
        string lines = string.Join(Environment.NewLine, lineList);
        return "FastDelete will not delete these. A whole drive or a Windows system folder is in the list, and one click could wipe something you need:"
            + Environment.NewLine + Environment.NewLine
            + lines
            + Environment.NewLine + Environment.NewLine
            + "Cancel, then select a normal folder you really mean to delete.";
    }

    private static bool IsWellKnownSystemLocation(string canonical)
    {
        if (canonical.Length < 4 || canonical[1] != ':' || canonical[2] != '\\')
            return false;

        string rest = canonical[3..];
        foreach (string name in WellKnownSystemFolders)
        {
            if (rest.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (rest.StartsWith(name + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsUnderDirectory(string canonical, string protectedDirectory)
    {
        string parent = Canonical(protectedDirectory);
        // A drive root must not be treated as a prefix for every folder on that drive.
        if (parent.Length == 0 || IsVolumeRoot(parent))
            return false;
        parent = parent.TrimEnd('\\');
        if (canonical.Equals(parent, StringComparison.OrdinalIgnoreCase))
            return true;
        return canonical.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVolumeRoot(string canonical)
    {
        if (canonical.Length == 3 && canonical[1] == ':' && canonical[2] == '\\')
            return true;
        if (!canonical.StartsWith(@"\\", StringComparison.Ordinal))
            return false;

        string rest = canonical[2..];
        int slash = rest.IndexOf('\\');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;
        return rest.IndexOf('\\', slash + 1) < 0;
    }

    /// <summary>Windows path form used for comparisons. Drive roots keep their trailing slash (<c>C:\</c>).</summary>
    internal static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string p = path.Trim().Trim('"').Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
        {
            p = p[4..];
            if (p.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                p = @"\\" + p[4..];
        }

        if (p.StartsWith(@"\\", StringComparison.Ordinal))
            return p.TrimEnd('\\');

        if (p.Length >= 2 && p[1] == ':')
        {
            char drive = char.ToUpperInvariant(p[0]);
            if (p.Length == 2 || (p.Length == 3 && p[2] == '\\'))
                return drive + ":\\";
            return drive + p[1..].TrimEnd('\\');
        }

        return p.TrimEnd('\\');
    }
}
