using System.Windows;

namespace NXProject.Views
{
    public partial class PredecessorUsageHelpWindow : Window
    {
        public PredecessorUsageHelpWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
