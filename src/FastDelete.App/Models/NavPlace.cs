using CommunityToolkit.Mvvm.ComponentModel;

namespace FastDelete.App.Models;

/// <summary>One row in the left list: This PC, a drive, a section, or one of your folders.</summary>
public partial class NavPlace : ObservableObject
{
    public required string Label { get; init; }
    public string FullPath { get; init; } = "";
    public bool IsHeader { get; init; }
    public bool IsComputer { get; init; }
    public bool IsDrive { get; init; }
    public bool IsFolder { get; init; }

    [ObservableProperty]
    private bool _isActive;
}
