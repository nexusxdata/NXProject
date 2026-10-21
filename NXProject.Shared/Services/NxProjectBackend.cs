// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Linq;
using NXProject.Models;

namespace NXProject.Services
{
    /// <summary>
    /// Destino do cronograma aberto, lido SEMPRE do Portfólio (<see cref="PortfolioProjectConfig.BackendKind"/>).
    ///
    /// Existe porque o cronograma não diz de onde veio: importado do GitProject, ele carrega o
    /// número da issue no TfsId e o mesmo DevOpsRootWorkItemId de um projeto do DevOps. As telas
    /// que olhavam só para isso mandavam a issue #45 ao TFS — e excluir/sincronizar ali acerta um
    /// work item de outro projeto. O cadastro do Portfólio é o único lugar onde a escolha mora.
    /// </summary>
    public static class NxProjectBackend
    {
        /// <summary>
        /// Cadastro do projeto no Portfólio: pelo arquivo, depois pelo nome do projeto no
        /// Portfólio (gravado no import) e, por último, pelo nome do cronograma.
        /// </summary>
        public static PortfolioProjectConfig? ConfigOf(Project? project, TfsConnectionOptions options)
        {
            if (project == null || options?.PortfolioProjectConfigs == null) return null;
            var cfgs = options.PortfolioProjectConfigs;
            return cfgs.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.FilePath)
                                            && !string.IsNullOrWhiteSpace(project.FilePath)
                                            && string.Equals(c.FilePath, project.FilePath, StringComparison.OrdinalIgnoreCase))
                ?? cfgs.FirstOrDefault(c => !string.IsNullOrWhiteSpace(project.DevOpsProjectName)
                                            && string.Equals(c.ProjectName, project.DevOpsProjectName, StringComparison.CurrentCultureIgnoreCase))
                ?? cfgs.FirstOrDefault(c => string.Equals(c.ProjectName, project.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Destino do projeto. Sem cadastro, ou Default, é o padrão do NXProject (Azure DevOps).</summary>
        public static NxBackendKind Of(Project? project, TfsConnectionOptions options)
            => Normalize(ConfigOf(project, options)?.BackendKind ?? NxBackendKind.Default);

        /// <summary>O que o destino do projeto sabe fazer — é por aqui que a tela pergunta.</summary>
        public static NxBackendCapabilities CapabilitiesOf(Project? project, TfsConnectionOptions options)
            => NxBackendCapabilities.For(Of(project, options));

        public static NxBackendKind Normalize(NxBackendKind kind)
            => kind == NxBackendKind.Default ? NxBackendKind.AzureDevOps : kind;
    }
}
