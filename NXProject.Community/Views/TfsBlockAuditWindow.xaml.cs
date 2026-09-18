// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Auditoria de bloqueio (BLOCK) de uma Story ou Task, lida NA HORA do DevOps — o NX nao
    /// guarda nada disso. Mostra desde quando o item esta em andamento, cada bloqueio com inicio,
    /// fim e duracao, e no rodape o total bloqueado contra o tempo util. E o numero que responde
    /// "vale mudar de atividade ou tratar a causa raiz do impedimento?".
    /// </summary>
    public partial class TfsBlockAuditWindow : Window
    {
        /// <summary>Uma linha da grade: um bloqueio.</summary>
        public sealed class BlockRow
        {
            public int Seq { get; init; }
            public string FromText { get; init; } = "";
            public string ToText { get; init; } = "";
            public string ElapsedText { get; init; } = "";
            public string WorkHoursText { get; init; } = "";
            public string BlockedBy { get; init; } = "";
            public string UnblockedBy { get; init; } = "";
            public bool Open { get; init; }
        }

        private readonly ObservableCollection<BlockRow> _rows = new();
        private readonly int _id;
        private readonly Action<int>? _openDevOps;
        private readonly Func<Task<TfsImportService.BlockAudit?>>? _reload;

        public TfsBlockAuditWindow(TfsImportService.BlockAudit audit, Action<int>? openDevOps = null,
            Func<Task<TfsImportService.BlockAudit?>>? reload = null)
        {
            InitializeComponent();
            _id = audit.Id;
            _openDevOps = openDevOps;
            _reload = reload;
            OpenDevOpsButton.Visibility = openDevOps is null ? Visibility.Collapsed : Visibility.Visible;
            RefreshButton.Visibility = reload is null ? Visibility.Collapsed : Visibility.Visible;
            ItemsGrid.ItemsSource = _rows;
            Fill(audit);
        }

        /// <summary>Rele o historico no DevOps. Util logo depois de sincronizar o board: enquanto o
        /// desbloqueio estiver so na fila do NX, o DevOps ainda tem a tag e o bloqueio segue aberto.</summary>
        private async void OnRefresh(object sender, RoutedEventArgs e)
        {
            if (_reload is null) return;
            try
            {
                RefreshButton.IsEnabled = false;
                var again = await _reload();
                if (again != null) { _rows.Clear(); Fill(again); }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { RefreshButton.IsEnabled = true; }
        }

        private static string Hours(double h) =>
            h <= 0 ? "0 h" : h < 1 ? $"{h * 60:0} min" : $"{h:0.#} h";

        private void Fill(TfsImportService.BlockAudit a)
        {
            TitleText.Text = $"{a.Type} #{a.Id} — {a.Title}".Trim();
            SubtitleText.Text = AppStrings.Get("Block_Subtitle", a.State);

            // Janela medida: do inicio do trabalho (primeira ida para Active) ate o encerramento
            // ou agora. Sem Active o item nunca comecou — ai nao ha tempo util para comparar.
            var end = a.WindowEnd;
            WindowText.Text = a.ActiveSince is DateTime since
                ? AppStrings.Get("Block_Window", since.ToString("dd/MM/yyyy HH:mm"),
                    a.ClosedAt is DateTime c
                        ? AppStrings.Get("Block_WindowClosed", c.ToString("dd/MM/yyyy HH:mm"))
                        : AppStrings.Get("Block_WindowNow", end.ToString("dd/MM/yyyy HH:mm")))
                : AppStrings.Get("Block_NeverActive");

            var seq = 0;
            double totalElapsed = 0, totalWork = 0;
            foreach (var p in a.Periods.OrderBy(x => x.Start))
            {
                var stop = p.End ?? end;
                var elapsed = (stop - p.Start).TotalHours;
                // Tempo util do bloqueio: horas do CALENDARIO do projeto (sem fim de semana nem
                // feriado). Um impedimento que atravessa o sabado nao custou 48 h de trabalho.
                var work = ProjectCalendarService.CountWorkingHours(p.Start, stop);
                totalElapsed += Math.Max(0, elapsed);
                totalWork += Math.Max(0, work);
                _rows.Add(new BlockRow
                {
                    Seq = ++seq,
                    FromText = p.Start.ToString("dd/MM/yyyy HH:mm"),
                    ToText = p.End is DateTime e ? e.ToString("dd/MM/yyyy HH:mm")
                                                 : AppStrings.Get("Block_StillOpen"),
                    ElapsedText = Hours(elapsed),
                    WorkHoursText = Hours(work),
                    BlockedBy = p.BlockedBy,
                    UnblockedBy = p.UnblockedBy,
                    Open = p.Open
                });
            }

            if (seq == 0)
            {
                TotalsText.Text = AppStrings.Get("Block_NoBlocks");
                TotalsHintText.Text = "";
                return;
            }

            // Tempo util da atividade = janela em horas de calendario MENOS o que ficou bloqueado.
            double windowWork = a.ActiveSince is DateTime s2
                ? ProjectCalendarService.CountWorkingHours(s2, end) : 0;
            var useful = Math.Max(0, windowWork - totalWork);
            var pct = windowWork > 0 ? totalWork / windowWork * 100 : 0;

            TotalsText.Text = AppStrings.Get("Block_Totals",
                seq.ToString(), Hours(totalWork), Hours(useful), pct.ToString("0.#"));
            TotalsHintText.Text = a.StillBlocked
                ? AppStrings.Get("Block_OpenHint")
                : AppStrings.Get("Block_Hint", Hours(totalElapsed));
        }

        private void OnOpenDevOps(object sender, RoutedEventArgs e) => _openDevOps?.Invoke(_id);

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
