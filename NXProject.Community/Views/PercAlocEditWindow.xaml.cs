using System.Windows;
using System.Windows.Input;

namespace NXProject.Views;

public partial class PercAlocEditWindow : Window
{
    private readonly int _maxPercent;

    public double ResultPercent { get; private set; }

    public PercAlocEditWindow(string taskName, double currentPercent, int maxPercent = 100)
    {
        InitializeComponent();
        _maxPercent = Math.Clamp(maxPercent, 1, 120);
        TaskNameText.Text = taskName;
        RangeText.Text = $"  (1 a {_maxPercent})";
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
        if (!int.TryParse(PercAlocBox.Text, out var v) || v < 1 || v > _maxPercent)
        {
            ErrorText.Text = $"Digite um valor entre 1 e {_maxPercent}.";
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
