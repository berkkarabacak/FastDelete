using System.Windows;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog(int targetCount, long totalBytes, DeletionMode mode)
    {
        InitializeComponent();
        bool recycle = mode == DeletionMode.RecycleBin;
        string what = targetCount == 1 ? "1 item" : $"{targetCount:N0} items";
        string sizeNote = totalBytes > 0 ? $" ({ViewModels.MainViewModel.FormatSize(totalBytes)})" : string.Empty;

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
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
