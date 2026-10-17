// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Tela modal de andamento da sincronização com o TFS/DevOps. Mostra a etapa atual e,
    /// na etapa de itens, qual Epic/Feature/Story/Task está sendo sincronizado.
    /// A sincronização não é cancelável: a janela só fecha quando termina.
    /// </summary>
    public partial class SyncProgressWindow : Window
    {
        private bool _canClose;

        /// <summary>
        /// Pedido de interrupção. Nulo = destino que não sabe parar — e aí o Esc não promete
        /// nada, porque prometer parar e continuar gravando seria pior do que não oferecer.
        /// </summary>
        private System.Threading.CancellationTokenSource? _cancelamento;

        /// <summary>
        /// Liga o Esc: a partir daqui, a tecla PEDE para parar. A gravação já começada termina —
        /// o corte é ENTRE itens, nunca no meio de um, senão um item ficaria com metade dos
        /// campos gravados e a outra metade não.
        /// </summary>
        public void PermiteInterromper(System.Threading.CancellationTokenSource fonte)
        {
            _cancelamento = fonte;
            HintText.Text = AppStrings.Get("SyncProg_EscHint");
            HintText.Visibility = Visibility.Visible;
        }

        /// <param name="destino">
        /// Nome do destino que esta sendo sincronizado, para o titulo. A tela e a mesma para
        /// DevOps, GitProject e Local — ler "Sincronizando com o TFS/DevOps" enquanto o board do
        /// GitHub e gravado e a janela contradizendo o que esta acontecendo.
        /// </param>
        public SyncProgressWindow(string? destino = null)
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(destino))
                Title = AppStrings.Get("SyncProg_TitleOf", destino!);
            PhaseText.Text = AppStrings.Get("SyncProg_Starting");
            Bar.IsIndeterminate = true;
            // Fechar pelo X só quando a sincronização terminar.
            Closing += (_, e) => e.Cancel = !_canClose;
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Escape || _cancelamento == null) return;
                if (_cancelamento.IsCancellationRequested) return;
                _cancelamento.Cancel();
                HintText.Text = AppStrings.Get("SyncProg_Stopping");
                e.Handled = true;
            };
        }

        /// <summary>Atualiza a tela com o andamento (chamado pela thread de UI).</summary>
        public void Report(Services.TfsImportService.SyncProgress p)
        {
            PhaseText.Text = p.Phase;
            ItemText.Text = p.Item ?? string.Empty;
            if (p.Total > 0)
            {
                Bar.IsIndeterminate = false;
                Bar.Value = p.Current * 100.0 / p.Total;
                CountText.Text = AppStrings.Get("SyncProg_Count", p.Current, p.Total);
            }
            else
            {
                Bar.IsIndeterminate = true;
                CountText.Text = string.Empty;
            }
        }

        /// <summary>Libera o fechamento e fecha a janela. Idempotente (catch + finally).</summary>
        public void Done()
        {
            if (_canClose) return;
            _canClose = true;
            Close();
        }
    }
}
