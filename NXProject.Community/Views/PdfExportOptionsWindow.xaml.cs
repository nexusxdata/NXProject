using System.Windows;
using System.Windows.Controls;
using PdfSharp;

namespace NXProject.Views
{
    public enum PdfLayoutMode { Together, TwoPages }

    public partial class PdfExportOptionsWindow : Window
    {
        public PdfLayoutMode LayoutMode { get; private set; }
        public PageSize      PageSize   { get; private set; }
        public int TimelineDaysBefore { get; private set; }
        public int TimelineDaysAfter  { get; private set; }

        public PdfExportOptionsWindow()
        {
            InitializeComponent();
            OnLayoutChanged(this, new RoutedEventArgs());
        }

        private void OnLayoutChanged(object sender, RoutedEventArgs e)
        {
            if (SizePanel == null) return;
            SizePanel.Visibility = RadioTwoPages.IsChecked == true
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            if (!TryReadNonNegativeDays(DaysBeforeBox, "Dias antes", out var daysBefore) ||
                !TryReadNonNegativeDays(DaysAfterBox, "Dias apos", out var daysAfter))
                return;

            LayoutMode = RadioTwoPages.IsChecked == true
                ? PdfLayoutMode.TwoPages
                : PdfLayoutMode.Together;

            PageSize = SizeA2.IsChecked == true ? PageSize.A2
                     : PageSize.A3; // default

            TimelineDaysBefore = daysBefore;
            TimelineDaysAfter = daysAfter;

            DialogResult = true;
        }

        private bool TryReadNonNegativeDays(TextBox box, string label, out int value)
        {
            value = 0;
            var text = box.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (!int.TryParse(text, out var parsed) || parsed < 0)
            {
                ErrorText.Text = $"{label}: informe um numero inteiro maior ou igual a zero.";
                box.Focus();
                box.SelectAll();
                return false;
            }

            if (parsed > 3650)
            {
                ErrorText.Text = $"{label}: use no maximo 3650 dias.";
                box.Focus();
                box.SelectAll();
                return false;
            }

            value = parsed;
            return true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
