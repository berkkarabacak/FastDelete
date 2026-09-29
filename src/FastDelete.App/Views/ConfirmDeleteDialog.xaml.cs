using System.Windows;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;

namespace FastDelete.App.Views;

public partial class ConfirmDeleteDialog : Window
{
    private readonly CancellationTokenSource _countCts = new();
    private ConfirmCountState _countState = ConfirmCountState.Checking;
    private bool _blocked;
    private int _closed;

    public ConfirmDeleteDialog(IReadOnlyList<string> targets, int targetCount, long totalBytes, DeletionMode mode)
    {
        InitializeComponent();
        bool recycle = mode == DeletionMode.RecycleBin;
        string what = targetCount == 1 ? "1 item" : $"{targetCount:N0} items";
        string sizeNote = totalBytes > 0 ? $" ({ViewModels.MainViewModel.FormatSize(totalBytes)})" : string.Empty;

        PathList.Text = DeleteSafety.FormatPathList(targets);
        ConfirmButton.IsEnabled = false;
        ConfirmButton.IsDefault = false;

        var guard = DeleteSafety.CheckTargets(targets);
        if (!guard.Allowed)
        {
            _blocked = true;
            _countState = ConfirmCountState.Finished;
            Title = "These cannot be deleted";
            Headline.Text = guard.BlockedPaths.Count == 1
                ? "This cannot be deleted"
                : "These cannot be deleted";
            Contents.Text = "Nothing will be deleted.";
            Warning.Text = guard.Explanation;
            ConfirmButton.Visibility = Visibility.Collapsed;
            return;
        }

        Title = recycle ? "Move to Recycle Bin" : "Delete forever?";
        Headline.Text = recycle
            ? $"Move {what}{sizeNote} to the Recycle Bin?"
            : $"Delete {what}{sizeNote} FOREVER?";
        Warning.Text = recycle
            ? "You can get these back later from the Recycle Bin if you change your mind. " +
              "Moving to the Recycle Bin is slower for very large folders."
            : "⚠ These files will be gone for good — you CANNOT get them back from the Recycle Bin. " +
              "Please make sure you really want to do this. Shortcut links are removed safely, and other folders are never touched.";
        ConfirmButton.Content = recycle ? "Yes, move to Recycle Bin" : "Yes, delete forever";
        if (!recycle)
        {
            ConfirmButton.Background = (System.Windows.Media.Brush)FindResource("Brush.Danger");
            ConfirmButton.Foreground = System.Windows.Media.Brushes.White;
        }

        // show what's really inside (recursive), so the warning means something.
        // Yes stays off until this finishes or fails in plain sight.
        _ = Task.Run(async () =>
        {
            try
            {
                var stats = await TreeCounter.CountAsync(targets, _countCts.Token, cap: 1_000_000);
                string text = stats.TotalItems >= 1_000_000
                    ? "Contains more than 1,000,000 items."
                    : $"Contains {stats.Files:N0} files, {stats.Directories:N0} folders" +
                      (stats.Links > 0 ? $", {stats.Links:N0} shortcuts" : "") +
                      (stats.Bytes > 0 ? $" — {ViewModels.MainViewModel.FormatSize(stats.Bytes)} in total" : "") + ".";
                await Dispatcher.InvokeAsync(() => ApplyCount(ConfirmCountState.Finished, text));
            }
            catch (OperationCanceledException)
            {
                // The window was closed before the count finished.
            }
            catch (Exception)
            {
                try
                {
                    await Dispatcher.InvokeAsync(() => ApplyCount(ConfirmCountState.Failed, DeleteSafety.CountFailedMessage));
                }
                catch
                {
                    // Dispatcher is gone because the window already closed.
                }
            }
        });
    }

    private void ApplyCount(ConfirmCountState state, string text)
    {
        if (Volatile.Read(ref _closed) != 0)
            return;
        _countState = state;
        Contents.Text = text;
        bool ok = DeleteSafety.CanAccept(_countState, _blocked);
        ConfirmButton.IsEnabled = ok;
        ConfirmButton.IsDefault = ok;
    }

    protected override void OnClosed(EventArgs e)
    {
        Volatile.Write(ref _closed, 1);
        try { _countCts.Cancel(); } catch (ObjectDisposedException) { }
        base.OnClosed(e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!DeleteSafety.CanAccept(_countState, _blocked))
            return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
