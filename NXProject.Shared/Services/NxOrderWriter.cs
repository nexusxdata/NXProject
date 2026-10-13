// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Fachada da ORDEM do backlog: "aplique no servidor a ordem do cronograma".
    ///
    /// Quem chama não sabe — nem precisa saber — que no Azure DevOps ordenar é escolher números
    /// num campo e no GitHub é mover o item na view. É a mesma ideia do
    /// <see cref="INxBoardSource"/>: a fachada ENCAMINHA, não decide. A regra de qual é a ordem
    /// mora no <see cref="BacklogOrderService"/>, uma vez só, e os dois destinos a consomem.
    /// </summary>
    public interface INxOrderWriter
    {
        /// <summary>
        /// Põe o servidor na ordem do cronograma e devolve quantos itens mudaram de lugar.
        /// Zero significa que já estava certo — e não que nada foi tentado.
        /// </summary>
        Task<OrderResult> ApplyAsync(Project project, IProgress<string>? progress = null);
    }

    /// <summary>
    /// O que a ordenação fez. Separado de um simples número porque a parte interessante é o que
    /// NÃO deu: item fora do board, falha no meio, servidor que não guarda ordem.
    /// </summary>
    public sealed class OrderResult
    {
        public int Moved { get; set; }
        public int Unchanged { get; set; }
        public List<string> Warnings { get; } = new();
    }

    /// <summary>Escolhe a implementação pelo destino. Não decide nada além disso.</summary>
    public static class NxOrderWriters
    {
        public static INxOrderWriter For(NxBackendKind kind, TfsConnectionOptions options,
                                         GitProjectOptions? git = null) => kind switch
        {
            NxBackendKind.GitProject => new GitProjectOrderWriter(git ?? options?.GitProject ?? new GitProjectOptions()),
            NxBackendKind.Local => new LocalOrderWriter(),
            _ => new DevOpsOrderWriter(options ?? new TfsConnectionOptions())
        };
    }

    /// <summary>
    /// Azure DevOps: a ordem é um número por item (`StackRank` no Agile/CMMI, `BacklogPriority`
    /// no Scrum). Grava só quem está fora de lugar, encaixando o rank ENTRE os vizinhos — assim
    /// mexer num item não obriga a reescrever o backlog inteiro.
    /// </summary>
    public sealed class DevOpsOrderWriter : INxOrderWriter
    {
        private readonly TfsConnectionOptions _options;
        public DevOpsOrderWriter(TfsConnectionOptions options) => _options = options;

        public async Task<OrderResult> ApplyAsync(Project project, IProgress<string>? progress = null)
        {
            var resultado = new OrderResult();
            var desejada = BacklogOrderService.DesiredOrder(project);
            if (desejada.Count < 2) return resultado;

            // O rank atual de cada item vem do cronograma, que o guarda na importação. Reler o
            // servidor aqui seria uma segunda consulta para a mesma informação.
            var ranks = new Dictionary<int, double?>();
            foreach (var t in Todas(project.Tasks))
                if (t.TfsId is > 0) ranks[t.TfsId.Value] = t.TfsStackRank;

            double? anterior = null;
            foreach (var id in desejada)
            {
                var atual = ranks.TryGetValue(id, out var r) ? r : null;
                // Já está depois do anterior: nada a fazer. É o equivalente de "não mover".
                if (atual.HasValue && (!anterior.HasValue || atual.Value > anterior.Value))
                {
                    anterior = atual;
                    resultado.Unchanged++;
                    continue;
                }

                var proximo = ProximoRankMaior(desejada, ranks, id, anterior);
                var novo = BacklogOrderService.NextRank(anterior, proximo);
                try
                {
                    progress?.Report(NxText.T("Ordenando #{0}…", "Ordering #{0}…", id));
                    var (ok, msg) = await TfsImportService.SetWorkItemStackRankAsync(_options, id, novo);
                    if (ok) { resultado.Moved++; ranks[id] = novo; }
                    else resultado.Warnings.Add($"#{id}: {msg}");
                }
                catch (Exception ex)
                {
                    resultado.Warnings.Add($"#{id}: {ex.Message}");
                }
                anterior = novo;
            }
            return resultado;
        }

        /// <summary>Primeiro rank adiante que ainda é maior que o anterior — o teto do encaixe.</summary>
        private static double? ProximoRankMaior(IReadOnlyList<int> desejada,
            IReadOnlyDictionary<int, double?> ranks, int id, double? anterior)
        {
            var i = -1;
            for (var k = 0; k < desejada.Count; k++) if (desejada[k] == id) { i = k; break; }
            for (var j = i + 1; j >= 0 && j < desejada.Count; j++)
                if (ranks.TryGetValue(desejada[j], out var r) && r.HasValue
                    && (!anterior.HasValue || r.Value > anterior.Value))
                    return r.Value;
            return null;
        }

        private static IEnumerable<ProjectTask> Todas(IEnumerable<ProjectTask> tarefas)
        {
            foreach (var t in tarefas)
            {
                yield return t;
                foreach (var f in Todas(t.Children)) yield return f;
            }
        }
    }

    /// <summary>
    /// GitHub Projects: não há campo de ordem — ela é a posição do item na view, movida uma a
    /// uma. Funciona (medido em `NXTestUnit gitorder`, no topo e no meio), mas custa ~2s por
    /// movimento: por isso só se move o que está fora de lugar.
    /// </summary>
    public sealed class GitProjectOrderWriter : INxOrderWriter
    {
        private readonly GitProjectOptions _git;
        public GitProjectOrderWriter(GitProjectOptions git) => _git = git;

        public async Task<OrderResult> ApplyAsync(Project project, IProgress<string>? progress = null)
        {
            var resultado = new OrderResult();
            if (_git?.IsConfigured != true)
            {
                resultado.Warnings.Add(NxText.T("Ordem não aplicada: conexão do GitProject incompleta.",
                                                 "Order not applied: incomplete GitProject connection."));
                return resultado;
            }

            var cliente = new GitProjectClient(_git);
            var snap = await cliente.LoadAsync();
            var porNumero = snap.Items.ToDictionary(i => i.Number);
            var itemIdPorNumero = porNumero.Values
                .Where(i => !string.IsNullOrEmpty(i.ItemId))
                .ToDictionary(i => i.Number, i => i.ItemId);

            var fila = await cliente.ListItemIdsAsync(snap.ProjectId);
            var numeroPorItemId = itemIdPorNumero.ToDictionary(kv => kv.Value, kv => kv.Key);
            var ordemAtual = fila.Select(id => numeroPorItemId.TryGetValue(id, out var n) ? n : 0)
                                 .Where(n => n > 0).ToList();

            var desejada = BacklogOrderService.DesiredOrder(project, id => porNumero.ContainsKey(id));
            var movimentos = BacklogOrderService.PlanMoves(desejada, ordemAtual);
            resultado.Unchanged = desejada.Count - movimentos.Count;

            foreach (var m in movimentos)
            {
                if (!itemIdPorNumero.TryGetValue(m.Id, out var itemId)) continue;
                string? depois = null;
                if (m.AfterId > 0 && itemIdPorNumero.TryGetValue(m.AfterId, out var ancora)) depois = ancora;
                try
                {
                    progress?.Report(NxText.T("Ordenando #{0}…", "Ordering #{0}…", m.Id));
                    await cliente.MoveItemAsync(snap.ProjectId, itemId, depois);
                    resultado.Moved++;
                }
                catch (Exception ex)
                {
                    resultado.Warnings.Add($"#{m.Id}: {ex.Message}");
                }
            }
            return resultado;
        }
    }

    /// <summary>
    /// Modo local: a ordem JÁ é a do cronograma — não há servidor onde gravá-la. Existe para a
    /// fachada não ter buraco: quem chama não precisa perguntar o destino antes de pedir.
    /// </summary>
    public sealed class LocalOrderWriter : INxOrderWriter
    {
        public Task<OrderResult> ApplyAsync(Project project, IProgress<string>? progress = null) =>
            Task.FromResult(new OrderResult());
    }
}
