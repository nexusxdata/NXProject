using System.Windows;
using PdfSharp;

namespace NXProject.Views
{
    public enum PdfLayoutMode { Together, TwoPages }

    public partial class PdfExportOptionsWindow : Window
    {
        public PdfLayoutMode LayoutMode { get; private set; }
        public PageSize      PageSize   { get; private set; }

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
            LayoutMode = RadioTwoPages.IsChecked == true
                ? PdfLayoutMode.TwoPages
                : PdfLayoutMode.Together;

            PageSize = SizeA2.IsChecked == true ? PageSize.A2
                     : PageSize.A3; // default

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
