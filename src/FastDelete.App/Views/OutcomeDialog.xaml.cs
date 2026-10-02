using System.Windows;
using System.Windows.Controls;
using FastDelete.Core.Deletion;

namespace FastDelete.App.Views;

public partial class OutcomeDialog : Window
{
    private readonly bool _retry;

    public OutcomeDialog(OutcomeCopy copy)
    {
        InitializeComponent();
        _retry = copy.IsRetry;
        Headline.Text = copy.Title;
        Body.Text = copy.Body;
        CheckMark.Visibility = copy.ShowCheck ? Visibility.Visible : Visibility.Collapsed;
        NameList.ItemsSource = copy.Items;
        ListHost.Visibility = copy.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Note.Text = copy.Note;
        Note.Visibility = string.IsNullOrEmpty(copy.Note) ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = copy.PrimaryLabel;
        CloseButton.Visibility = copy.ShowClose ? Visibility.Visible : Visibility.Collapsed;
        if (!copy.ShowClose)
        {
            PrimaryButton.IsCancel = true;
            Grid.SetColumnSpan(PrimaryButton, 1);
        }
        Loaded += (_, _) =>
        {
            if (copy.ShowClose)
                CloseButton.Focus();
            else
                PrimaryButton.Focus();
        };
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _retry;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
