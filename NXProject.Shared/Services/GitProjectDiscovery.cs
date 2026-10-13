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
    /// Descobre, no GitHub, o que vira projeto do Portfólio do NXProject.
    ///
    /// No Azure DevOps o Discovery é uma consulta WIQL: "todo work item do tipo Project neste
    /// Team Project". No GitHub essa pergunta não existe — não há tipo validado nem busca por
    /// tipo no âmbito da conta. O que existe são **boards**. Então a descoberta tem dois níveis:
    ///
    ///   1. quais boards o dono tem;
    ///   2. dentro de um board, quais itens estão marcados como Project (`card_type`).
    ///
    /// E os dois usos reais são legítimos, por isso os dois são suportados:
    ///
    /// - **um board por projeto** — comum em time pequeno. Aí não há item algum marcado como
    ///   Project, e o projeto do Portfólio é o PRÓPRIO BOARD;
    /// - **um board para o portfólio inteiro** — aí cada item marcado como Project é um projeto.
    ///
    /// Tratar só o segundo caso deixaria de fora quem usa o GitHub do jeito mais comum.
    /// </summary>
    public static class GitProjectDiscovery
    {
        /// <summary>
        /// Projeto encontrado. <paramref name="BoardNumber"/> é o número do board (o que aparece
        /// na URL); <paramref name="RootIssue"/> é o número da issue raiz, ou 0 quando o próprio
        /// board é o projeto.
        /// </summary>
        public sealed record Found(int BoardNumber, string BoardTitle, int RootIssue, string Title,
                                   string Owner, int ItemCount);

        /// <summary>Boards do dono — o primeiro nível da descoberta.</summary>
        public static Task<List<(int Number, string Title)>> ListBoardsAsync(GitProjectOptions options)
            => new GitProjectClient(options).ListProjectsAsync();

        /// <summary>
        /// Projetos dentro de um board. Sem nenhum item marcado como Project, devolve o próprio
        /// board como projeto — é o caso "um board por projeto", e devolver vazio ali faria o
        /// Portfólio parecer que não há nada a importar.
        /// </summary>
        public static async Task<List<Found>> FindInBoardAsync(GitProjectOptions options)
        {
            var snap = await new GitProjectClient(options).LoadAsync();
            var porNumero = snap.Items.ToDictionary(i => i.Number);

            var raizes = snap.Items
                .Where(i => string.Equals(
                    GitProjectBoardService.LevelKindOf(i, porNumero, options), "Project",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (raizes.Count == 0)
                return new List<Found>
                {
                    new(options.ProjectNumber, snap.Title, 0, snap.Title, "", snap.Items.Count)
                };

            return raizes.Select(r => new Found(
                options.ProjectNumber, snap.Title, r.Number, r.Title, r.Assignee,
                // Quantos itens pendem desta raiz: ajuda a escolher, no Portfólio, o que importar.
                snap.Items.Count(i => DescendeDe(i, r.Number, porNumero)))).ToList();
        }

        private static bool DescendeDe(GitProjectItem item, int raiz,
                                       IReadOnlyDictionary<int, GitProjectItem> porNumero)
        {
            var atual = item;
            for (var i = 0; i < 10; i++)
            {
                if (atual.Number == raiz) return true;
                if (atual.ParentNumber <= 0 || !porNumero.TryGetValue(atual.ParentNumber, out var pai))
                    return false;
                atual = pai;
            }
            return false;
        }

        /// <summary>
        /// Converte o achado no projeto do Portfólio. O que o DevOps traz e o GitHub não tem
        /// fica vazio, de propósito: processo, grupo administrador e tipo de centro de custo não
        /// existem lá, e preencher com palpite seria pior que deixar em branco.
        /// </summary>
        public static DevOpsProject ToPortfolio(Found achado) => new()
        {
            Name = achado.Title,
            // Sem issue raiz, o board é o projeto: guarda o número do board negativo para não
            // colidir com número de issue, e deixa claro que a raiz não é um work item.
            RootWorkItemId = achado.RootIssue > 0 ? achado.RootIssue : -achado.BoardNumber,
            Owner = achado.Owner ?? "",
            Process = "",
            AdmGroupName = ""
        };
    }
}
