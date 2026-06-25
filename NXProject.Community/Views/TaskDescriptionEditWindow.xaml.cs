using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NXProject.Models;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class TaskDescriptionEditWindow : Window
    {
        private readonly ProjectTask _task;
        private bool _webViewReady;
        private bool _pendingPreview;
        private string _html = string.Empty;

        public TaskDescriptionEditWindow(ProjectTask task)
        {
            InitializeComponent();
            _task = task;
            TitleText.Text = $"Descrição — {task.Name}";
            _html = task.Description ?? string.Empty;
            DescriptionBox.Text = _html;

            if (task.TfsId is not > 0)
                FetchBtn.IsEnabled = false;

            // Começa no modo preview se houver conteúdo
            if (!string.IsNullOrWhiteSpace(_html))
                _pendingPreview = true;

            InitWebViewAsync();
        }

        private async void InitWebViewAsync()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                _webViewReady = true;
                SetupWebViewAuth();

                if (_pendingPreview)
                    ShowPreview();
                else
                    ShowEdit();
            }
            catch
            {
                // WebView2 runtime ausente: cai direto no modo texto
                PreviewPanel.Visibility = Visibility.Collapsed;
                DescriptionBox.Visibility = Visibility.Visible;
                PreviewModeBtn.IsEnabled = false;
            }
        }

        private void SetupWebViewAuth()
        {
            if (!_webViewReady) return;

            try
            {
                var options = TfsConnectionStore.Load("NXProject.Community");
                if (string.IsNullOrWhiteSpace(options.PersonalAccessToken)) return;

                var authValue = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(":" + options.PersonalAccessToken));

                // Intercepta requisições para URLs do Azure DevOps e adiciona o PAT
                WebView.CoreWebView2.AddWebResourceRequestedFilter(
                    "https://*.visualstudio.com/*", CoreWebView2WebResourceContext.All);
                WebView.CoreWebView2.AddWebResourceRequestedFilter(
                    "https://dev.azure.com/*", CoreWebView2WebResourceContext.All);

                WebView.CoreWebView2.WebResourceRequested += (_, e) =>
                {
                    e.Request.Headers.SetHeader("Authorization", "Basic " + authValue);
                };
            }
            catch { /* auth opcional — imagens sem PAT simplesmente não carregam */ }
        }

        private void ShowPreview()
        {
            // Sincroniza texto da caixa de edição (caso o usuário tenha editado antes)
            if (DescriptionBox.Visibility == Visibility.Visible)
                _html = DescriptionBox.Text;

            PreviewPanel.Visibility = Visibility.Visible;
            DescriptionBox.Visibility = Visibility.Collapsed;
            PreviewModeBtn.FontWeight = FontWeights.Bold;
            EditModeBtn.FontWeight = FontWeights.Normal;

            if (_webViewReady)
                LoadHtmlInWebView(_html);
        }

        private void ShowEdit()
        {
            DescriptionBox.Text = _html;
            PreviewPanel.Visibility = Visibility.Collapsed;
            DescriptionBox.Visibility = Visibility.Visible;
            EditModeBtn.FontWeight = FontWeights.Bold;
            PreviewModeBtn.FontWeight = FontWeights.Normal;
        }

        private void LoadHtmlInWebView(string html)
        {
            // Envolve o HTML em uma página completa com estilos básicos
            const string css =
                "body{font-family:Segoe UI,sans-serif;font-size:13px;color:#1f1f1f;padding:16px;margin:0;line-height:1.5}" +
                "img{max-width:100%;height:auto}" +
                "table{border-collapse:collapse}" +
                "td,th{border:1px solid #ccc;padding:4px 8px}" +
                "th{background:#f0f0f0}" +
                "code{background:#f4f4f4;padding:1px 4px;border-radius:3px}" +
                "p{margin:0 0 8px 0}";

            var page = string.IsNullOrWhiteSpace(html)
                ? "<html><body style='font-family:Segoe UI,sans-serif;color:#666;padding:16px'><i>Sem descrição.</i></body></html>"
                : $"<html><head><meta charset='utf-8'/><style>{css}</style></head><body>{html}</body></html>";

            WebView.CoreWebView2.NavigateToString(page);
        }

        private void OnPreviewMode(object sender, RoutedEventArgs e) => ShowPreview();
        private void OnEditMode(object sender, RoutedEventArgs e) => ShowEdit();

        private async void OnFetchFromDevOpsClick(object sender, RoutedEventArgs e)
        {
            FetchBtn.IsEnabled = false;
            FetchStatus.Text = "Buscando...";
            try
            {
                var options = TfsConnectionStore.Load("NXProject.Community");
                // Busca o HTML original (sem converter para plain text)
                var html = await TfsImportService.LoadWorkItemDescriptionHtmlAsync(
                    options, _task.TfsId!.Value);
                _html = html ?? string.Empty;
                DescriptionBox.Text = _html;
                FetchStatus.Text = string.IsNullOrWhiteSpace(_html)
                    ? "Descrição vazia no DevOps."
                    : "Descrição carregada do DevOps.";

                if (_webViewReady && PreviewPanel.Visibility == Visibility.Visible)
                    LoadHtmlInWebView(_html);
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
            // Se estiver no modo edição, pega o texto da caixa; senão usa _html
            _task.Description = DescriptionBox.Visibility == Visibility.Visible
                ? DescriptionBox.Text.Trim()
                : _html.Trim();
            DialogResult = true;
            Close();
        }
    }
}
