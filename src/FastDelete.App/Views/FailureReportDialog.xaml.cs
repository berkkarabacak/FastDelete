using System.IO;
using System.Text;
using System.Windows;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class FailureReportDialog : Window
{
    private readonly IReadOnlyList<DeleteFailure> _failures;
    private readonly DeleteSafety.RetryPlan _plan;

    public IReadOnlyList<string> RetryPaths => _plan.Paths;

    public DeletionMode RetryMode => _plan.Mode;

    public FailureReportDialog(IReadOnlyList<DeleteFailure> failures, DeletionMode mode, bool wasCancelled = false, bool offerRetry = true)
    {
        InitializeComponent();
        _failures = failures;
        _plan = DeleteSafety.PlanRetry(mode, failures, wasCancelled);
        int retryable = _plan.Paths.Count;
        Summary.Text = failures.Count == 1
            ? "1 file or folder could not be deleted."
            : $"{failures.Count:N0} files or folders could not be deleted.";
        if (retryable > 0 && offerRetry)
        {
            Summary.Text += retryable == 1
                ? " 1 of them may work if you try again (it is in use or locked right now)."
                : $" {retryable:N0} of them may work if you try again (they are in use or locked right now).";
            Summary.Text += mode == DeletionMode.RecycleBin
                ? " Trying again moves only those to the Recycle Bin, and asks you to confirm first."
                : " Trying again deletes only those forever, and asks you to confirm first.";
        }
        if (!offerRetry || retryable == 0)
            RetryButton.Visibility = Visibility.Collapsed;
        var offered = new HashSet<string>(_plan.Paths, StringComparer.OrdinalIgnoreCase);
        DataContext = new
        {
            Failures = failures.Select(f => new FailureRow(f.Path, f.Message, offered.Contains(f.Path))).ToList(),
        };
    }

    private sealed record FailureRow(string Path, string Message, bool Retryable);

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_plan.Paths.Count == 0 || !_plan.NeedsConfirmation)
        {
            System.Windows.MessageBox.Show(this,
                "Nothing left that can be tried again. What was already deleted stays deleted.",
                "FastDelete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
        => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, _failures.Select(f => f.ToString())));

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export failures to CSV",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = "fastdelete-failures.csv",
        };
        if (dialog.ShowDialog(this) != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Path,ErrorCode,Message,Kind,Retryable");
        foreach (var f in _failures)
            sb.AppendLine($"\"{f.Path.Replace("\"", "\"\"")}\",0x{f.ErrorCode:X8},\"{f.Message.Replace("\"", "\"\"")}\",{f.Kind},{f.Retryable}");
        File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
