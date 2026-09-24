// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;

namespace NXProject.Services
{
    /// <summary>
    /// Fonte única dos campos personalizados que o NXProject usa no Azure DevOps: como o campo se
    /// chama, que nomes alternativos são aceitos, que tipo de dado o NX grava nele e em quais
    /// work items ele faz falta.
    ///
    /// Existe porque a mesma regra era escrita em três lugares — as listas de apelido do import,
    /// o mapa de escopo da tela de configuração e o catálogo do instalador — e elas divergiram na
    /// primeira oportunidade: o instalador dizia que "Sync_version" não existia (não olhava os
    /// apelidos) e que "Perc_Conclusao" era double (o import grava inteiro). Com uma lista só,
    /// detectar no app e criar no Setup respondem sempre a mesma coisa.
    ///
    /// Quem consome:
    ///   • TfsImportService — resolve o campo no DevOps pelos <see cref="NxFieldSpec.Names"/>;
    ///   • tela "Configuração Integração Azure DevOps" — detecta e diz onde o campo deveria estar;
    ///   • NXProject-Setup — detecta e CRIA o que falta (a criação é só dele).
    /// </summary>
    public static class NxDevOpsFieldCatalog
    {
        /// <summary>
        /// Prefixo sugerido para organização nova. Serve para separar, na lista de campos do
        /// DevOps, o que é do NXProject do que é do processo da empresa. É só sugestão: os nomes
        /// padrão continuam os de sempre, para não quebrar quem já instalou.
        /// </summary>
        public const string SuggestedPrefix = "nx_";

        /// <summary>Um campo do NXProject no DevOps.</summary>
        public sealed class NxFieldSpec
        {
            /// <summary>Chave interna usada por todo mundo (effort, start, blockDuration…).</summary>
            public string Key { get; init; } = "";

            /// <summary>Nome padrão: o que a configuração traz e o que o Setup cria.</summary>
            public string DefaultName { get; init; } = "";

            /// <summary>
            /// Todos os nomes aceitos, NA ORDEM em que o import tenta resolver. A ordem importa:
            /// numa organização com dois deles cadastrados, ganha o primeiro da lista.
            /// </summary>
            public string[] Names { get; init; } = Array.Empty<string>();

            /// <summary>
            /// Tipo do dado na Process API (string, integer, double, dateTime, boolean). É o que o
            /// NX REALMENTE grava, não o que pareceria razoável — Perc_Conclusao, por exemplo, vai
            /// como inteiro arredondado de 0 a 100.
            /// </summary>
            public string Type { get; init; } = "string";

            /// <summary>
            /// Work items em que o campo faz falta. Cobrar o campo onde o NX nunca o usa só gera
            /// ruído no detector e campo a mais na organização.
            /// </summary>
            public string[] Scope { get; init; } = Array.Empty<string>();

            /// <summary>
            /// Work item -> campo PADRÃO do DevOps que o NXProject usa ali, no lugar do campo
            /// personalizado. Na Task, por exemplo, as horas saem de OriginalEstimate/CompletedWork,
            /// que já vêm de fábrica: não há campo personalizado para criar, e cobrar um seria pedir
            /// para duplicar informação que o DevOps já guarda.
            /// </summary>
            public Dictionary<string, string[]> StandardByType { get; init; } = new(StringComparer.CurrentCultureIgnoreCase);

            /// <summary>Os campos padrão que cobrem esse work item, ou vazio quando não há.</summary>
            public string[] StandardFor(string workItemType) =>
                StandardByType.TryGetValue(workItemType ?? "", out var refs) ? refs : Array.Empty<string>();

            /// <summary>
            /// Work item -> campo padrão do DevOps que PODE substituir o personalizado ali, se a
            /// pessoa quiser. Diferente de <see cref="StandardByType"/>, que é o que o NX já faz
            /// de fábrica: aqui é escolha, e ligar a opção grava um TypeFieldMappings no NX.
            /// </summary>
            public Dictionary<string, string> OptionalStandardByType { get; init; } = new(StringComparer.CurrentCultureIgnoreCase);

            /// <summary>O campo padrão que pode substituir o personalizado neste tipo, ou "".</summary>
            public string OptionalStandardFor(string workItemType) =>
                OptionalStandardByType.TryGetValue(workItemType ?? "", out var r) ? r : "";

            /// <summary>
            /// Tipos em que a opção "usar o campo padrão" já nasce LIGADA, porque é o que o
            /// NXProject faz hoje. No HH da Task, por exemplo, o import sempre leu Original
            /// Estimate: a caixa marcada só deixa explícito o que já acontecia — e desmarcá-la é
            /// uma escolha consciente de usar o campo personalizado ali.
            /// </summary>
            public string[] StandardOnByDefault { get; init; } = Array.Empty<string>();

            /// <summary>A opção nasce marcada para este work item?</summary>
            public bool StandardDefaultOn(string workItemType) =>
                StandardOnByDefault.Contains(workItemType ?? "", StringComparer.CurrentCultureIgnoreCase);

            /// <summary>O NX funciona sem ele (no Setup, nasce desmarcado).</summary>
            public bool Optional { get; init; }

            /// <summary>Para que serve — vira a descrição do campo criado no DevOps.</summary>
            public string Purpose { get; init; } = "";

            /// <summary>Os nomes alternativos, sem o padrão.</summary>
            public IEnumerable<string> Aliases =>
                Names.Where(n => !string.Equals(n, DefaultName, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Os campos, na ordem em que aparecem nas telas.
        ///
        /// O ESCOPO de cada um é onde o NXProject de fato usa o campo, e vale a pena ler junto:
        /// HH, datas e percentuais são planejamento de quem executa, e moram na Story e na Task.
        /// Na Feature e no EPIC esses números são consolidação dos filhos, calculada pelo
        /// cronograma — não há o que preencher lá, e associar o campo só encheria a organização
        /// de campo sem uso. Sync_version e Sync_Name são a exceção: o controle de conflito é do
        /// próprio NX e acompanha todo item que ele sincroniza, Feature e EPIC inclusive.
        /// </summary>
        public static IReadOnlyList<NxFieldSpec> All { get; } = new List<NxFieldSpec>
        {
            new()
            {
                Key = "effort", DefaultName = "HH Estimado", Type = "double",
                Names = new[] { "Esforço Estimado", "Esforco Estimado", "HH Estimado", "HH_Estimado" },
                Scope = new[] { "User Story", "Task" },
                // Na Task o NX lê Original Estimate / Completed Work de fábrica. Vem marcado, mas
                // é caixa: quem preferir manter o HH personalizado também na Task pode desmarcar.
                OptionalStandardByType = new(StringComparer.CurrentCultureIgnoreCase)
                { ["Task"] = "Microsoft.VSTS.Scheduling.OriginalEstimate" },
                StandardOnByDefault = new[] { "Task" },
                Purpose = "NXProject: horas estimadas da atividade"
            },
            new()
            {
                Key = "start", DefaultName = "Data_Inicio", Type = "dateTime",
                Names = new[] { "Data_Inicio", "Data Inicio", "DataInicio" },
                Scope = new[] { "User Story", "Task" },
                // Muita organização já usa o Start Date de fábrica na Task. Quem preferir liga a
                // opção no instalador e o NX passa a ler/gravar ele ali, sem campo personalizado.
                OptionalStandardByType = new(StringComparer.CurrentCultureIgnoreCase)
                { ["Task"] = "Microsoft.VSTS.Scheduling.StartDate" },
                // Nasce marcada: a Task do DevOps ja traz esse campo de fabrica, e e nele que as
                // equipes preenchem. Desmarcar volta ao campo personalizado na Task.
                StandardOnByDefault = new[] { "Task" },
                Purpose = "NXProject: data de início planejada"
            },
            new()
            {
                Key = "finish", DefaultName = "Data_Fim", Type = "dateTime",
                Names = new[] { "Data_Fim", "Data Fim", "DataFim" },
                Scope = new[] { "User Story", "Task" },
                OptionalStandardByType = new(StringComparer.CurrentCultureIgnoreCase)
                { ["Task"] = "Microsoft.VSTS.Scheduling.FinishDate" },
                // Nasce marcada: a Task do DevOps ja traz esse campo de fabrica, e e nele que as
                // equipes preenchem. Desmarcar volta ao campo personalizado na Task.
                StandardOnByDefault = new[] { "Task" },
                Purpose = "NXProject: data de fim planejada"
            },
            new()
            {
                Key = "percAloc", DefaultName = "Perc_Alocacao", Type = "double", Optional = true,
                Names = new[] { "Perc_Alocacao", "Perc_Alocação", "Perc_Aloc", "PercAloc",
                                "Perc Aloc", "Percentual Alocacao", "Percentual_Alocacao" },
                Scope = new[] { "User Story", "Task" },
                Purpose = "NXProject: percentual de alocação do recurso"
            },
            new()
            {
                // Gravado como (int)Math.Round(0..100) — inteiro, nao double.
                Key = "percConclusao", DefaultName = "Perc_Conclusao", Type = "integer", Optional = true,
                Names = new[] { "Perc_Conclusao", "Perc_Conclusão", "PercConclusao",
                                "Percentual Conclusao", "Percentual_Conclusao" },
                // So na Story: a Task nao tem esse campo. Sem ele, o percentual vem do ESTADO
                // (StateToPercent) e item encerrado e 100% — por isso o campo e opcional.
                Scope = new[] { "User Story" },
                Purpose = "NXProject: percentual concluído (inteiro, 0 a 100)"
            },
            new()
            {
                Key = "syncVersion", DefaultName = "Sync_version", Type = "integer", Optional = true,
                Names = new[] { "Sync_version", "SyncVersion", "Sync Version" },
                Scope = new[] { "User Story", "Feature", "Epic" },
                Purpose = "NXProject: versão da última sincronização (controle de conflito)"
            },
            new()
            {
                Key = "syncName", DefaultName = "Sync_Name", Type = "string", Optional = true,
                Names = new[] { "Sync_Name", "SyncName", "Sync Name" },
                Scope = new[] { "User Story", "Feature", "Epic" },
                Purpose = "NXProject: quem sincronizou por último"
            },
            new()
            {
                Key = "approved", DefaultName = "Approved", Type = "boolean", Optional = true,
                Names = new[] { "Approved", "Aprovado" },
                Scope = new[] { "Task" },
                Purpose = "NXProject: atividade aprovada"
            },
            new()
            {
                Key = "epicType", DefaultName = "EPIC_TYPE", Type = "string", Optional = true,
                Names = new[] { "EPIC_TYPE", "Tipo_Epic" },
                Scope = new[] { "Epic" },
                Purpose = "NXProject: tipo do EPIC"
            },
            new()
            {
                Key = "admGroup", DefaultName = "Adm_NX", Type = "string", Optional = true,
                Names = new[] { "Adm_NX", "AdmNX", "Adm NX" },
                Scope = new[] { "Project" },
                Purpose = "NXProject: grupo administrador do projeto"
            },
            new()
            {
                Key = "blockDuration", DefaultName = "block_duration_hours", Type = "double", Optional = true,
                Names = new[] { "block_duration_hours", "Block_Duration_Hours", "Block Duration Hours" },
                Scope = new[] { "Task" },
                Purpose = "NXProject: horas acumuladas em BLOCK"
            },
        };

        private static readonly Dictionary<string, NxFieldSpec> ByKey =
            All.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary>O campo pela chave, ou null quando a chave não é do catálogo.</summary>
        public static NxFieldSpec? Find(string key) =>
            key != null && ByKey.TryGetValue(key, out var f) ? f : null;

        /// <summary>Nomes aceitos, na ordem de resolução. Vazio para chave desconhecida.</summary>
        public static string[] Names(string key) => Find(key)?.Names ?? Array.Empty<string>();

        /// <summary>Work items em que o campo faz falta. Vazio para chave desconhecida.</summary>
        public static string[] Scope(string key) => Find(key)?.Scope ?? Array.Empty<string>();
    }
}
