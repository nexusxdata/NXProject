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
    /// Aplica no CRONOGRAMA o que o board deixou pendente — o equivalente local do "Atualizar
    /// TFS", só que escrevendo no `Project` em memória, para o NXProject salvar no `.nxproject`
    /// como salva qualquer outra edição.
    ///
    /// Duas diferenças importantes em relação ao caminho do DevOps:
    ///
    ///  • **Nada vai para servidor nenhum.** Mesmo que o cronograma tenha vindo do DevOps, o
    ///    ajuste fica no arquivo; levar para o servidor é uma segunda decisão, explícita, ligada
    ///    pelo Portfólio ("Sincronizar TaskBoard Local no DevOps").
    ///  • **Não há id novo a negociar.** A Story/Task criada no board nasce com id interno do
    ///    cronograma, igual ao que o NX já faz para atividade NoDevOps.
    /// </summary>
    public static class LocalBoardWriter
    {
        /// <summary>O que mudar no cronograma. Cada dicionário é uma fila do board.</summary>
        public sealed class Pending
        {
            public Dictionary<int, string> States { get; } = new();
            public Dictionary<int, string> Owners { get; } = new();
            public Dictionary<int, string> Titles { get; } = new();
            public Dictionary<int, double?> EstimateHours { get; } = new();
            public Dictionary<int, double?> CompletedHours { get; } = new();
            public Dictionary<int, DateTime?> StartDates { get; } = new();
            public Dictionary<int, DateTime?> FinishDates { get; } = new();
            public Dictionary<int, string> Tags { get; } = new();
            public Dictionary<int, string> Descriptions { get; } = new();
            public Dictionary<int, string> AcceptanceCriteria { get; } = new();
            public Dictionary<int, int> Priorities { get; } = new();
            public Dictionary<int, double> Ranks { get; } = new();
            public Dictionary<int, string> IterationPaths { get; } = new();
            /// <summary>Task → nova Story pai (reparent dentro do cronograma).</summary>
            public Dictionary<int, int> NewParents { get; } = new();
            public HashSet<int> Deletes { get; } = new();
        }

        /// <summary>Resultado da gravação, para a tela dizer o que aconteceu.</summary>
        public sealed record Result(int Applied, IReadOnlyList<string> Problems);

        /// <summary>
        /// Aplica as pendências. O que não achar atividade correspondente vira aviso, não exceção:
        /// o board pode ter ficado aberto enquanto o cronograma mudou.
        /// </summary>
        public static Result Apply(Project project, Pending pending, Resource[]? knownResources = null)
        {
            var problems = new List<string>();
            var applied = 0;
            if (project == null || pending == null) return new Result(0, problems);

            var byId = Flatten(project.Tasks).ToDictionary(t => t.Id);
            ProjectTask? Find(int id) => byId.TryGetValue(id, out var t) ? t : null;

            void ForEach<T>(Dictionary<int, T> fila, Action<ProjectTask, T> aplica, string oque)
            {
                foreach (var kv in fila)
                {
                    var t = Find(kv.Key);
                    if (t == null) { problems.Add($"#{kv.Key}: atividade não está mais no cronograma ({oque})"); continue; }
                    aplica(t, kv.Value);
                    applied++;
                }
            }

            ForEach(pending.Titles, (t, v) => t.Name = v, "título");
            ForEach(pending.Descriptions, (t, v) => t.Description = v, "descrição");
            ForEach(pending.AcceptanceCriteria, (t, v) => t.AcceptanceCriteria = v, "critérios");
            ForEach(pending.Tags, (t, v) => t.Tags = v, "tags");
            ForEach(pending.IterationPaths, (t, v) => t.TfsIterationPath = v, "sprint");
            ForEach(pending.Priorities, (t, v) => t.Priority = v, "prioridade");
            ForEach(pending.Ranks, (t, v) => t.TfsStackRank = v, "ordem");
            ForEach(pending.EstimateHours, (t, v) => t.EstimatedHours = v, "HH estimado");
            ForEach(pending.CompletedHours, (t, v) => t.CurrentHours = v, "HH realizado");
            ForEach(pending.StartDates, (t, v) => { if (v is { } d) { t.Start = d; t.StartFixed = true; } }, "data de início");
            ForEach(pending.FinishDates, (t, v) => { if (v is { } d) t.Finish = d; }, "data alvo");

            // Estado: além de gravar, acerta o percentual, que é o que o cronograma usa para
            // calcular. É a mesma regra do import (estado manda no percentual padrão).
            ForEach(pending.States, (t, v) =>
            {
                t.TfsState = v;
                var norm = TfsImportService.NormalizeTaskState(v);
                if (string.Equals(norm, "Closed", StringComparison.OrdinalIgnoreCase)) t.PercentComplete = 100;
                else if (string.Equals(norm, "New", StringComparison.OrdinalIgnoreCase)) t.PercentComplete = 0;
                else if (t.PercentComplete <= 0) t.PercentComplete = 50;
            }, "estado");

            // Responsável: casa pelo nome com os recursos do cronograma. Nome desconhecido é
            // avisado em vez de criar recurso silenciosamente — recurso é cadastro, não efeito
            // colateral de arrastar um card.
            foreach (var kv in pending.Owners)
            {
                var t = Find(kv.Key);
                if (t == null) { problems.Add($"#{kv.Key}: atividade não está mais no cronograma (responsável)"); continue; }
                var nome = (kv.Value ?? "").Trim();
                if (nome.Length == 0) { t.Resources.Clear(); applied++; continue; }
                var recurso = (knownResources ?? Array.Empty<Resource>())
                    .FirstOrDefault(r => string.Equals(r.Name, nome, StringComparison.CurrentCultureIgnoreCase));
                if (recurso == null) { problems.Add($"#{kv.Key}: recurso '{nome}' não existe no cronograma"); continue; }
                t.Resources.Clear();
                t.Resources.Add(new TaskResource { ResourceId = recurso.Id, Resource = recurso });
                applied++;
            }

            // Mover de pai: a Task sai da Story atual e entra na nova, mantendo a ordem relativa.
            foreach (var kv in pending.NewParents)
            {
                var filho = Find(kv.Key);
                var novoPai = Find(kv.Value);
                if (filho == null || novoPai == null)
                { problems.Add($"#{kv.Key}: não deu para mover (atividade ou destino ausente)"); continue; }
                var paiAtual = Flatten(project.Tasks).FirstOrDefault(p => p.Children.Any(c => c.Id == filho.Id));
                paiAtual?.Children.Remove(filho);
                novoPai.Children.Add(filho);
                applied++;
            }

            foreach (var id in pending.Deletes)
            {
                var t = Find(id);
                if (t == null) { problems.Add($"#{id}: já não está no cronograma"); continue; }
                var pai = Flatten(project.Tasks).FirstOrDefault(p => p.Children.Any(c => c.Id == id));
                if (pai != null) pai.Children.Remove(t);
                else project.Tasks.Remove(t);
                applied++;
            }

            return new Result(applied, problems);
        }

        private static IEnumerable<ProjectTask> Flatten(IEnumerable<ProjectTask>? tasks)
        {
            foreach (var t in tasks ?? Enumerable.Empty<ProjectTask>())
            {
                yield return t;
                foreach (var c in Flatten(t.Children)) yield return c;
            }
        }
    }
}
