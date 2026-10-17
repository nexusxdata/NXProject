// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Linq;
using System.Windows;
using NXProject.Models;
using NXProject.Services;
using NXProject.ViewModels;

namespace NXProject.Views
{
    /// <summary>
    /// Configurações do PROJETO: a data em que ele começa, o calendário de trabalho que ele usa
    /// e as pessoas que trabalham nele.
    ///
    /// É o equivalente ao "Informações sobre o projeto" do MS Project, e existe pelo mesmo
    /// motivo: essas três coisas valem para o cronograma inteiro, mas estavam espalhadas pelo
    /// menu — e a data de início não tinha tela nenhuma. Sem ela, começar o projeto noutro dia
    /// significava empurrar a primeira atividade na mão e deixar as outras seguirem atrás.
    /// </summary>
    public partial class ProjectSettingsWindow : Window
    {
        private readonly MainViewModel _vm;

        /// <summary>O calendário foi editado aqui dentro? Quem abriu precisa redesenhar.</summary>
        public bool CalendarChanged { get; private set; }

        public ProjectSettingsWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            ProjectNameText.Text = _vm.Project?.Name ?? "";
            // A data do PROJETO, sempre. So quando ela nunca foi informada (01/01/0001, de
            // arquivo antigo ou de projeto novo) a tela sugere a menor data das atividades — e,
            // na falta dessas, hoje. Abrir com "hoje" por cima de uma data gravada fazia a tela
            // mentir sobre o que esta no arquivo, e salvar em seguida apagava o valor real.
            var doProjeto = _vm.Project?.StartDate.Date ?? default;
            StartBox.SelectedDate = ScheduleDates.IsMissing(doProjeto)
                ? Folhas(_vm.Project?.Tasks)
                      .Where(x => !ScheduleDates.IsMissing(x.Start))
                      .Select(x => (DateTime?)x.Start.Date)
                      .DefaultIfEmpty(DateTime.Today)
                      .Min()
                : doProjeto;
            MostraCalendario();
            MostraPessoas();
            MostraInicio();
            StartBox.SelectedDateChanged += (_, _) => MostraInicio();
        }

        /// <summary>
        /// Que calendário está valendo. O NXProject tem dois caminhos: o GERAL, num arquivo que
        /// pode ser compartilhado pela equipe, e o DO CRONOGRAMA, embutido no .nxp. Dizer qual
        /// deles está em uso é metade da resposta para "por que esta data caiu neste dia".
        /// </summary>
        private void MostraCalendario()
        {
            var cal = ProjectCalendarService.Current;
            CalendarText.Text = ProjectCalendarService.Origin switch
            {
                ProjectCalendarService.CalendarOrigin.Schedule => AppStrings.Get("PrjCfg_CalSchedule"),
                ProjectCalendarService.CalendarOrigin.Error => AppStrings.Get("PrjCfg_CalError"),
                _ => AppStrings.Get("PrjCfg_CalGeneral")
            };

            var dias = (cal.TreatSaturdayAsWorkday ? 1 : 0) + (cal.TreatSundayAsWorkday ? 1 : 0);
            CalendarHint.Text = AppStrings.Get("PrjCfg_CalHint",
                cal.WorkingHoursPerDay.ToString("0.##"),
                cal.Holidays?.Count.ToString() ?? "0",
                (5 + dias).ToString());
        }

        private void MostraPessoas()
        {
            var total = _vm.Project?.Resources?.Count ?? 0;
            PeopleText.Text = AppStrings.Get("PrjCfg_PeopleCount", total.ToString());
        }

        /// <summary>
        /// O que a data de início significa, dito na tela.
        ///
        /// Ela é a data do WORK ITEM PROJECT: gravada lá com a tag de início fixo (a mesma de
        /// uma atividade com data negociada), vai para o DevOps no Sincronizar e volta no
        /// Importar, onde passa a valer sobre a âncora de sprint. Fixada, é também o piso do que
        /// ainda não começou. O que já começou ou terminou nunca se move: replanejar o passado
        /// seria reescrever o que aconteceu.
        /// </summary>
        private void MostraInicio()
        {
            // A caixa MOSTRA o que esta no work item Project: raiz ja marcada abre marcada, e
            // desmarcar tira a tag. Aplicar a tag so por ter aberto a tela seria decidir pelo
            // usuario — e a tag significa "esta data foi negociada", o que ninguem pode afirmar
            // por ele.
            AnchorBox.IsChecked = _vm.Project?.StartDateFixed == true;
            AnchorHint.Text = AppStrings.Get("PrjCfg_StartRole", TagDeInicioFixo());
        }

        private static System.Collections.Generic.IEnumerable<ProjectTask> Folhas(
            System.Collections.Generic.IEnumerable<ProjectTask>? tarefas)
        {
            if (tarefas == null) yield break;
            foreach (var t in tarefas)
            {
                if (t.Children == null || t.Children.Count == 0) yield return t;
                else foreach (var f in Folhas(t.Children)) yield return f;
            }
        }

        private void OnEditCalendarClick(object sender, RoutedEventArgs e)
        {
            // A MESMA tela do menu Calendário: duplicar a edição aqui criaria duas regras para o
            // mesmo arquivo, e elas divergiriam na primeira mudança.
            var duracoes = _vm.CaptureTaskWorkingDurations();
            var controle = new Controls.CalendarSettingsControl("NXProject.Community", _vm.Project);
            var janela = new Window
            {
                Title = AppStrings.Get("PrjCfg_CalendarWindow"),
                Owner = this,
                Width = 820,
                Height = 540,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = controle
            };
            // A MESMA ligacao da tela do menu: o controle avisa que gravou, e e ai que o
            // cronograma e recalculado — fechar sem salvar nao pode mexer em duracao nenhuma.
            controle.Saved += (_, _) =>
            {
                _vm.RecalculateScheduleFromCalendar(duracoes);
                CalendarChanged = true;
                janela.DialogResult = true;
                janela.Close();
            };
            janela.ShowDialog();

            MostraCalendario();
            MostraInicio();
        }

        private void OnPeopleClick(object sender, RoutedEventArgs e)
        {
            new PeopleWindow(_vm) { Owner = this }.ShowDialog();
            MostraPessoas();
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_vm.Project == null) { DialogResult = true; return; }

            var inicio = StartBox.SelectedDate?.Date ?? _vm.Project.StartDate.Date;
            if (inicio == _vm.Project.StartDate.Date && RaizComoPedida(inicio))
            { DialogResult = true; return; }

            _vm.Project.StartDate = inicio;

            // A data mora no WORK ITEM PROJECT, não num conceito só do NXProject: é a raiz do
            // cronograma que recebe a data e a TAG de início fixo — a mesma tag que uma
            // atividade usa para dizer "esta data é negociada, não recalcule". Assim o valor vai
            // para o DevOps no Sincronizar e volta de lá no Importar, em vez de viver só aqui.
            // A decisão fica no PROJETO. Escrever na linha do work item Project não adianta:
            // ela é um resumo, e o recálculo devolve a menor data dos filhos no instante
            // seguinte — foi o que fez a data voltar para 06/07 depois de salvar 20/10. Daqui
            // ela vai para o servidor no Sincronizar, com a tag, e volta no Importar.
            var fixar = AnchorBox.IsChecked == true;
            _vm.Project.StartDateFixed = fixar;

            // A regra do piso vale AGORA, não só na próxima importação: atividade por fazer não
            // fica antes do início do projeto. Quem já começou não se move. Sem a data fixada no
            // work item Project não há piso: a data é só a referência do cronograma.
            var puxadas = 0;
            if (fixar)
            foreach (var t in Folhas(_vm.Project.Tasks))
            {
                if (t.PercentComplete > 0.0001) continue;
                if (!ScheduleDates.IsMissing(t.Start) && t.Start.Date >= inicio) continue;
                t.Start = inicio;
                puxadas++;
            }
            if (puxadas > 0)
            {
                _vm.RecalculateScheduleRespectingAssignments();
                _vm.RefreshTasks();
            }

            _vm.Project.IsDirty = true;
            DialogResult = true;
        }

        /// <summary>A raiz do cronograma — o work item do tipo Project.</summary>
        private ProjectTask? Raiz() => _vm.Project?.Tasks?.FirstOrDefault();

        /// <summary>Nada mudou em relação ao que está gravado?</summary>
        private bool RaizComoPedida(DateTime inicio) =>
            _vm.Project != null
            && _vm.Project.StartDate.Date == inicio
            && _vm.Project.StartDateFixed == (AnchorBox.IsChecked == true);

        /// <summary>
        /// A tag configurada para "data de início fixa" (padrão DT-INI-NEG). Sai da configuração
        /// do DevOps, e não de uma constante aqui: quem renomeou a tag na organização renomeou
        /// para todo mundo.
        /// </summary>
        private static string TagDeInicioFixo()
        {
            var nome = TfsConnectionStore.Load("NXProject.Community").FixedStartTagName;
            return string.IsNullOrWhiteSpace(nome) ? "DT-INI-NEG" : nome.Trim();
        }

        private static bool TemTag(string? tags, string tag) =>
            (tags ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Any(x => string.Equals(x.Trim(), tag, StringComparison.CurrentCultureIgnoreCase));

        private static string SemTag(string? tags, string tag) =>
            string.Join("; ", (tags ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && !string.Equals(x, tag, StringComparison.CurrentCultureIgnoreCase)));

        private static string ComTag(string? tags, string tag)
        {
            if (TemTag(tags, tag)) return tags ?? "";
            var atuais = (tags ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            atuais.Add(tag);
            return string.Join("; ", atuais);
        }

    }
}
