using System;
using System.Windows;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TaskDescriptionEditWindow : Window
    {
        private readonly ProjectTask _task;

        public TaskDescriptionEditWindow(ProjectTask task)
        {
            InitializeComponent();
            _task = task;
            TitleText.Text = $"Descrição — {task.Name}";
            DescriptionBox.Text = task.Description ?? string.Empty;

            if (task.TfsId is not > 0)
                FetchBtn.IsEnabled = false;
        }

        private async void OnFetchFromDevOpsClick(object sender, RoutedEventArgs e)
        {
            FetchBtn.IsEnabled = false;
            FetchStatus.Text = "Buscando...";
            try
            {
                var options = TfsConnectionStore.Load("NXProject.Community");
                var desc = await TfsImportService.LoadWorkItemDescriptionAsync(options, _task.TfsId!.Value);
                DescriptionBox.Text = desc;
                FetchStatus.Text = string.IsNullOrWhiteSpace(desc)
                    ? "Descrição vazia no DevOps."
                    : "Descrição carregada do DevOps.";
            }
            catch (Exception ex)
            {
                FetchStatus.Text = $"Erro: {ex.Message}";
            }
            finally
            {
                FetchBtn.IsEnabled = _task.TfsId is > 0;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            _task.Description = DescriptionBox.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}
