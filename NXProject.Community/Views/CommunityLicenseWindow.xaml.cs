using System.ComponentModel;
using System.Windows;

namespace NXProject.Views
{
    public partial class CommunityLicenseWindow : Window
    {
        public bool RequireAcceptance { get; set; }

        public CommunityLicenseWindow()
        {
            InitializeComponent();
        }

        private void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void OnDeclineClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            // X button when acceptance is required = treat as decline (DialogResult stays null → false)
            if (RequireAcceptance && DialogResult == null)
                DialogResult = false;
        }
    }
}
