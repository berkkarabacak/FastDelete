using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace FastDelete.App.Models;

/// <summary>One row in the file list.</summary>
public partial class FileSystemItem : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsReparsePoint { get; init; }

    /// <summary>Shown with a Hidden mark, and only while More is ticked.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Known byte size. Null until a folder has been measured.</summary>
    [ObservableProperty]
    private long? _size;

    /// <summary>What the Size column shows: a size, "Checking…", "Empty", or "Very large".</summary>
    [ObservableProperty]
    private string _sizeLabel = "";

    public DateTime Modified { get; init; }
    public ImageSource? Icon { get; init; }

    public string TypeName => IsDirectory
        ? (IsReparsePoint ? "Shortcut link" : "Folder")
        : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext + " File" : "File";

    public override string ToString() => Name;
}
