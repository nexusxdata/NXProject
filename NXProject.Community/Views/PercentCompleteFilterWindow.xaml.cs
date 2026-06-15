using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace NXProject.Views
{
    public partial class PercentCompleteFilterWindow : Window
    {
        public double? MinPercent { get; private set; }
        public double? MaxPercent { get; private set; }

        public PercentCompleteFilterWindow(double? minPercent, double? maxPercent)
        {
            InitializeComponent();

            MinBox.Text = FormatPercent(minPercent);
            MaxBox.Text = FormatPercent(maxPercent);
            ActiveOnlyCheck.IsChecked = !minPercent.HasValue && maxPercent == 99;
            ApplyActiveOnlyState();
        }

        private static string FormatPercent(double? value) =>
            value.HasValue
                ? value.Value.ToString("0", CultureInfo.CurrentCulture)
                : string.Empty;

        private bool TryReadPercent(TextBox box, string label, out double? value)
        {
            value = null;
            var text = box.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (!double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
            {
                ErrorText.Text = $"{label}: informe um numero entre 0 e 100.";
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (parsed < 0 || parsed > 100)
            {
                ErrorText.Text = $"{label}: o valor deve ficar entre 0 e 100.";
                box.Focus();
                box.SelectAll();
                return false;
            }

            value = Math.Round(parsed);
            return true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            MinPercent = null;
            MaxPercent = null;
            DialogResult = true;
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            if (ActiveOnlyCheck.IsChecked == true)
            {
                MinPercent = null;
                MaxPercent = 99;
                DialogResult = true;
                return;
            }

            if (!TryReadPercent(MinBox, "Minimo", out var min) ||
                !TryReadPercent(MaxBox, "Maximo", out var max))
                return;

            if (min.HasValue && max.HasValue && min.Value > max.Value)
            {
                ErrorText.Text = "O minimo nao pode ser maior que o maximo.";
                MinBox.Focus();
                MinBox.SelectAll();
                return;
            }

            MinPercent = min;
            MaxPercent = max;
            DialogResult = true;
        }

        private void OnActiveOnlyChanged(object sender, RoutedEventArgs e) => ApplyActiveOnlyState();

        private void ApplyActiveOnlyState()
        {
            if (ActiveOnlyCheck.IsChecked == true)
            {
                MinBox.Text = string.Empty;
                MaxBox.Text = "99";
                MinBox.IsEnabled = false;
                MaxBox.IsEnabled = false;
                ErrorText.Text = string.Empty;
            }
            else
            {
                MinBox.IsEnabled = true;
                MaxBox.IsEnabled = true;
            }
        }
    }
}
