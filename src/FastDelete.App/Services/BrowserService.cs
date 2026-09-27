using System.IO;
using FastDelete.App.Models;

namespace FastDelete.App.Services;

/// <summary>Filesystem reads for the browser shell. Enumeration errors surface as
/// empty results with the status message captured for the status bar.</summary>
public static class BrowserService
{
    public static string LastError { get; private set; } = string.Empty;

    /// <summary>Children of a directory, one level, dirs-first then files, both name-sorted.</summary>
    public static List<FileSystemItem> LoadItems(string path)
    {
        LastError = string.Empty;
        var items = new List<FileSystemItem>(1024);
        try
        {
            foreach (var dir in EnumerateSafe(path, directories: true))
            {
                // NOTE: child counts are intentionally not computed here - enumerating
                // every subtree on each folder open is O(tree) and stalls the UI.
                items.Add(new FileSystemItem
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    IsDirectory = true,
                    IsReparsePoint = (dir.Attributes & FileAttributes.ReparsePoint) != 0,
                    Modified = dir.LastWriteTime,
                    Icon = IconService.GetIcon(dir.FullName, isDirectory: true),
                });
            }
            foreach (var file in EnumerateSafe(path, directories: false))
            {
                items.Add(new FileSystemItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    IsReparsePoint = (file.Attributes & FileAttributes.ReparsePoint) != 0,
                    Size = file is FileInfo fi ? fi.Length : 0,
                    Modified = file.LastWriteTime,
                    Icon = IconService.GetIcon(file.FullName, isDirectory: false),
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LastError = ex.Message;
        }
        return items;
    }

    private static IEnumerable<FileSystemInfo> EnumerateSafe(string path, bool directories)
    {
        IEnumerable<FileSystemInfo> query = directories
            ? new DirectoryInfo(path).EnumerateDirectories()
            : new DirectoryInfo(path).EnumerateFiles();
        return query.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(); // snapshot: enumerator would break on mid-run deletes
    }
}
