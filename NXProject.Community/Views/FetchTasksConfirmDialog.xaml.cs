using System.Windows;

namespace NXProject.Views
{
    public enum FetchTasksAction { Include, Release, Cancel }

    public partial class FetchTasksConfirmDialog : Window
    {
        public FetchTasksAction Result { get; private set; } = FetchTasksAction.Cancel;

        public FetchTasksConfirmDialog(int totalFound, int newCount)
        {
            InitializeComponent();
            SummaryText.Text = $"{totalFound} Tasks encontradas no DevOps. {newCount} são novas.";

            // Se há Tasks novas ou alteradas, avisa que serão suprimidas ao liberar
            if (newCount > 0)
            {
                WarningText.Text = $"⚠ Ao liberar: as {newCount} Task(s) nova(s) serão suprimidas e não adicionadas ao cronograma.";
                WarningText.Visibility = Visibility.Visible;
            }
        }

        private void OnIncludeClick(object sender, RoutedEventArgs e)
        {
            Result = FetchTasksAction.Include;
            Close();
        }

        private void OnReleaseClick(object sender, RoutedEventArgs e)
        {
            Result = FetchTasksAction.Release;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Result = FetchTasksAction.Cancel;
            Close();
        }
    }
}
