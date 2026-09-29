using System.IO;
using FastDelete.App.Models;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
using TreeNode = FastDelete.App.Models.TreeNode;

namespace FastDelete.App.Services;

/// <summary>Directory-only enumeration for the navigation tree.</summary>
public static class TreeService
{
    /// <summary>When false, hidden, system, and dot folders stay out of the tree.</summary>
    public static bool IncludeHidden { get; set; }

    public static IEnumerable<TreeNode> LoadChildDirectories(string path)
    {
        List<DirectoryInfo> dirs;
        try
        {
            dirs = new DirectoryInfo(path).EnumerateDirectories()
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            yield break;
        }
        foreach (var dir in dirs)
        {
            bool hidden = (dir.Attributes & FileAttributes.Hidden) != 0;
            bool system = (dir.Attributes & FileAttributes.System) != 0;
            if (!ListingRules.ShouldShow(dir.Name, hidden, system, IncludeHidden))
                continue;
            yield return new TreeNode { Name = dir.Name, FullPath = dir.FullName };
        }
    }

    /// <summary>"C:\" becomes "C: drive" so a drive does not look like an ordinary folder.</summary>
    public static string DriveLabel(string root) => DeleteSafety.DriveDisplayName(root);

    public static IEnumerable<string> GetDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            yield return drive.RootDirectory.FullName;
    }
}
