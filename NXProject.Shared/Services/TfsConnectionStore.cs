using System;
using System.IO;
using System.Text.Json;

namespace NXProject.Services
{
    /// <summary>
    /// Dados de conexao com o Azure DevOps / TFS usados pelo import.
    /// </summary>
    public sealed class TfsConnectionOptions
    {
        /// <summary>URL da organizacao, ex.: https://dev.azure.com/sua-organizacao </summary>
        public string OrganizationUrl { get; set; } = "";

        /// <summary>Nome do team project, ex.: Seu Projeto </summary>
        public string TeamProject { get; set; } = "";

        /// <summary>Personal Access Token (Work Items - Read).</summary>
        public string PersonalAccessToken { get; set; } = string.Empty;

        /// <summary>ID do work item raiz (tipo Project) a ser importado.</summary>
        public int RootWorkItemId { get; set; }

        /// <summary>Horas que equivalem a 1 dia util ao converter esforco.</summary>
        public double HoursPerDay { get; set; } = 8.0;

        /// <summary>Nome do campo de esforco em horas (rotulo "HH Estimado").</summary>
        public string EffortFieldName { get; set; } = "HH Estimado";

        /// <summary>Nome do campo de data de inicio da Story.</summary>
        public string StartFieldName { get; set; } = "Data_Inicio";

        /// <summary>Nome do campo de data de fim da Story.</summary>
        public string FinishFieldName { get; set; } = "Data_Fim";

        /// <summary>Tag DevOps que marca data de início fixada/negociada (ex.: "DT-INI-NEG").</summary>
        public string FixedStartTagName { get; set; } = "DT-INI-NEG";

        /// <summary>Tag DevOps que marca data de fim fixada/comprometida (ex.: "DT_FIM_NEG").</summary>
        public string FixedFinishTagName { get; set; } = "DT_FIM_NEG";

        /// <summary>Sincroniza links de predecessora no DevOps durante Export → Sincronizar.</summary>
        public bool SyncPredecessorLinks { get; set; } = true;

        /// <summary>
        /// Janela de dias futuros para incluir sprints no dropdown mesmo sem work items.
        /// Padrão 90 dias. Use 0 para incluir apenas sprints com itens importados.
        /// </summary>
        public int FutureSprintDays { get; set; } = 90;

        /// <summary>Caminho do arquivo JSON da lista de projetos DevOps (compartilhável entre usuários).</summary>
        public string DevOpsProjectListPath { get; set; } = string.Empty;

        /// <summary>Código do idioma selecionado pelo usuário (ex: "pt-BR", "en-US"). Vazio = detectar do Windows.</summary>
        public string Language { get; set; } = string.Empty;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(OrganizationUrl) &&
            !string.IsNullOrWhiteSpace(TeamProject) &&
            !string.IsNullOrWhiteSpace(PersonalAccessToken) &&
            RootWorkItemId > 0;
    }

    /// <summary>
    /// Persiste a conexao do TFS (org, projeto, ultimo ID, horas/dia) e,
    /// opcionalmente, o PAT cifrado via DPAPI no escopo do usuario.
    /// </summary>
    public static class TfsConnectionStore
    {
        private sealed class StoredConnection
        {
            public string OrganizationUrl { get; set; } = "";
            public string TeamProject { get; set; } = "";
            public int RootWorkItemId { get; set; }
            public double HoursPerDay { get; set; } = 8.0;
            public string EffortFieldName { get; set; } = "HH Estimado";
            public string StartFieldName { get; set; } = "Data_Inicio";
            public string FinishFieldName { get; set; } = "Data_Fim";
            public string FixedStartTagName { get; set; } = "DT-INI-NEG";
            public string FixedFinishTagName { get; set; } = "DT_FIM_NEG";
            public bool SyncPredecessorLinks { get; set; } = true;
            public int FutureSprintDays { get; set; } = 90;
            public bool RememberToken { get; set; }
            public string EncryptedToken { get; set; } = string.Empty;
            public string DevOpsProjectListPath { get; set; } = string.Empty;
            public string Language { get; set; } = string.Empty;
        }

        public static TfsConnectionOptions Load(string storageKey = "NXProject.Community")
        {
            var file = GetSettingsFile(storageKey);
            var options = new TfsConnectionOptions
            {
                HoursPerDay = ProjectCalendarService.WorkingHoursPerDay
            };
            if (!File.Exists(file))
                return options;

            try
            {
                var stored = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(file));
                if (stored == null)
                    return options;

                options.OrganizationUrl = string.IsNullOrWhiteSpace(stored.OrganizationUrl)
                    ? options.OrganizationUrl
                    : stored.OrganizationUrl.Trim();
                options.TeamProject = string.IsNullOrWhiteSpace(stored.TeamProject)
                    ? options.TeamProject
                    : stored.TeamProject.Trim();
                options.RootWorkItemId = stored.RootWorkItemId;
                options.HoursPerDay = stored.HoursPerDay <= 0 ? 8.0 : stored.HoursPerDay;
                options.EffortFieldName = string.IsNullOrWhiteSpace(stored.EffortFieldName)
                    ? options.EffortFieldName : stored.EffortFieldName.Trim();
                options.StartFieldName = string.IsNullOrWhiteSpace(stored.StartFieldName)
                    ? options.StartFieldName : stored.StartFieldName.Trim();
                options.FinishFieldName = string.IsNullOrWhiteSpace(stored.FinishFieldName)
                    ? options.FinishFieldName : stored.FinishFieldName.Trim();
                options.FixedStartTagName = string.IsNullOrWhiteSpace(stored.FixedStartTagName)
                    ? options.FixedStartTagName : stored.FixedStartTagName.Trim();
                options.FixedFinishTagName = string.IsNullOrWhiteSpace(stored.FixedFinishTagName)
                    ? options.FixedFinishTagName : stored.FixedFinishTagName.Trim();
                options.SyncPredecessorLinks = stored.SyncPredecessorLinks;
                options.FutureSprintDays = stored.FutureSprintDays >= 0 ? stored.FutureSprintDays : 90;
                if (stored.RememberToken)
                    options.PersonalAccessToken = WindowsDataProtection.Decrypt(stored.EncryptedToken);
                options.DevOpsProjectListPath = stored.DevOpsProjectListPath ?? string.Empty;
                options.Language = stored.Language ?? string.Empty;
            }
            catch
            {
                return new TfsConnectionOptions();
            }

            return options;
        }

        public static void Save(TfsConnectionOptions options, bool rememberToken, string storageKey = "NXProject.Community")
        {
            var directory = GetSettingsDirectory(storageKey);
            Directory.CreateDirectory(directory);

            var payload = new StoredConnection
            {
                OrganizationUrl = options.OrganizationUrl?.Trim() ?? string.Empty,
                TeamProject = options.TeamProject?.Trim() ?? string.Empty,
                RootWorkItemId = options.RootWorkItemId,
                HoursPerDay = options.HoursPerDay <= 0 ? 8.0 : options.HoursPerDay,
                EffortFieldName = string.IsNullOrWhiteSpace(options.EffortFieldName) ? "HH Estimado" : options.EffortFieldName.Trim(),
                StartFieldName = string.IsNullOrWhiteSpace(options.StartFieldName) ? "Data_Inicio" : options.StartFieldName.Trim(),
                FinishFieldName = string.IsNullOrWhiteSpace(options.FinishFieldName) ? "Data_Fim" : options.FinishFieldName.Trim(),
                FixedStartTagName = string.IsNullOrWhiteSpace(options.FixedStartTagName) ? "DT-INI-NEG" : options.FixedStartTagName.Trim(),
                FixedFinishTagName = string.IsNullOrWhiteSpace(options.FixedFinishTagName) ? "DT_FIM_NEG" : options.FixedFinishTagName.Trim(),
                SyncPredecessorLinks = options.SyncPredecessorLinks,
                FutureSprintDays = options.FutureSprintDays,
                RememberToken = rememberToken,
                EncryptedToken = rememberToken
                    ? WindowsDataProtection.Encrypt(options.PersonalAccessToken ?? string.Empty, "NXProject.Tfs")
                    : string.Empty,
                DevOpsProjectListPath = options.DevOpsProjectListPath ?? string.Empty,
                Language = options.Language ?? string.Empty
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSettingsFile(storageKey), json);
        }

        private static string GetSettingsDirectory(string storageKey)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                string.IsNullOrWhiteSpace(storageKey) ? "NXProject.Community" : storageKey.Trim());
        }

        private static string GetSettingsFile(string storageKey)
        {
            return Path.Combine(GetSettingsDirectory(storageKey), "config_nxproject.json");
        }
    }
}
