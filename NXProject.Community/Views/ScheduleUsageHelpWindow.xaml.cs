using System.Windows;

namespace NXProject.Views
{
    public partial class ScheduleUsageHelpWindow : Window
    {
        public ScheduleUsageHelpWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
