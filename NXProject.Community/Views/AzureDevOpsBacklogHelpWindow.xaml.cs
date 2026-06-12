using System.Windows;

namespace NXProject.Views
{
    public partial class AzureDevOpsBacklogHelpWindow : Window
    {
        public AzureDevOpsBacklogHelpWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
