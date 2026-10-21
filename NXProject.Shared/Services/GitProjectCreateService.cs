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
    /// Cria um item FILHO no GitProject a partir do board — o "+ EPIC", "+ Feature", "+ Story"
    /// e "+ Task" do TaskBoard.
    ///
    /// No Azure DevOps isso é uma chamada: o work item nasce já com tipo, pai e campos. No GitHub
    /// são quatro passos, e é por isso que cada um deles reporta o próprio erro em vez de deixar
    /// um item pela metade sem explicação:
    ///
    ///   1. a ISSUE nasce num repositório (o GitHub não tem item solto);
    ///   2. ela entra no BOARD do projeto;
    ///   3. vira SUB-ISSUE do pai, que é como a hierarquia existe aqui;
    ///   4. recebe o nível (card_type), que é quem diz se ela é EPIC, Feature, Story ou Task.
    ///
    /// O repositório sai do PAI: a issue nova nasce junto da issue que a gerou, que é o que a
    /// pessoa espera ao clicar "+ Story" dentro de uma Feature. Só quando o pai não tem
    /// repositório conhecido é que vale o configurado em <see cref="GitProjectOptions.Repository"/>.
    /// </summary>
    public static class GitProjectCreateService
    {
        /// <summary>
        /// Cria o filho JÁ COM os campos do card (sprint, responsável, HH e início) e devolve o
        /// NÚMERO da issue (0 = não criou, com o motivo em Message). O que não deu para gravar
        /// volta em <paramref name="warnings"/> — o item existe, e dizer isso é melhor do que
        /// falhar tudo.
        ///
        /// Os campos são gravados AQUI, e não pelo gravador do board, por uma razão concreta: o
        /// gravador procura o item pelo número numa releitura do projeto, e o item recém-criado
        /// ainda não aparecia nela ("#96 não está neste board"). Aqui o id do item do board já
        /// está na mão — veio do passo que o adicionou — e o repositório também, que é de onde
        /// sai a lista de quem pode ser responsável (sem depender do repositório configurado).
        /// </summary>
        public static async Task<(int Number, string Message)> CreateChildAsync(
            GitProjectOptions options, int parentNumber, string kind, string title,
            string? description = null, IProgress<string>? progress = null,
            string? sprintTitle = null, string? assignee = null,
            double? effortHours = null, DateTime? startDate = null,
            List<string>? warnings = null)
        {
            if (options?.IsConfigured != true)
                return (0, NxText.T("Conexão do GitProject incompleta.", "Incomplete GitProject connection."));
            if (string.IsNullOrWhiteSpace(title))
                return (0, NxText.T("Informe o nome do item.", "Enter the item name."));

            var cliente = new GitProjectClient(options);
            try
            {
                progress?.Report(NxText.T("Lendo o board…", "Reading the board…"));
                var snap = await cliente.LoadAsync();

                var pai = snap.Items.FirstOrDefault(i => i.Number == parentNumber);
                var paiIssueId = pai?.IssueId ?? "";
                string? repoDoPai = null;
                if (string.IsNullOrEmpty(paiIssueId))
                {
                    // Pai recém-criado: a issue já existe, mas a consulta do projeto pode levar
                    // minutos para mostrá-la. Procura pelo número nos repositórios do board.
                    (paiIssueId, repoDoPai) = await PaiPeloNumeroAsync(cliente, snap, options, parentNumber);
                    if (string.IsNullOrEmpty(paiIssueId))
                        return (0, NxText.T($"Item pai #{parentNumber} não foi encontrado no board.",
                                            $"Parent item #{parentNumber} was not found on the board."));
                    TfsImportService.BoardLog.Write($"CRIAR GitProject: pai #{parentNumber} achado pelo numero em {repoDoPai} (ainda fora da consulta do board).");
                }

                // Repositório do PAI; sem ele, o configurado. Erro claro quando não há nenhum —
                // é a mesma regra (e a mesma mensagem) do sincronizar do cronograma.
                var nomeRepo = repoDoPai ?? await cliente.IssueRepositoryAsync(paiIssueId);
                if (string.IsNullOrWhiteSpace(nomeRepo)) nomeRepo = options.Repository;
                var partes = (nomeRepo ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 2)
                    return (0, NxText.T(
                        "Não dá para criar: informe o repositório (dono/repo) na configuração do GitProject.",
                        "Cannot create: set the repository (owner/repo) in the GitProject configuration."));

                var repoId = await cliente.RepositoryIdAsync(partes[0].Trim(), partes[1].Trim());

                progress?.Report(NxText.T($"Criando '{title}'…", $"Creating '{title}'…"));
                var corpo = string.IsNullOrWhiteSpace(description)
                    ? NxText.T("Criado pelo NXProject (TaskBoard).", "Created by NXProject (TaskBoard).")
                    : description!;
                var (numero, issueId) = await cliente.CreateIssueAsync(repoId, title.Trim(), corpo);

                TfsImportService.BoardLog.Write(
                    $"CRIAR GitProject: issue #{numero} criada em {partes[0]}/{partes[1]} (pai #{parentNumber}).");

                var itemId = await cliente.AddToProjectAsync(snap.ProjectId, issueId);
                TfsImportService.BoardLog.Write(
                    $"CRIAR GitProject: #{numero} entrou no board, itemId={itemId}.");
                await cliente.AddSubIssueAsync(paiIssueId, issueId);

                // Nível: sem ele o item nasce sem saber o que é, e o board o trataria pela
                // posição na árvore — que é justamente o palpite que o campo existe para evitar.
                var campoNivel = snap.Fields.FirstOrDefault(f => GitProjectOptions.Matches(f.Name, options.LevelFields));
                var nivel = NivelDoTipo(kind);
                if (campoNivel != null && nivel.Length > 0
                    && campoNivel.Options.TryGetValue(nivel, out var opcaoId))
                {
                    await cliente.SetFieldAsync(snap.ProjectId, itemId, campoNivel.Id, new GitSingleSelect(opcaoId));
                    TfsImportService.BoardLog.Write($"CRIAR GitProject: #{numero} nivel='{nivel}' gravado.");
                }
                else
                    TfsImportService.BoardLog.Write(
                        $"CRIAR GitProject: #{numero} SEM nivel — campo='{campoNivel?.Name ?? "(nao existe)"}', valor='{nivel}'.");

                await AplicaCamposAsync(cliente, snap, options, itemId, issueId,
                    partes[0].Trim(), partes[1].Trim(),
                    sprintTitle, assignee, effortHours, startDate,
                    warnings ?? new List<string>(), progress);

                return (numero, NxText.T($"#{numero} criado em {partes[0]}/{partes[1]}.",
                                         $"#{numero} created in {partes[0]}/{partes[1]}."));
            }
            catch (Exception ex)
            {
                return (0, ex.Message);
            }
        }

        /// <summary>
        /// Acha a issue pai pelo número quando ela ainda não aparece na consulta do projeto: tenta o
        /// repositório configurado e os repositórios de alguns itens do board (em geral é um só).
        /// </summary>
        internal static async Task<(string IssueId, string? Repo)> PaiPeloNumeroAsync(
            GitProjectClient cliente, GitProjectClient.ProjectSnapshot snap, GitProjectOptions options, int numero)
        {
            var repos = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.Repository)) repos.Add(options.Repository.Trim());
            foreach (var item in snap.Items.Where(i => !string.IsNullOrEmpty(i.IssueId)).Take(3))
            {
                try
                {
                    var r = await cliente.IssueRepositoryAsync(item.IssueId);
                    if (!string.IsNullOrWhiteSpace(r) && !repos.Contains(r, StringComparer.OrdinalIgnoreCase)) repos.Add(r);
                }
                catch { /* item sem repositório legível: tenta o próximo */ }
            }
            foreach (var repo in repos)
            {
                var partes = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length != 2) continue;
                try
                {
                    var id = await cliente.IssueIdByNumberAsync(partes[0].Trim(), partes[1].Trim(), numero);
                    if (id.Length > 0) return (id, repo);
                }
                catch { /* sem acesso a esse repositório: tenta o próximo */ }
            }
            return ("", null);
        }

        /// <summary>
        /// Grava no item recém-criado o que veio do card. Cada valor é independente: falhar num
        /// não impede os outros, e o que falhou vira aviso com o nome do campo.
        /// </summary>
        private static async Task AplicaCamposAsync(
            GitProjectClient cliente, GitProjectClient.ProjectSnapshot snap, GitProjectOptions options,
            string itemId, string issueId, string dono, string repo,
            string? sprintTitle, string? assignee, double? effortHours, DateTime? startDate,
            List<string> avisos, IProgress<string>? progress)
        {
            GitProjectField? Campo(IEnumerable<string> nomes) =>
                snap.Fields.FirstOrDefault(f => GitProjectOptions.Matches(f.Name, nomes));

            async Task Grava(string conceito, IEnumerable<string> nomes, object valor)
            {
                var campo = Campo(nomes);
                if (campo == null)
                {
                    avisos.Add(NxText.T("O board não tem o campo de {0}: esse valor não foi gravado.",
                                        "The board has no {0} field: that value was not written.", conceito));
                    return;
                }
                try { await cliente.SetFieldAsync(snap.ProjectId, itemId, campo.Id, valor); }
                catch (Exception ex) { avisos.Add($"{conceito}: {ex.Message}"); }
            }

            // ── Sprint: é o campo que faz o item APARECER no board filtrado por sprint ──────
            if (!string.IsNullOrWhiteSpace(sprintTitle))
            {
                progress?.Report(NxText.T("Gravando a sprint…", "Writing the sprint…"));
                var campoIter = Campo(options.IterationFields);
                var iter = snap.Iterations.FirstOrDefault(x =>
                    string.Equals(x.Title?.Trim(), sprintTitle!.Trim(), StringComparison.CurrentCultureIgnoreCase));
                if (campoIter == null || iter == null)
                {
                    TfsImportService.BoardLog.Write(
                        $"CRIAR GitProject: sprint '{sprintTitle}' NAO gravada — campo='{campoIter?.Name ?? "(nao existe)"}', "
                        + $"iteracoes do board=[{string.Join(" | ", snap.Iterations.Select(x => x.Title))}].");
                    avisos.Add(NxText.T("A sprint '{0}' não existe neste board — não foi gravada.",
                                        "Sprint '{0}' does not exist on this board — it was not written.", sprintTitle));
                }
                else
                    try
                    {
                        await cliente.SetIterationAsync(snap.ProjectId, itemId, campoIter.Id, iter.Id);
                        TfsImportService.BoardLog.Write(
                            $"CRIAR GitProject: sprint '{iter.Title}' gravada (campo '{campoIter.Name}', itemId={itemId}).");
                    }
                    catch (Exception ex) { avisos.Add($"sprint: {ex.Message}"); }
            }

            // ── Responsável: as contas vêm do repositório DA ISSUE, não do configurado ─────
            if (!string.IsNullOrWhiteSpace(assignee))
            {
                progress?.Report(NxText.T("Gravando o responsável…", "Writing the assignee…"));
                try
                {
                    var contas = await cliente.AssignableAccountsAsync(dono, repo);
                    var r = GitProjectPeopleService.Resolve(assignee!, contas);
                    if (!r.Ok) avisos.Add(r.Problem ?? "");
                    else await cliente.SetAssigneeAsync(issueId, r.Account!.Id);
                }
                catch (Exception ex) { avisos.Add($"responsável: {ex.Message}"); }
            }

            if (effortHours is { } hh && hh > 0)
                await Grava(NxText.T("HH estimado", "estimated hours"), options.EffortFields, hh);

            if (startDate is { } ini)
                await Grava(NxText.T("data de início", "start date"), options.StartDateFields, ini);
        }

        /// <summary>
        /// Espera o item recém-criado APARECER na leitura do projeto, e diz se apareceu.
        ///
        /// O GitHub leva alguns segundos para publicar o item novo na consulta do projeto: a
        /// issue já existe, já está no board e já tem sprint — medido no servidor —, mas a
        /// leitura feita logo depois ainda devolve o projeto sem ela. Recarregar o board nesse
        /// intervalo dava o pior resultado possível: o card sumia da tela e parecia não ter
        /// sido criado, quando estava tudo certo no servidor.
        ///
        /// Por isso a espera é uma releitura com intervalo crescente, e não um atraso fixo: na
        /// maioria das vezes o item já vem na primeira tentativa.
        /// </summary>
        public static async Task<bool> EsperaAparecerAsync(
            GitProjectOptions options, int numero, IProgress<string>? progress = null)
        {
            var cliente = new GitProjectClient(options);
            foreach (var espera in new[] { 0, 2000, 3000, 5000 })
            {
                if (espera > 0)
                {
                    progress?.Report(NxText.T($"Aguardando #{numero} aparecer no board…",
                                              $"Waiting for #{numero} to show up on the board…"));
                    await Task.Delay(espera);
                }
                try
                {
                    var snap = await cliente.LoadAsync();
                    if (snap.Items.Any(i => i.Number == numero))
                    {
                        TfsImportService.BoardLog.Write($"CRIAR GitProject: #{numero} ja aparece na leitura do projeto.");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    TfsImportService.BoardLog.Write($"CRIAR GitProject: leitura falhou enquanto esperava #{numero}: {ex.Message}");
                }
            }
            TfsImportService.BoardLog.Write(
                $"CRIAR GitProject: #{numero} NAO apareceu na leitura do projeto depois de 10s — "
                + "o item existe no servidor, mas o board so o mostrara no proximo refresh.");
            return false;
        }

        /// <summary>Nome do nível como o campo single-select do board o escreve.</summary>
        private static string NivelDoTipo(string? tipo) =>
            NxLevelMap.Current.LevelOf(tipo) is { } n ? NxLevelMap.Current.Suggested(n) : "";
    }
}
