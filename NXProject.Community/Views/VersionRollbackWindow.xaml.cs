// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Voltar o NXProject para uma versão anterior — a saída de emergência para quando uma versão
    /// nova chega com defeito e a pessoa precisa trabalhar hoje.
    ///
    /// O caminho é o MESMO da atualização (baixa o pacote da release e troca o executável), só que
    /// apontando para uma release antiga em vez da mais recente. O instalador (NXProject-Setup)
    /// não entra nisso: ele muda muito pouco e vive na release fixa dele.
    ///
    /// Duas precauções que a tela toma, porque voltar versão não é o caminho normal:
    ///
    ///  • **O aviso fica à vista**, não num tooltip: cronograma salvo por uma versão nova pode não
    ///    abrir numa antiga, já que o schema do arquivo é versionado.
    ///  • **A confirmação nomeia a versão** de origem e de destino. "Instalar" sem dizer o que sai
    ///    e o que entra seria convidar ao engano.
    /// </summary>
    public partial class VersionRollbackWindow : Window
    {
        private IReadOnlyList<UpdateService.PublishedVersion> _versions = Array.Empty<UpdateService.PublishedVersion>();

        public VersionRollbackWindow()
        {
            InitializeComponent();
            CurrentText.Text = AppStrings.Get("Roll_Current", UpdateService.GetCurrentVersion().ToString());
            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            StatusText.Text = AppStrings.Get("Roll_Loading");
            _versions = await UpdateService.ListPublishedVersionsAsync();
            VersionsList.Items.Clear();

            if (_versions.Count == 0)
            {
                StatusText.Text = AppStrings.Get("Roll_NoVersions");
                return;
            }

            foreach (var v in _versions)
            {
                var texto = v.IsCurrent
                    ? AppStrings.Get("Roll_ItemCurrent", v.TagName, v.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy"))
                    : AppStrings.Get("Roll_Item", v.TagName, v.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy"));
                VersionsList.Items.Add(new ListBoxItem
                {
                    Content = texto,
                    Tag = v,
                    // A versão instalada aparece para dar referência, mas não se "volta" para ela.
                    IsEnabled = !v.IsCurrent,
                    FontWeight = v.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal
                });
            }
            StatusText.Text = "";
        }

        private void OnVersionSelected(object sender, SelectionChangedEventArgs e) =>
            InstallBtn.IsEnabled = Selected() != null;

        private UpdateService.PublishedVersion? Selected() =>
            (VersionsList.SelectedItem as ListBoxItem)?.Tag as UpdateService.PublishedVersion;

        private async void OnInstallClick(object sender, RoutedEventArgs e)
        {
            if (Selected() is not { } alvo) return;

            var atual = UpdateService.GetCurrentVersion().ToString();
            var ask = MessageBox.Show(this,
                AppStrings.Get("Roll_Confirm", atual, alvo.TagName),
                AppStrings.Get("Roll_Title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.OK) return;

            InstallBtn.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            StatusText.Text = AppStrings.Get("Roll_Downloading", alvo.TagName);
            try
            {
                var progresso = new Progress<int>(p => Progress.Value = p);
                var dir = await UpdateService.DownloadAndExtractAsync(alvo.DownloadUrl, progresso);
                StatusText.Text = AppStrings.Get("Roll_Restarting");
                // Mesmo mecanismo da atualização: o script troca o executável com o app fechado.
                UpdateService.LaunchUpdaterAndExit(dir);
            }
            catch (Exception ex)
            {
                Progress.Visibility = Visibility.Collapsed;
                InstallBtn.IsEnabled = true;
                StatusText.Text = "";
                MessageBox.Show(this, ex.Message, AppStrings.Get("Roll_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
