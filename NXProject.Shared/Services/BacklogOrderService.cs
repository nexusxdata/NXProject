// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// A ordem do backlog: qual é, e o que fazer para o servidor ficar com ela.
    ///
    /// Existe um serviço só porque a ORDEM é a mesma nos dois destinos — ela é o planejamento,
    /// não um detalhe de cada API. O que muda é só como cada servidor a guarda:
    ///
    ///   • **Azure DevOps**: um campo numérico por item (`StackRank` no Agile/CMMI,
    ///     `BacklogPriority` no Scrum). Ordenar é escolher números.
    ///   • **GitHub Projects**: não há campo — a ordem é a POSIÇÃO do item na view, e muda-se
    ///     item a item com `updateProjectV2ItemPosition`.
    ///
    /// A ordem desejada é a da ÁRVORE do cronograma, de cima para baixo: é como a pessoa a vê, e
    /// é a única definição que não depende de servidor.
    ///
    /// O custo do lado do GitHub foi MEDIDO, não suposto (`NXTestUnit gitorder`): a mutation
    /// funciona no topo e no meio, e a releitura a reflete — mas custa ~2s por chamada, e
    /// reescrever uma fila de 48 itens levou 98 segundos. Daí <see cref="PlanMoves"/> ser um
    /// diff: quem já está no lugar certo não é tocado.
    /// </summary>
    public static class BacklogOrderService
    {
        /// <summary>
        /// Um item que precisa mudar de lugar: vai logo DEPOIS de <paramref name="AfterId"/>, ou
        /// para o topo quando ele é 0.
        /// </summary>
        public sealed record Move(int Id, int AfterId);

        /// <summary>
        /// A ordem que o cronograma define: a árvore achatada, pai antes dos filhos, irmãos na
        /// ordem em que aparecem. Só entram itens com vínculo no servidor — o que ainda não
        /// existe lá não tem posição para ocupar.
        /// </summary>
        public static List<int> DesiredOrder(Project project, Func<int, bool>? existeNoServidor = null)
        {
            var ordem = new List<int>();
            var vistos = new HashSet<int>();

            void Desce(IEnumerable<ProjectTask> tarefas)
            {
                foreach (var t in tarefas)
                {
                    var id = t.TfsId ?? 0;
                    if (id > 0 && (existeNoServidor?.Invoke(id) ?? true) && vistos.Add(id))
                        ordem.Add(id);
                    Desce(t.Children);
                }
            }

            if (project?.Tasks != null) Desce(project.Tasks);
            return ordem;
        }

        /// <summary>
        /// Os movimentos que levam a fila ATUAL do servidor à ordem DESEJADA, mexendo no mínimo
        /// de itens possível.
        ///
        /// Item que o servidor tem e o cronograma não conhece fica onde está: ele não é nosso
        /// para reposicionar — pode ser trabalho de outra equipe no mesmo board.
        ///
        /// A fila é simulada a cada passo. Sem isso, cada decisão seria tomada contra uma ordem
        /// que o movimento anterior já desfez, e o plano pediria movimentos que se cancelam.
        /// </summary>
        public static List<Move> PlanMoves(IReadOnlyList<int> desejada, IReadOnlyList<int> atual)
        {
            var movimentos = new List<Move>();
            if (desejada == null || atual == null || desejada.Count < 2) return movimentos;

            var fila = atual.Where(n => n > 0).ToList();
            var anterior = 0;
            foreach (var id in desejada)
            {
                var atualIdx = fila.IndexOf(id);
                if (atualIdx < 0) continue;           // não está no board: nada a mover

                var destino = anterior == 0 ? 0 : fila.IndexOf(anterior) + 1;
                if (destino < 0) destino = 0;

                if (atualIdx != destino)
                {
                    movimentos.Add(new Move(id, anterior));
                    fila.RemoveAt(atualIdx);
                    if (destino > fila.Count) destino = fila.Count;
                    fila.Insert(destino, id);
                }
                anterior = id;
            }
            return movimentos;
        }

        /// <summary>
        /// O número de rank para um item que vai entre <paramref name="anterior"/> e
        /// <paramref name="proximo"/> — a forma do Azure DevOps de dizer a mesma coisa que o
        /// movimento diz no GitHub.
        ///
        /// Encaixar no MEIO quando há os dois vizinhos é o que evita reescrever o backlog inteiro
        /// a cada sincronização: mexer num item não muda o rank de nenhum outro. É o mesmo
        /// princípio do diff de movimentos do GitHub, com outra aritmética.
        /// </summary>
        public static double NextRank(double? anterior, double? proximo)
        {
            if (anterior.HasValue && proximo.HasValue && proximo.Value - anterior.Value > 0.0)
                return anterior.Value + (proximo.Value - anterior.Value) / 2.0;
            if (anterior.HasValue) return anterior.Value + 1000.0;
            if (proximo.HasValue) return proximo.Value - 1000.0;
            return 1000000000.0;   // grupo sem nenhum rank: começa no meio da faixa do DevOps
        }
    }
}
