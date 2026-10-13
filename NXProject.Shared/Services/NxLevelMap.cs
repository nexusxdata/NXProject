// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NXProject.Services
{
    /// <summary>Os cinco níveis do NXProject, do topo para a folha.</summary>
    public enum NxLevel { Project = 0, Epic = 1, Feature = 2, Story = 3, Task = 4 }

    /// <summary>
    /// Que NOME de tipo corresponde a cada nível do NXProject — e quais pares de níveis a
    /// hierarquia aceita.
    ///
    /// Isto existe porque os nomes estavam presos no código: `epic`/`epico`,
    /// `feature`/`funcionalidade`, `project`/`projeto`. Quem batizou o tipo de "Iniciativa",
    /// "Programa" ou "Produto" ficava de fora sem recompilar — e isso é limitação do conector do
    /// DevOps de hoje, não só uma necessidade do GitHub.
    ///
    /// É também o lugar ÚNICO da hierarquia: o DevOps recusa Task sob Feature por nós, o GitHub
    /// não recusa nada, e o NXProject precisa da mesma resposta nos dois. Ter a regra em dois
    /// lugares foi o que já deu errado com o catálogo de campos.
    ///
    /// Um nível pode ser DESLIGADO: time sem o nível Project começa no Epic; time sem Task
    /// trabalha no nível Story, que o cronograma já trata como folha. Desligar muda a validação —
    /// com Task desligada, nada pode ficar sob a Story.
    /// </summary>
    public sealed class NxLevelMap
    {
        /// <summary>
        /// Valor de EPIC_TYPE que marca um EPIC como AGRUPADOR de EPICs.
        ///
        /// Mora aqui, e não junto dos outros valores de EPIC_TYPE, porque é hierarquia: é a única
        /// exceção à regra de "o pai é o nível imediatamente acima", e quem responde por ela é
        /// este mapa. <c>EpicTypes.Group</c> aponta para cá, então continua havendo um valor só.
        /// </summary>
        public const string EpicGroupValue = "EPIC_GROUP";

        /// <summary>O EPIC_TYPE informado marca um agrupador?</summary>
        public static bool IsEpicGroup(string? epicType)
            => string.Equals(epicType?.Trim(), EpicGroupValue, StringComparison.OrdinalIgnoreCase);

        public static readonly NxLevel[] AllLevels =
            { NxLevel.Project, NxLevel.Epic, NxLevel.Feature, NxLevel.Story, NxLevel.Task };

        /// <summary>
        /// Nomes aceitos por nível. Os de fábrica são exatamente os que estavam no código, para
        /// que quem não configurar nada não veja diferença nenhuma.
        /// </summary>
        private static readonly Dictionary<NxLevel, string[]> Fabrica = new()
        {
            [NxLevel.Project] = new[] { "Project", "Projeto", "Work Item Project" },
            [NxLevel.Epic] = new[] { "Epic", "Épico", "Epico" },
            [NxLevel.Feature] = new[] { "Feature", "Funcionalidade", "Entrega" },
            [NxLevel.Story] = new[] { "Story", "User Story", "Product Backlog Item", "Requirement",
                                      "História de Usuário", "História", "Historia" },
            [NxLevel.Task] = new[] { "Task", "Tarefa", "Sub-task", "Subtask" },
        };

        public Dictionary<NxLevel, List<string>> Names { get; } = Fabrica.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToList());

        /// <summary>Níveis desligados. Vazio = os cinco em uso.</summary>
        public HashSet<NxLevel> Disabled { get; } = new();

        /// <summary>Nomes de fábrica de um nível — serve ao botão "restaurar padrões" da tela.</summary>
        public static IReadOnlyList<string> DefaultNames(NxLevel nivel) => Fabrica[nivel];

        /// <summary>
        /// O mapa em vigor nesta execução. Estático porque os pontos que precisam dele —
        /// a montagem do board, o import e o sync — são métodos estáticos espalhados, e passar a
        /// configuração por todos eles seria mexer em centenas de assinaturas para a mesma
        /// resposta. Quem carrega a configuração atribui aqui, uma vez.
        /// </summary>
        public static NxLevelMap Current { get; set; } = new();

        /// <summary>
        /// Nível deste tipo de work item, ou null quando o nome não é de nenhum nível. Compara sem
        /// caixa, sem acento e sem espaços: processo customizado escreve "Épico", "epico" e
        /// "User  Story" e os três têm de cair no mesmo lugar.
        /// </summary>
        public NxLevel? LevelOf(string? tipo)
        {
            var chave = Normaliza(tipo);
            if (chave.Length == 0) return null;
            foreach (var nivel in AllLevels)
            {
                if (Disabled.Contains(nivel)) continue;
                if (Names.TryGetValue(nivel, out var nomes) && nomes.Any(n => Normaliza(n) == chave))
                    return nivel;
            }
            return null;
        }

        public bool Is(string? tipo, NxLevel nivel) => LevelOf(tipo) == nivel;

        /// <summary>
        /// O par pai → filho é válido? O filho tem de ser o PRÓXIMO nível em uso: com o nível
        /// Feature desligado, Epic passa a aceitar Story diretamente — senão desligar um nível
        /// quebraria o cronograma inteiro em vez de simplificá-lo.
        /// </summary>
        public bool ParentChildOk(string? tipoPai, string? tipoFilho)
            => ParentChildOk(tipoPai, tipoFilho, null, null);

        /// <summary>
        /// Igual à versão acima, mas enxergando o EPIC_TYPE dos dois lados — e é só por causa do
        /// <see cref="NXProject.Models.EpicTypes.Group"/> que isso existe.
        ///
        /// O EPIC_GROUP é a ÚNICA exceção à regra de "o pai é o nível imediatamente acima": ele é
        /// um Epic que agrupa Epics. As três restrições que o mantêm previsível:
        ///
        ///   • Epic sob Epic vale só quando o pai é um grupo (senão a hierarquia vira árvore livre);
        ///   • um grupo não entra sob outro grupo — um nível de agrupamento, não uma escada;
        ///   • sob o grupo só vêm EPICs. Feature pendurada direto nele pularia um nível, e o
        ///     cronograma mostraria entrega sem o Epic que a explica.
        /// </summary>
        public bool ParentChildOk(string? tipoPai, string? tipoFilho,
                                  string? epicTypePai, string? epicTypeFilho)
        {
            var pai = LevelOf(tipoPai);
            var filho = LevelOf(tipoFilho);
            if (pai == null || filho == null) return false;

            if (pai.Value == NxLevel.Epic && IsEpicGroup(epicTypePai))
                return filho.Value == NxLevel.Epic && !IsEpicGroup(epicTypeFilho);

            // Grupo é Epic: entra onde um Epic entra (sob o Work Item Project), e nunca sob outro Epic.
            if (filho.Value == NxLevel.Epic && IsEpicGroup(epicTypeFilho))
                return pai.Value == NxLevel.Project;

            return ProximoEmUso(pai.Value) == filho.Value;
        }

        /// <summary>O nível imediatamente abaixo que está em uso, ou o próprio quando não há.</summary>
        public NxLevel ProximoEmUso(NxLevel nivel)
        {
            for (var i = (int)nivel + 1; i <= (int)NxLevel.Task; i++)
                if (!Disabled.Contains((NxLevel)i)) return (NxLevel)i;
            return nivel;
        }

        /// <summary>O nível é folha? É o mais baixo em uso — nele o cronograma para de descer.</summary>
        public bool IsLeaf(NxLevel nivel) => ProximoEmUso(nivel) == nivel;

        /// <summary>Nome SUGERIDO deste nível: o primeiro da lista, que é o usado ao criar item.</summary>
        public string Suggested(NxLevel nivel) =>
            Names.TryGetValue(nivel, out var nomes) && nomes.Count > 0 ? nomes[0] : nivel.ToString();

        /// <summary>
        /// Um nome que aparece em mais de um nível é ambiguidade, e ambiguidade aqui monta o
        /// cronograma torto em silêncio. A tela mostra isto antes de salvar.
        /// </summary>
        public List<string> Ambiguities()
        {
            var vistos = new Dictionary<string, NxLevel>();
            var achados = new List<string>();
            foreach (var nivel in AllLevels)
                foreach (var nome in Names.TryGetValue(nivel, out var l) ? l : new List<string>())
                {
                    var chave = Normaliza(nome);
                    if (chave.Length == 0) continue;
                    if (vistos.TryGetValue(chave, out var outro) && outro != nivel)
                        achados.Add($"'{nome.Trim()}' está em {outro} e em {nivel}");
                    else vistos[chave] = nivel;
                }
            return achados;
        }

        public NxLevelMap Clone()
        {
            var copia = new NxLevelMap();
            foreach (var nivel in AllLevels)
                copia.Names[nivel] = Names.TryGetValue(nivel, out var l) ? l.ToList() : new List<string>();
            foreach (var d in Disabled) copia.Disabled.Add(d);
            return copia;
        }

        private static string Normaliza(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var form = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(form.Length);
            foreach (var ch in form)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                        != System.Globalization.UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(ch))
                    sb.Append(ch);
            return sb.ToString();
        }
    }
}
