using System;
using System.Diagnostics;
using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class CommunityAboutWindow : Window
    {
        private const string ContactEmail = "comercial.nexus.xdata@gmail.com";
        private const string NxStoreUrl = "https://github.com/nexusxdata/NXProject";

        public CommunityAboutWindow()
        {
            InitializeComponent();
            CompanyLogoImage.Source = ProtectedLogoProvider.GetLogoImage();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnEmailClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo($"mailto:{ContactEmail}") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Nao foi possivel abrir o cliente de e-mail.\n\n{ex.Message}",
                    "Contato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void OnNxStoreClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(NxStoreUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Nao foi possivel abrir o endereco de novas versoes.\n\n{NxStoreUrl}\n\n{ex.Message}",
                    "NXStore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
