using System.Windows;
using System.Windows.Controls;
using FastDelete.Core.Deletion;
using FastDelete.Core.Enumeration;

namespace FastDelete.App.Views;

public partial class ConfirmDeleteDialog : Window
{
    private readonly CancellationTokenSource _countCts = new();
    private ConfirmCountState _countState = ConfirmCountState.Checking;
    private bool _blocked;
    private int _closed;

    public ConfirmDeleteDialog(IReadOnlyList<string> targets, DeletionMode mode, IReadOnlyList<ScreenItem>? named = null)
    {
        InitializeComponent();
        Loaded += (_, _) => CancelButton.Focus();

        var rows = named is { Count: > 0 }
            ? named
            : targets.Select(ScreenCopy.ItemFromPath).ToList();
        var copy = ScreenCopy.Confirm(mode, rows);
        NameList.ItemsSource = copy.Items;
        Headline.Text = copy.Title;
        Body.Text = copy.Body;
        Body.FontWeight = copy.BodyStrong ? FontWeights.ExtraBold : FontWeights.Normal;
        if (!string.IsNullOrEmpty(copy.Body2))
        {
            Body2.Text = copy.Body2;
            Body2.Visibility = Visibility.Visible;
        }
        ListLabel.Text = copy.ListLabel.ToUpperInvariant();
        ConfirmText.Text = copy.YesLabel;
        CancelText.Text = copy.CancelLabel;
        if (copy.UseRed)
        {
            Stripe.Background = (System.Windows.Media.Brush)FindResource("Brush.Danger");
            ConfirmButton.Style = (Style)FindResource("DialogDangerButton");
        }

        ConfirmButton.IsEnabled = false;
        ConfirmButton.IsDefault = false;
        CountStatus.Text = ScreenCopy.CheckingLine;

        var guard = DeleteSafety.CheckTargets(targets);
        if (!guard.Allowed)
        {
            _blocked = true;
            _countState = ConfirmCountState.Finished;
            Title = "FastDelete";
            Headline.Text = guard.BlockedPaths.Count == 1 ? "This cannot be deleted" : "These cannot be deleted";
            Body.Text = guard.Explanation;
            Body.FontWeight = FontWeights.Normal;
            Body2.Visibility = Visibility.Collapsed;
            CountStatus.Text = "Nothing will be deleted.";
            NameList.ItemsSource = guard.BlockedPaths.Select(ScreenCopy.ItemFromPath).ToList();
            ListLabel.Text = "CANNOT DELETE";
            ConfirmButton.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(CancelButton, 3);
            return;
        }

        Title = mode == DeletionMode.RecycleBin ? "Move to Recycle Bin" : "Delete forever?";
        _ = Task.Run(async () =>
        {
            try
            {
                var stats = await TreeCounter.CountAsync(targets, _countCts.Token, cap: 1_000_000);
                string text = stats.TotalItems >= 1_000_000
                    ? "Contains more than 1,000,000 items."
                    : $"Contains {stats.Files:N0} files and {stats.Directories:N0} folders.";
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
        bool ok = DeleteSafety.CanAccept(_countState, _blocked);
        ConfirmButton.IsEnabled = ok;
        ConfirmButton.IsDefault = false;
        if (state == ConfirmCountState.Finished)
        {
            CountStatus.Text = "";
            CountStatus.Visibility = Visibility.Collapsed;
            return;
        }
        CountStatus.Text = text;
        CountStatus.Visibility = Visibility.Visible;
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
