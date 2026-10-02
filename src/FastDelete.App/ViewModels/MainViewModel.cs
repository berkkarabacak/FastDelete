using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastDelete.App.Models;
using TreeNode = FastDelete.App.Models.TreeNode;
using FastDelete.App.Services;
using FastDelete.Core;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;
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
    private string _recycleLabel = "Move to Recycle Bin";

    [ObservableProperty]
    private string _itemCountLabel = "";

    [ObservableProperty]
    private string _emptyTitle = ScreenCopy.EmptyTitle;

    [ObservableProperty]
    private string _emptyBody = ScreenCopy.EmptyBody;

    [ObservableProperty]
    private bool _includeHidden;

    [ObservableProperty]
    private string _workTitle = "";

    [ObservableProperty]
    private string _workBody = "";

    [ObservableProperty]
    private string _workCounter = "";

    [ObservableProperty]
    private string _workNow = "";

    [ObservableProperty]
    private string _workPauseLabel = "Pause";

    [ObservableProperty]
    private string _workNote = "";

    [ObservableProperty]
    private double _workFraction;

    /// <summary>Whether anything is highlighted. The big button stays grey until then.</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Friendly empty-state text: "This folder is empty" / "Nothing matches…" / "".</summary>
    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    private AppSettings _settings = SettingsService.Load();

    /// <summary>First screen: nothing of the user's is open.</summary>
    public bool ShowWelcome => string.IsNullOrEmpty(CurrentPath) && !IsDeleting;

    /// <summary>A whole drive is open. Both action buttons do nothing.</summary>
    public bool ShowDriveGuard => DeleteSafety.IsDriveRoot(CurrentPath);

    public string DriveGuardTitle => ShowDriveGuard
        ? ScreenCopy.DriveGuardTitle(DeleteSafety.DriveDisplayName(CurrentPath))
        : "";

    /// <summary>The big buttons work only when a normal folder has something chosen.</summary>
    public bool CanAct => ScreenCopy.CanAct(SelectedCount, ShowDriveGuard);

    public bool HasRows => !ShowWelcome && Items.Count > 0;

    public bool ShowEmpty => !ShowWelcome && !string.IsNullOrEmpty(CurrentPath) && Items.Count == 0;

    /// <summary>Left list: This PC, drives, then your folders.</summary>
    public ObservableCollection<NavPlace> Places { get; } = new();

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
    private CancellationTokenSource? _measureCts;
    private PauseToken? _pauseToken;
    private DeletionMode _activeMode = DeletionMode.RecycleBin;
    private long _workDone;
    private long _workTotal;
    private string _currentWorkItem = "";
    private int _loadGeneration; // stale loads (rapid navigation) must not overwrite newer ones
    private IReadOnlyList<FileSystemItem> _lastGridSelection = Array.Empty<FileSystemItem>();

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanAct));
        UpdateSelectionStatus();
    }

    partial void OnIncludeHiddenChanged(bool value)
    {
        TreeService.IncludeHidden = value;
        if (!string.IsNullOrEmpty(CurrentPath))
            LoadDirectory(CurrentPath);
    }

    public MainViewModel()
    {
        Places.Add(new NavPlace { Label = "This PC", IsComputer = true });
        foreach (var drive in TreeService.GetDrives())
            Places.Add(new NavPlace { Label = TreeService.DriveLabel(drive), FullPath = drive, IsDrive = true });
        Places.Add(new NavPlace { Label = "YOUR FOLDERS", IsHeader = true });
        foreach (var (label, folder) in new (string, Environment.SpecialFolder)[]
        {
            ("Desktop", Environment.SpecialFolder.Desktop),
            ("Documents", Environment.SpecialFolder.MyDocuments),
            ("Downloads", Environment.SpecialFolder.UserProfile),
            ("Pictures", Environment.SpecialFolder.MyPictures),
            ("Music", Environment.SpecialFolder.MyMusic),
            ("Videos", Environment.SpecialFolder.MyVideos),
        })
        {
            string path = folder == Environment.SpecialFolder.UserProfile
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Environment.GetFolderPath(folder);
            if (!Directory.Exists(path)) continue;
            Places.Add(new NavPlace { Label = label, FullPath = path, IsFolder = true });
        }

        // The window stays light. Open the last folder only when it is an ordinary one.
        // A first run, the user profile, a drive, or a Windows folder stays on the welcome screen.
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? start = _settings.LastPath;
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start)
            && !DeleteSafety.IsUnsafePlaceToOpenFirst(start, profile))
            Navigate(start);
        else
            StatusText = "Nothing is open yet.";
    }

    partial void OnCurrentPathChanged(string value)
    {
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ShowDriveGuard));
        OnPropertyChanged(nameof(DriveGuardTitle));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmpty));
        UpdateActivePlace();
    }

    partial void OnIsDeletingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    private void UpdateActivePlace()
    {
        var places = Places
            .Where(p => !p.IsHeader && p.FullPath.Length > 0)
            .Select(p => new DeleteSafety.NavPlaceRef(p.Label, p.FullPath))
            .ToList();
        string? active = DeleteSafety.ActiveNavLabel(CurrentPath, places);
        foreach (var place in Places)
            place.IsActive = active != null && place.Label == active && !place.IsHeader;
    }

    public void OpenDownloads()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(path))
        {
            StatusText = "The Downloads folder could not be found.";
            return;
        }
        Navigate(path);
    }

    /// <summary>Creates the practice folder and opens it. Does not touch photos or documents.</summary>
    public void OpenPracticeFolder()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string root = string.IsNullOrEmpty(local)
            ? Path.Combine(Path.GetTempPath(), "FastDelete", "Practice")
            : Path.Combine(local, "FastDelete", "Practice");
        PracticeFolder.Create(root);
        Navigate(root);
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

        bool includeHidden = TreeService.IncludeHidden;
        var loaded = await Task.Run(() => BrowserService.LoadItems(path, includeHidden));
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
        UpdateActivePlace();

        ItemCountLabel = ScreenCopy.ItemCount(loaded.Count);
        if (BrowserService.LastError.Length > 0)
        {
            EmptyTitle = "This folder could not be opened.";
            EmptyBody = BrowserService.LastError;
            StatusText = BrowserService.LastError;
        }
        else
        {
            EmptyTitle = ScreenCopy.EmptyTitle;
            EmptyBody = ScreenCopy.EmptyBody;
            StatusText = ItemCountLabel;
        }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmpty));
        // a post-delete summary must survive the async refresh that follows it
        if (_statusAfterLoad is not null)
        {
            StatusText = _statusAfterLoad;
            _statusAfterLoad = null;
        }

        StartMeasuringFolders(generation, loaded.Where(i => i.IsDirectory && !i.IsReparsePoint).ToList());
    }

    private void StartMeasuringFolders(int generation, List<FileSystemItem> folders)
    {
        _measureCts?.Cancel();
        _measureCts = new CancellationTokenSource();
        if (folders.Count == 0)
            return;
        var token = _measureCts.Token;
        _ = MeasureFoldersAsync(generation, folders, token);
    }

    private async Task MeasureFoldersAsync(int generation, List<FileSystemItem> folders, CancellationToken token)
    {
        foreach (var folder in folders)
        {
            if (generation != _loadGeneration || token.IsCancellationRequested)
                return;
            try
            {
                var stats = await TreeCounter.CountAsync(new[] { folder.FullPath }, token, cap: FolderSizeLabel.ItemCap);
                if (generation != _loadGeneration || token.IsCancellationRequested)
                    return;
                folder.Size = stats.Bytes;
                folder.SizeLabel = FolderSizeLabel.FromStats(stats.Bytes, stats.TotalItems);
                if (_lastGridSelection.Contains(folder))
                    UpdateSelection(_lastGridSelection);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                if (generation == _loadGeneration)
                    folder.SizeLabel = "Could not check";
            }
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
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var crumbs = DeleteSafety.Breadcrumbs(path, profile);
        for (int i = 0; i < crumbs.Count; i++)
            Breadcrumbs.Add(new BreadcrumbSegment(crumbs[i].Name, crumbs[i].Path, i == crumbs.Count - 1));
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
        RecycleLabel = ScreenCopy.RecycleLabel(SelectedCount);
        OnPropertyChanged(nameof(CanAct));
    }

    private void RefreshWorkCopy()
    {
        var copy = ScreenCopy.Working(_activeMode, IsPaused, _workDone, _workTotal, _currentWorkItem);
        WorkTitle = copy.Title;
        WorkBody = copy.Body;
        WorkCounter = copy.Counter;
        WorkNow = copy.Now;
        WorkPauseLabel = copy.PauseLabel;
        WorkNote = copy.Note;
        WorkFraction = copy.Fraction;
    }

    /// <summary>Runs the deletion engine (permanent) or the recycle-bin path; reports progress.</summary>
    public async Task<DeletionResult> DeleteAsync(IReadOnlyList<string> paths, DeletionMode mode)
    {
        IsDeleting = true;
        IsPaused = false;
        _activeMode = mode;
        _workDone = 0;
        _workTotal = paths.Count;
        _currentWorkItem = "";
        RefreshWorkCopy();
        _deleteCts = new CancellationTokenSource();
        _pauseToken = new PauseToken();

        var progress = new Progress<DeletionProgress>(p =>
        {
            _workDone = p.ItemsProcessed;
            _workTotal = p.TotalDiscovered;
            _currentWorkItem = p.CurrentItem ?? "";
            ProgressCurrentItem = p.CurrentItem ?? "";
            RefreshWorkCopy();
        });

        try
        {
            return mode == DeletionMode.RecycleBin
                ? await RecycleBinDeleter.DeleteAsync(paths, _deleteCts.Token, _pauseToken, progress)
                : await new DeletionEngine().DeleteAsync(paths, progress, _deleteCts.Token, _pauseToken);
        }
        finally
        {
            IsDeleting = false;
            _deleteCts = null;
            _pauseToken = null;
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
        RefreshWorkCopy();
    }

    public static string FormatSize(long bytes) => FolderSizeLabel.ByteText(bytes);
}
