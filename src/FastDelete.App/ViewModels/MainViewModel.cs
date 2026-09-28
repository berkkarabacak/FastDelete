using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDelete.App.Models;
using TreeNode = FastDelete.App.Models.TreeNode;
using FastDelete.App.Services;
using FastDelete.Core.Deletion;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;

namespace FastDelete.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private bool _suppressHistory;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FileSystemItem> _items = new();

    [ObservableProperty]
    private ICollectionView? _itemsView;

    [ObservableProperty]
    private ObservableCollection<TreeNode> _treeRoots = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDarkTheme;   // light theme by default: higher contrast for older eyes

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private string _deleteLabel = "Delete Selected";

    /// <summary>Whether anything is highlighted - the red button greys out otherwise.</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Friendly empty-state text: "This folder is empty" / "Nothing matches…" / "".</summary>
    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    private AppSettings _settings = SettingsService.Load();

    /// <summary>One-line coaching banner, visible until the user selects something.</summary>
    public bool ShowHint => SelectedCount == 0 && !IsDeleting;

    /// <summary>Breadcrumb segments of the current folder (root + one per subfolder).</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = new();

    public sealed record BreadcrumbSegment(string Name, string Path, bool IsLast);

    // ---- deletion progress ----
    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private string _progressCurrentItem = string.Empty;

    [ObservableProperty]
    private string _progressCounters = string.Empty;

    [ObservableProperty]
    private string _progressRate = string.Empty;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _deleteModeLabel = string.Empty;

    private CancellationTokenSource? _deleteCts;
    private PauseToken? _pauseToken;
    private int _loadGeneration; // stale loads (rapid navigation) must not overwrite newer ones
    private IReadOnlyList<FileSystemItem> _lastGridSelection = Array.Empty<FileSystemItem>();

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowHint));
        UpdateSelectionStatus();
    }

    public MainViewModel()
    {
        // Quick access first - real people live in Documents/Downloads, not drive letters.
        var quick = new TreeNode { Name = "Quick access", FullPath = string.Empty };
        foreach (var (label, folder) in new (string, Environment.SpecialFolder)[]
        {
            ("Desktop", Environment.SpecialFolder.Desktop),
            ("Documents", Environment.SpecialFolder.MyDocuments),
            ("Downloads", Environment.SpecialFolder.UserProfile), // resolved below
            ("Pictures", Environment.SpecialFolder.MyPictures),
            ("Music", Environment.SpecialFolder.MyMusic),
            ("Videos", Environment.SpecialFolder.MyVideos),
        })
        {
            string path = folder == Environment.SpecialFolder.UserProfile
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) continue;
            quick.Children.Add(TreeNode.Create(label, path, Services.IconService.GetIcon(path, isDirectory: true)));
        }
        quick.IsExpanded = true;
        TreeRoots.Add(quick);

        // recent folders - where she actually cleaned up last
        var recentPaths = (_settings.RecentPaths ?? new List<string>()).Where(Directory.Exists).Take(5).ToList();
        if (recentPaths.Count > 0)
        {
            var recent = new TreeNode { Name = "Recent", FullPath = string.Empty };
            foreach (var path in recentPaths)
                recent.Children.Add(TreeNode.Create(Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path,
                    path, IconService.GetIcon(path, isDirectory: true)));
            recent.IsExpanded = true;
            TreeRoots.Add(recent);
        }

        foreach (var drive in TreeService.GetDrives())
            TreeRoots.Add(TreeNode.CreateRoot(drive));

        // restore the theme and come back to the last-used folder
        IsDarkTheme = _settings.DarkTheme;
        if (IsDarkTheme)
            Themes.ThemeManager.Apply(dark: true);
        string start = !string.IsNullOrEmpty(_settings.LastPath) && Directory.Exists(_settings.LastPath)
            ? _settings.LastPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Navigate(start);
    }

    partial void OnSearchTextChanged(string value)
    {
        ItemsView?.Refresh();
        UpdateEmptyMessage();
    }

    [RelayCommand]
    private void Navigate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            path = Path.GetFullPath(path.Trim('"'));
        }
        catch
        {
            StatusText = $"Invalid path: {path}";
            return;
        }
        if (!Directory.Exists(path))
        {
            StatusText = $"Folder not found: {path}";
            return;
        }

        if (!_suppressHistory && !string.IsNullOrEmpty(CurrentPath) &&
            !CurrentPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            _back.Push(CurrentPath);
        _forward.Clear();

        LoadDirectory(path);
        SaveLocation(); // remember the folder even if the app crashes later
    }

    /// <summary>Persists folder + theme immediately (geometry is saved on graceful close).</summary>
    private void SaveLocation()
    {
        var recent = new List<string>();
        if (!string.IsNullOrEmpty(CurrentPath))
            recent.Add(CurrentPath);
        foreach (var old in _settings.RecentPaths ?? new List<string>())
        {
            if (recent.Count >= 5) break;
            if (!recent.Contains(old, StringComparer.OrdinalIgnoreCase))
                recent.Add(old);
        }
        _settings = _settings with { LastPath = CurrentPath, DarkTheme = IsDarkTheme, RecentPaths = recent };
        SettingsService.Save(_settings);
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (!string.IsNullOrEmpty(CurrentPath))
            ExplorerIntegration.OpenInExplorer(CurrentPath);
    }

    [RelayCommand]
    private void NavigateBack()
    {
        if (_back.Count == 0) return;
        _forward.Push(CurrentPath);
        var target = _back.Pop();
        _suppressHistory = true;
        Navigate(target);
        _suppressHistory = false;
    }

    [RelayCommand]
    private void NavigateForward()
    {
        if (_forward.Count == 0) return;
        _back.Push(CurrentPath);
        var target = _forward.Pop();
        _suppressHistory = true;
        Navigate(target);
        _suppressHistory = false;
    }

    [RelayCommand]
    private void NavigateUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent != null)
            Navigate(parent.FullName);
    }

    [RelayCommand]
    private void Refresh() => LoadDirectory(CurrentPath);

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        Themes.ThemeManager.Apply(IsDarkTheme);
        SaveLocation();
    }

    private async void LoadDirectory(string path)
    {
        CurrentPath = path;
        int generation = ++_loadGeneration;
        StatusText = "Opening folder…";

        var loaded = await Task.Run(() => BrowserService.LoadItems(path));
        if (generation != _loadGeneration) return; // superseded by a newer navigation

        Items = new ObservableCollection<FileSystemItem>(loaded);
        foreach (var item in Items)
        {
            // (checkbox-less selection; kept for API compatibility)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileSystemItem.IsDirectory))
                    UpdateSelection(_lastGridSelection);
            };
        }
        var view = CollectionViewSource.GetDefaultView(Items);
        view.Filter = FilterItem;
        ItemsView = view;

        RebuildBreadcrumbs(path);

        long bytes = loaded.Where(i => i.Size.HasValue).Sum(i => i.Size!.Value);
        var what = loaded.Count == 1 ? "1 item" : $"{loaded.Count:N0} items";
        var friendly = BrowserService.LastError.Length > 0
            ? $"{what} here — {BrowserService.LastError}"
            : $"{what} here";
        StatusText = bytes > 0 ? $"{what} here ({FormatSize(bytes)})" : friendly;
        UpdateEmptyMessage();
        // a post-delete summary must survive the async refresh that follows it
        if (_statusAfterLoad is not null)
        {
            StatusText = _statusAfterLoad;
            _statusAfterLoad = null;
        }
    }

    private string? _statusAfterLoad;

    /// <summary>Shows this message once the current folder finishes reloading.</summary>
    public void SetStatusAfterLoad(string message) => _statusAfterLoad = message;

    private void UpdateEmptyMessage()
    {
        if (Items.Count == 0)
        {
            EmptyMessage = "This folder is empty";
            return;
        }
        if (!string.IsNullOrWhiteSpace(SearchText) && ItemsView is not null && ItemsView.Cast<object>().FirstOrDefault() is null)
            EmptyMessage = $"Nothing matches “{SearchText}”";
        else
            EmptyMessage = string.Empty;
    }

    /// <summary>Called by the window on close so the next run restores everything.</summary>
    public void PersistState(double width, double height, double left, double top, bool maximized)
    {
        SettingsService.Save(_settings with
        {
            Width = width,
            Height = height,
            Left = left,
            Top = top,
            Maximized = maximized,
        });
    }

    private void RebuildBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return;
        Breadcrumbs.Add(new BreadcrumbSegment(root.TrimEnd('\\', '/'), root, false));
        string rest = path[root.Length..].Trim('\\', '/');
        if (rest.Length > 0)
        {
            string walk = root;
            var parts = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                walk = Path.Combine(walk, parts[i]);
                Breadcrumbs.Add(new BreadcrumbSegment(parts[i], walk, i == parts.Length - 1));
            }
        }
        if (Breadcrumbs.Count > 0)
        {
            var last = Breadcrumbs[^1];
            Breadcrumbs[^1] = last with { IsLast = true };
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not FileSystemItem item || string.IsNullOrWhiteSpace(SearchText))
            return true;
        return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Paths targeted for deletion: the currently highlighted grid rows.</summary>
    public IReadOnlyList<string> ResolveDeleteTargets(IEnumerable<FileSystemItem> gridSelection)
        => gridSelection.Select(i => i.FullPath).ToList();

    /// <summary>Called by the view whenever the grid selection changes.</summary>
    public void UpdateSelection(IReadOnlyList<FileSystemItem> gridSelection)
    {
        _lastGridSelection = gridSelection;
        var effective = gridSelection.ToList();
        SelectedCount = effective.Count;
        SelectedBytes = effective.Where(i => i.Size.HasValue).Sum(i => i.Size!.Value);
        HasSelection = effective.Count > 0;
    }

    private void UpdateSelectionStatus()
    {
        DeleteLabel = SelectedCount == 0 ? "Delete Selected" : $"Delete Selected ({SelectedCount:N0})";
        if (SelectedCount == 0)
        {
            // keep folder summary; nothing else to do
            return;
        }
        var what = SelectedCount == 1 ? "1 item selected" : $"{SelectedCount:N0} items selected";
        StatusText = SelectedBytes > 0 ? $"{what} ({FormatSize(SelectedBytes)})" : what;
    }

    /// <summary>Runs the deletion engine (permanent) or the recycle-bin path; reports progress.</summary>
    public async Task<DeletionResult> DeleteAsync(IReadOnlyList<string> paths, DeletionMode mode)
    {
        IsDeleting = true;
        IsPaused = false;
        DeleteModeLabel = mode == DeletionMode.RecycleBin ? "Moving to the Recycle Bin…" : "Deleting… please wait";
        OnPropertyChanged(nameof(ShowHint));
        _deleteCts = new CancellationTokenSource();
        _pauseToken = new PauseToken();

        var progress = new Progress<DeletionProgress>(p =>
        {
            ProgressCurrentItem = p.CurrentItem;
            var parts = new List<string>
            {
                $"{p.FilesDeleted:N0} files",
                $"{p.DirectoriesDeleted:N0} folders",
            };
            if (p.LinksDeleted > 0) parts.Add($"{p.LinksDeleted:N0} shortcuts");
            if (p.Failed > 0) parts.Add($"{p.Failed:N0} could not be deleted");
            ProgressCounters = "Deleted: " + string.Join(", ", parts);
            ProgressRate = $"{p.ItemsPerSecond:N0} items per second   •   {p.Elapsed:hh\\:mm\\:ss} elapsed";
        });

        try
        {
            return mode == DeletionMode.RecycleBin
                ? await RecycleBinDeleter.DeleteAsync(paths, _deleteCts.Token)
                : await new DeletionEngine().DeleteAsync(paths, progress, _deleteCts.Token, _pauseToken);
        }
        finally
        {
            IsDeleting = false;
            _deleteCts = null;
            _pauseToken = null;
            OnPropertyChanged(nameof(ShowHint));
        }
    }

    [RelayCommand]
    private void CancelDelete() => _deleteCts?.Cancel();

    [RelayCommand]
    private void TogglePause()
    {
        if (_pauseToken == null) return;
        if (IsPaused) _pauseToken.Resume(); else _pauseToken.Pause();
        IsPaused = !IsPaused;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):F2} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B",
    };
}
