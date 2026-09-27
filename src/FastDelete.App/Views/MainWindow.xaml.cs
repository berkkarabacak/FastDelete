using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FastDelete.App.Models;
using FastDelete.App.ViewModels;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class MainWindow : Window
{
    private readonly List<FileSystemItem> _gridSelection = new();

    public MainWindow() => InitializeComponent();

    private MainViewModel VM => (MainViewModel)DataContext;

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Models.TreeNode node && !node.IsPlaceholder)
            VM.NavigateCommand.Execute(node.FullPath);
    }

    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
            VM.NavigateCommand.Execute(path);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
        => FileGrid.SelectAll();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose the folder that has the files you want to delete, then click OK.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog(new Win32WindowOwner(this)) == System.Windows.Forms.DialogResult.OK)
            VM.NavigateCommand.Execute(dialog.SelectedPath);
    }

    /// <summary>Explorer right-click behavior: right-clicking an unselected row selects just that row;
    /// right-clicking inside an existing multi-selection keeps the whole selection. The context
    /// menu is opened explicitly at the click position — PlacementMode.Mouse can land at the
    /// physical cursor (stale with synthesized input), RelativePoint cannot.</summary>
    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is FileSystemItem item && !FileGrid.SelectedItems.Contains(item))
        {
            FileGrid.SelectedItems.Clear();
            FileGrid.SelectedItems.Add(item);
        }
        var position = e.GetPosition(FileGrid);
        e.Handled = true; // suppress the default path - it would toggle the menu back shut
        // Open after this click finishes, otherwise the right-button-UP handler closes it again.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FileGrid.ContextMenu is { } menu)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
                menu.PlacementTarget = FileGrid;
                menu.HorizontalOffset = position.X;
                menu.VerticalOffset = position.Y;
                menu.IsOpen = true;
            }
        }));
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child is not null and not T)
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        return child as T;
    }

    private sealed class Win32WindowOwner(System.Windows.Window window) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = new System.Windows.Interop.WindowInteropHelper(window).Handle;
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _gridSelection.Clear();
        if (sender is DataGrid grid)
            _gridSelection.AddRange(grid.SelectedItems.Cast<FileSystemItem>());
        VM.UpdateSelection(_gridSelection);
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)e.OriginalSource).DataContext is FileSystemItem { IsDirectory: true } item)
            VM.NavigateCommand.Execute(item.FullPath);
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is FileSystemItem { IsDirectory: true } item)
            VM.NavigateCommand.Execute(item.FullPath);
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
        var targets = VM.ResolveDeleteTargets(_gridSelection);
        if (targets.Count == 0)
        {
            VM.StatusText = "Nothing is selected — click items in the list first, or press Select All.";
            return;
        }

        var confirm = new ConfirmDeleteDialog(VM.SelectedCount, VM.SelectedBytes, mode) { Owner = this };
        if (confirm.ShowDialog() != true) return;

        var result = await VM.DeleteAsync(targets, mode);
        VM.RefreshCommand.Execute(null);

        if (result.Failures.Count > 0)
        {
            var report = new FailureReportDialog(result.Failures, mode) { Owner = this };
            if (report.ShowDialog() == true && report.RetryPaths.Count > 0)
            {
                var retryResult = await VM.DeleteAsync(report.RetryPaths, DeletionMode.Permanent);
                VM.RefreshCommand.Execute(null);
                if (retryResult.Failures.Count > 0)
                    _ = new FailureReportDialog(retryResult.Failures, DeletionMode.Permanent) { Owner = this }.ShowDialog();
            }
        }
    }
}

/// <summary>Pause/resume button label.</summary>
public sealed class PauseLabelConverter : IValueConverter
{
    public static readonly PauseLabelConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Resume" : "Pause";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
