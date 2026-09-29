using System.IO;
using FastDelete.App.Models;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;

namespace FastDelete.App.Services;

/// <summary>Filesystem reads for the browser shell. Enumeration errors surface as
/// empty results with the status message captured for the status bar.</summary>
public static class BrowserService
{
    public static string LastError { get; private set; } = string.Empty;

    /// <summary>Children of a directory, one level, dirs-first then files, both name-sorted.
    /// Hidden and dot names stay out unless <paramref name="includeHidden"/> is set.
    /// Folder sizes are filled in later so opening a folder does not walk every subtree.</summary>
    public static List<FileSystemItem> LoadItems(string path, bool includeHidden = false)
    {
        LastError = string.Empty;
        var items = new List<FileSystemItem>(1024);
        try
        {
            foreach (var dir in EnumerateSafe(path, directories: true))
            {
                if (!ListingRules.ShouldShow(dir.Name, IsHidden(dir), IsSystem(dir), includeHidden))
                    continue;
                bool link = (dir.Attributes & FileAttributes.ReparsePoint) != 0;
                items.Add(new FileSystemItem
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    IsDirectory = true,
                    IsReparsePoint = link,
                    SizeLabel = link ? "Shortcut" : "Checking…",
                    Modified = dir.LastWriteTime,
                    Icon = IconService.GetIcon(dir.FullName, isDirectory: true),
                });
            }
            foreach (var file in EnumerateSafe(path, directories: false))
            {
                if (!ListingRules.ShouldShow(file.Name, IsHidden(file), IsSystem(file), includeHidden))
                    continue;
                long size = file is FileInfo fi ? fi.Length : 0;
                items.Add(new FileSystemItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    IsReparsePoint = (file.Attributes & FileAttributes.ReparsePoint) != 0,
                    Size = size,
                    SizeLabel = FolderSizeLabel.ByteText(size),
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

    private static bool IsHidden(FileSystemInfo info) => (info.Attributes & FileAttributes.Hidden) != 0;
    private static bool IsSystem(FileSystemInfo info) => (info.Attributes & FileAttributes.System) != 0;
}
