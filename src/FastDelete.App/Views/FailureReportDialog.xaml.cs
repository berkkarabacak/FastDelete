using System.IO;
using System.Text;
using System.Windows;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class FailureReportDialog : Window
{
    private readonly IReadOnlyList<DeleteFailure> _failures;

    public IReadOnlyList<string> RetryPaths { get; private set; } = [];

    public FailureReportDialog(IReadOnlyList<DeleteFailure> failures, DeletionMode mode)
    {
        InitializeComponent();
        _failures = failures;
        int retryable = failures.Count(f => f.Retryable);
        Summary.Text = failures.Count == 1
            ? "1 file or folder could not be deleted."
            : $"{failures.Count:N0} files or folders could not be deleted.";
        if (retryable > 0)
            Summary.Text += retryable == 1
                ? " 1 of them may work if you try again (it is in use or locked right now)."
                : $" {retryable:N0} of them may work if you try again (they are in use or locked right now).";
        DataContext = new { Failures = failures };
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        RetryPaths = _failures.Where(f => f.Retryable).Select(f => f.Path).ToList();
        if (RetryPaths.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "No retryable failures (only access/sharing/not-empty errors can be retried).",
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
