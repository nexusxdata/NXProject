using System.Windows;
using System.Windows.Input;
using NXProject.Services;

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

        // Preenche HH/dia a partir do % atual para referência
        var hpd = ProjectCalendarService.WorkingHoursPerDay * currentPercent / 100.0;
        if (hpd > 0)
            HhDiaBox.Text = $"{hpd:0.##}";

        HhDiaBox.Focus();
        HhDiaBox.SelectAll();
    }

    private void OnCalculatePercent(object sender, RoutedEventArgs e)
    {
        var raw = HhDiaBox.Text.Replace(',', '.').Trim();
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var hh) || hh <= 0)
        {
            ErrorText.Text = "Digite um valor válido para HH/dia.";
            ErrorText.Visibility = Visibility.Visible;
            HhDiaBox.Focus();
            return;
        }

        var hoursPerDay = ProjectCalendarService.WorkingHoursPerDay;
        var perc = (int)Math.Round(hh / hoursPerDay * 100.0);
        perc = Math.Clamp(perc, 1, _maxPercent);
        PercAlocBox.Text = perc.ToString();
        ErrorText.Visibility = Visibility.Collapsed;
        PercAlocBox.Focus();
        PercAlocBox.SelectAll();
    }

    private void OnPreviewDecimalInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(c => char.IsDigit(c) || c == '.' || c == ',');
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
