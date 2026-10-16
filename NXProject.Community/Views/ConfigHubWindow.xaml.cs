// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System.Windows;

namespace NXProject.Views
{
    /// <summary>
    /// Índice das configurações de integração: DevOps, GitProject, campos e calendário.
    ///
    /// É o MESMO bloco que a tela de importação já mostrava — e é de propósito: quem prepara a
    /// integração passa por esses quatro lugares, e eles estavam espalhados por dois menus e uma
    /// tela de import. Aqui não se configura nada; cada botão abre a tela que faz o trabalho,
    /// que continua sendo uma só para os dois destinos.
    /// </summary>
    public partial class ConfigHubWindow : Window
    {
        private readonly string _storageKey;
        private readonly string? _currentProjectName;

        public ConfigHubWindow(string storageKey = "NXProject.Community", string? currentProjectName = null)
        {
            InitializeComponent();
            _storageKey = string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim();
            _currentProjectName = string.IsNullOrWhiteSpace(currentProjectName) ? null : currentProjectName!.Trim();
        }

        /// <summary>
        /// A tela de conexao — Azure DevOps e GitHub Projects sao abas dela, e o Salvar grava
        /// as duas. Por isso um botao so.
        /// </summary>
        private void OnDevOpsClick(object sender, RoutedEventArgs e)
            => new TfsDevOpsConfigWindow(_storageKey, _currentProjectName) { Owner = this }.ShowDialog();

        private void OnFieldsClick(object sender, RoutedEventArgs e)
            => new FieldNamesWindow { Owner = this }.ShowDialog();

        /// <summary>
        /// Calendário: o mesmo controle da tela de conexão, numa janela própria — ele é do
        /// PROJETO (jornada, feriados), não de um destino.
        /// </summary>
        private void OnCalendarClick(object sender, RoutedEventArgs e)
        {
            var control = new NXProject.Controls.CalendarSettingsControl(_storageKey);
            new Window
            {
                Title = Services.AppStrings.Get("Imp_Calendar"),
                Content = control,
                Owner = this,
                Width = 620,
                Height = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            }.ShowDialog();
        }
    }
}
