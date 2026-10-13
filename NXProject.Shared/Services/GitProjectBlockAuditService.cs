// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NXProject.Services
{
    /// <summary>
    /// Auditoria de bloqueio de uma issue do GitHub: desde quando ela está em andamento e cada
    /// período em que esteve bloqueada.
    ///
    /// O plano dava isto como "talvez nunca fique igual", por supor que a timeline do GitHub não
    /// serviria. A POC (`NXTestUnit gitpoc`) mediu o contrário: `timelineItems` com
    /// `LABELED_EVENT`/`UNLABELED_EVENT` devolve **a label, a hora e o autor** de cada entrada e
    /// saída — exatamente o que o `/updates` do DevOps dá, em outro formato. Então a auditoria
    /// aqui é tradução, não invenção.
    ///
    /// Devolve o MESMO <see cref="TfsImportService.BlockAudit"/> do DevOps de propósito: a tela
    /// de auditoria já existe e é boa; um relatório próprio por destino seria uma segunda tela
    /// para manter — e o TaskBoard não deve saber de que servidor veio o período.
    /// </summary>
    public static class GitProjectBlockAuditService
    {
        /// <summary>
        /// Monta a auditoria do item. <paramref name="numero"/> é o número da issue, como o card
        /// o mostra.
        /// </summary>
        public static async Task<TfsImportService.BlockAudit?> LoadAsync(
            GitProjectOptions options, GitProjectClient.ProjectSnapshot snap, int numero)
        {
            if (options == null || snap == null || numero <= 0) return null;
            var item = snap.Items.FirstOrDefault(i => i.Number == numero);
            if (item == null || string.IsNullOrEmpty(item.IssueId)) return null;

            var eventos = await new GitProjectClient(options).LabelHistoryAsync(item.IssueId);
            var tag = string.IsNullOrWhiteSpace(options.BlockedTag) ? "BLOCK" : options.BlockedTag.Trim();
            return Build(item, eventos, tag);
        }

        /// <summary>
        /// A parte que decide, separada da rede para caber em teste: a série de eventos vira
        /// períodos.
        ///
        /// Duas situações que o GitHub produz e que não podem virar número errado:
        ///
        ///   • **saída sem entrada** — a label foi tirada mas o evento de entrada é anterior ao
        ///     que a API devolve (a timeline é paginada, e um item antigo pode ter mais de 100
        ///     eventos). Ignora-se a saída órfã em vez de inventar um início.
        ///   • **entrada sem saída** — ainda bloqueado: o período fica ABERTO, que é o que a tela
        ///     mostra em vermelho. Fechá-lo "em agora" faria o bloqueio parecer resolvido.
        /// </summary>
        public static TfsImportService.BlockAudit Build(
            GitProjectItem item, IReadOnlyList<GitProjectClient.LabelEvent> eventos, string tag)
        {
            var periodos = new List<TfsImportService.BlockPeriod>();
            DateTime? inicio = null;
            var bloqueouPor = "";

            foreach (var e in (eventos ?? new List<GitProjectClient.LabelEvent>()).OrderBy(x => x.CreatedAt))
            {
                if (!string.Equals(e.Label?.Trim(), tag, StringComparison.CurrentCultureIgnoreCase)) continue;

                if (e.Added)
                {
                    // Duas entradas seguidas (alguém reaplicou a label): a primeira continua
                    // valendo — o bloqueio não recomeçou, ele nunca parou.
                    if (inicio == null) { inicio = e.CreatedAt; bloqueouPor = e.Actor; }
                }
                else if (inicio != null)
                {
                    periodos.Add(new TfsImportService.BlockPeriod(inicio.Value, e.CreatedAt, bloqueouPor, e.Actor));
                    inicio = null;
                    bloqueouPor = "";
                }
            }

            if (inicio != null)
                periodos.Add(new TfsImportService.BlockPeriod(inicio.Value, null, bloqueouPor, ""));

            // "Em andamento desde": o GitHub não tem o estado Active do DevOps. O mais próximo e
            // honesto é o primeiro bloqueio conhecido — e, quando não há nenhum, nada. Inventar
            // uma data de início de trabalho a partir da criação da issue seria contar como
            // trabalho o tempo em que ela só estava no backlog.
            var desde = periodos.Count > 0 ? periodos[0].Start : (DateTime?)null;

            // O "tipo" aqui e o nivel que o item declara (Issue Type ou campo do board). Sem
            // declaracao, fica vazio: deduzir pela arvore exigiria o board inteiro, e a auditoria
            // e de UM item.
            var tipo = !string.IsNullOrWhiteSpace(item.IssueType) ? item.IssueType : item.LevelValue ?? "";
            return new TfsImportService.BlockAudit(
                item.Number, item.Title ?? "", tipo, GitProjectBoardService.StateOf(item),
                desde, item.ClosedAt, periodos);
        }
    }
}
