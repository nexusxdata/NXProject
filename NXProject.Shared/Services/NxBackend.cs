// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;

namespace NXProject.Services
{
    /// <summary>Contra o que o NXProject trabalha neste cronograma.</summary>
    public enum NxBackendKind
    {
        /// <summary>Segue o padrão do NXProject (hoje, Azure DevOps).</summary>
        Default = 0,
        AzureDevOps = 1,
        GitProject = 2,
        /// <summary>Só o arquivo: cronograma aberto + pasta do projeto, sem servidor.</summary>
        Local = 3
    }

    /// <summary>Onde o anexo mora neste destino.</summary>
    public enum NxAttachmentStore
    {
        /// <summary>Não existe anexo neste destino.</summary>
        None,
        /// <summary>No próprio work item do servidor.</summary>
        Server,
        /// <summary>Em arquivos, na pasta do projeto.</summary>
        LocalFolder
    }

    /// <summary>
    /// O que cada destino sabe fazer. Existe para a tela **perguntar pela capacidade**, nunca pelo
    /// nome do destino: <c>if (caps.Comments)</c> em vez de <c>if (kind == AzureDevOps)</c>.
    ///
    /// A diferença não é de estilo. Com a pergunta certa, um destino novo é uma linha nesta
    /// tabela e nenhuma tela precisa ser reaberta — nem corre o risco de ser esquecida. E o botão
    /// que não funciona some (ou fica desabilitado com o motivo) em vez de dar erro no clique.
    ///
    /// O objeto é IMUTÁVEL de propósito: o NXProject abre várias janelas sobre o mesmo
    /// cronograma, e cada uma recebe a sua cópia ao abrir. Um valor global mutável deixaria telas
    /// abertas com as capacidades de outro destino.
    /// </summary>
    public sealed record NxBackendCapabilities(
        /// <summary>Onde o anexo do card é guardado.</summary>
        NxAttachmentStore Attachments,
        /// <summary>Comentários / trâmite no item.</summary>
        bool Comments,
        /// <summary>Auditoria de BLOCK (precisa de histórico — do servidor ou do log local).</summary>
        bool BlockAudit,
        /// <summary>🔗 abrir o item no navegador.</summary>
        bool OpenInBrowser,
        /// <summary>Colunas de estado vindas do processo do servidor.</summary>
        bool ServerStates,
        /// <summary>Pessoas vindas da organização (e não dos recursos do cronograma).</summary>
        bool ServerPeople,
        /// <summary>Controle de concorrência (Sync_version / conflito).</summary>
        bool ConcurrencyControl,
        /// <summary>Gravar a ordem do backlog no destino.</summary>
        bool BacklogOrderWrite,
        /// <summary>Criar Story/Task no destino.</summary>
        bool CreateItem,
        /// <summary>Precisa de cronograma aberto para funcionar.</summary>
        bool RequiresOpenSchedule,
        /// <summary>Precisa da pasta do projeto (artefatos e logs).</summary>
        bool RequiresProjectFolder)
    {
        /// <summary>As capacidades de um destino. É a única tabela: mexeu aqui, mexeu em tudo.</summary>
        public static NxBackendCapabilities For(NxBackendKind kind) => kind switch
        {
            // DevOps: tudo pelo servidor — é o destino completo de hoje.
            NxBackendKind.Default or NxBackendKind.AzureDevOps => new(
                Attachments: NxAttachmentStore.Server, Comments: true, BlockAudit: true,
                OpenInBrowser: true, ServerStates: true, ServerPeople: true,
                ConcurrencyControl: true, BacklogOrderWrite: true, CreateItem: true,
                RequiresOpenSchedule: false, RequiresProjectFolder: false),

            // GitHub Projects: sem anexo no item, sem auditoria por histórico e sem ordem
            // gravável — ver Plano_GitProject_Import_Sync.md.
            NxBackendKind.GitProject => new(
                Attachments: NxAttachmentStore.None, Comments: true, BlockAudit: false,
                OpenInBrowser: true, ServerStates: false, ServerPeople: true,
                ConcurrencyControl: false, BacklogOrderWrite: false, CreateItem: true,
                RequiresOpenSchedule: false, RequiresProjectFolder: false),

            // Local: anexo e auditoria de BLOCK existem, mas vêm da PASTA DO PROJETO; o resto é
            // do cronograma aberto. Nada de servidor.
            NxBackendKind.Local => new(
                Attachments: NxAttachmentStore.LocalFolder, Comments: false, BlockAudit: true,
                OpenInBrowser: false, ServerStates: false, ServerPeople: false,
                ConcurrencyControl: false, BacklogOrderWrite: true, CreateItem: true,
                RequiresOpenSchedule: true, RequiresProjectFolder: true),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "destino desconhecido")
        };

        /// <summary>Tem anexo, em qualquer lugar?</summary>
        public bool HasAttachments => Attachments != NxAttachmentStore.None;
    }
}
