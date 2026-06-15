using System.Collections.Generic;
using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TfsOnlineChildTasksWindow : Window
    {
        public TfsOnlineChildTasksWindow(
            int storyId,
            string storyName,
            IReadOnlyList<TfsImportService.OnlineChildTaskInfo> tasks)
        {
            InitializeComponent();

            TitleText.Text = $"Tasks online da Story #{storyId} - {storyName}";
            CountText.Text = tasks.Count == 1
                ? "1 Task encontrada no DevOps"
                : $"{tasks.Count} Tasks encontradas no DevOps";
            TasksGrid.ItemsSource = tasks;
        }
    }
}
