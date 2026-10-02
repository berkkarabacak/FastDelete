using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastDelete.App.Models;
using FastDelete.App.ViewModels;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class MainWindow : Window
{
    private readonly List<FileSystemItem> _gridSelection = new();

    public MainWindow()
    {
        InitializeComponent();

        var s = Services.SettingsService.Load();
        Width = s.Width > 0 ? s.Width : 1100;
        Height = s.Height > 0 ? s.Height : 700;
        if (!double.IsNaN(s.Left) && !double.IsNaN(s.Top))
        {
            Left = s.Left;
            Top = s.Top;
        }
        if (s.Maximized)
            WindowState = WindowState.Maximized;

        Closing += (_, _) =>
        {
            var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
            VM.PersistState(bounds.Width, bounds.Height, bounds.Left, bounds.Top, WindowState == WindowState.Maximized);
        };
    }

    private MainViewModel VM => (MainViewModel)DataContext;

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            return;
        switch (e.Key)
        {
            case Key.Delete:
                if (VM.CanAct)
                    StartDelete(DeletionMode.RecycleBin);
                e.Handled = true;
                break;
            case Key.F5:
                VM.RefreshCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Back:
                VM.NavigateUpCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
        => VM.NavigateUpCommand.Execute(null);

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && !string.IsNullOrWhiteSpace(path))
            VM.NavigateCommand.Execute(path);
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path } && !string.IsNullOrWhiteSpace(path))
            VM.NavigateCommand.Execute(path);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        MorePopup.DataContext = DataContext;
        MorePopup.IsOpen = true;
    }

    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => VM.OpenDownloads();

    private void MakePractice_Click(object sender, RoutedEventArgs e) => VM.OpenPracticeFolder();

    private void ToggleHidden_Click(object sender, RoutedEventArgs e)
        => VM.IncludeHidden = !VM.IncludeHidden;

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _gridSelection.Clear();
        if (sender is DataGrid grid)
            _gridSelection.AddRange(grid.SelectedItems.Cast<FileSystemItem>());
        VM.UpdateSelection(_gridSelection);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)e.OriginalSource).DataContext is not FileSystemItem item)
            return;
        OpenItem(item);
    }

    private void OpenRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileSystemItem item })
            OpenItem(item);
        e.Handled = true;
    }

    private void OpenItem(FileSystemItem item)
    {
        if (item.IsDirectory)
            VM.NavigateCommand.Execute(item.FullPath);
        else
            Services.ExplorerIntegration.OpenFileWithDefaultApp(item.FullPath);
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0 && Directory.Exists(paths[0]))
            VM.NavigateCommand.Execute(paths[0]);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
        => StartDelete(DeletionMode.Permanent);

    private void DeleteRecycle_Click(object sender, RoutedEventArgs e)
        => StartDelete(DeletionMode.RecycleBin);

    private async void StartDelete(DeletionMode mode)
    {
        if (!VM.CanAct)
            return;

        var selected = _gridSelection.ToList();
        var targets = selected.Select(i => i.FullPath).ToList();
        if (targets.Count == 0)
            return;

        if (!ConfirmDelete(targets, mode, selected.Select(ToScreenItem).ToList()))
            return;

        var result = await VM.DeleteAsync(targets, mode);
        await ShowOutcome(result, mode, allowRetry: true);
    }

    private static ScreenItem ToScreenItem(FileSystemItem item)
        => new(item.Name, item.IsDirectory ? ScreenCopy.FolderDetail : item.SizeLabel, item.IsDirectory);

    private bool ConfirmDelete(IReadOnlyList<string> targets, DeletionMode mode, IReadOnlyList<ScreenItem>? named)
    {
        var confirm = new ConfirmDeleteDialog(targets, mode, named) { Owner = this };
        return confirm.ShowDialog() == true;
    }

    private async Task ShowOutcome(DeletionResult result, DeletionMode mode, bool allowRetry)
    {
        VM.RefreshCommand.Execute(null);
        var plan = DeleteSafety.PlanRetry(mode, result.Failures, result.WasCancelled);
        var copy = ScreenCopy.DescribeOutcome(mode, result, plan, allowRetry);
        VM.SetStatusAfterLoad(copy.Title);
        var dialog = new OutcomeDialog(copy) { Owner = this };
        if (dialog.ShowDialog() != true || !copy.IsRetry || plan.Paths.Count == 0)
            return;

        if (!ConfirmDelete(plan.Paths, plan.Mode, named: null))
            return;

        var retry = await VM.DeleteAsync(plan.Paths, plan.Mode);
        await ShowOutcome(retry, plan.Mode, allowRetry: false);
    }
}
