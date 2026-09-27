using System.IO;
using FastDelete.App.Models;
using TreeNode = FastDelete.App.Models.TreeNode;

namespace FastDelete.App.Services;

/// <summary>Directory-only enumeration for the navigation tree.</summary>
public static class TreeService
{
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
            yield return new TreeNode { Name = dir.Name, FullPath = dir.FullName };
        }
    }

    public static IEnumerable<string> GetDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            yield return drive.RootDirectory.FullName;
    }
}
