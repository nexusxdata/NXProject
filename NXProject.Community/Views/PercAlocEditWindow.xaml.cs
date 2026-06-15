using System.Windows;
using System.Windows.Input;

namespace NXProject.Views;

public partial class PercAlocEditWindow : Window
{
    public double ResultPercent { get; private set; }

    public PercAlocEditWindow(string taskName, double currentPercent)
    {
        InitializeComponent();
        TaskNameText.Text = taskName;
        PercAlocBox.Text = ((int)currentPercent).ToString();
        PercAlocBox.SelectAll();
        PercAlocBox.Focus();
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PercAlocBox.Text, out var v) || v < 1 || v > 100)
        {
            ErrorText.Text = "Digite um valor entre 1 e 100.";
            ErrorText.Visibility = Visibility.Visible;
            PercAlocBox.Focus();
            PercAlocBox.SelectAll();
            return;
        }

        ResultPercent = v;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
