using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NXProject.Models
{
    public class GapJustification
    {
        public int ResourceId { get; set; }
        public DateTime GapStart { get; set; }
        public DateTime GapEnd { get; set; }
        public string Justification { get; set; } = string.Empty;
    }

    public class SprintSettingsProfile
    {
        public int SprintDurationDays { get; set; } = 14;
        public int FirstSprintNumber { get; set; } = 1;
        public string SprintNumberingMode { get; set; } = "Sequencial";
        public double LowDaysPerSfp { get; set; } = 1.0;
        public double MediumDaysPerSfp { get; set; } = 1.0;
        public double HighDaysPerSfp { get; set; } = 1.0;
    }

    public class Project
    {
        public string Name { get; set; } = "Novo Projeto";
        public string? Description { get; set; }
        public string? Author { get; set; }

        public DateTime StartDate { get; set; } = DateTime.Today;

        // Duração do sprint em dias úteis
        public int SprintDurationDays { get; set; } = 14;

        // Número da primeira sprint exibida no cronograma
        public int FirstSprintNumber { get; set; } = 1;

        // Modo de numeracao das sprints: Sequencial, Par ou Impar
        public string SprintNumberingMode { get; set; } = "Sequencial";

        // Último nível de zoom selecionado pelo usuário
        public string LastZoom { get; set; } = "Mês";

        // Vínculo com o Azure DevOps: nome e ID do work item raiz importado
        public string? DevOpsProjectName { get; set; }
        public int DevOpsRootWorkItemId { get; set; }

        // Quantos dias de duracao equivalem a 1 SFP em cada faixa
        public double LowDaysPerSfp { get; set; } = 1.0;
        public double MediumDaysPerSfp { get; set; } = 1.0;
        public double HighDaysPerSfp { get; set; } = 1.0;

        // Sprints reais lidas do DevOps (nome + janela), numeradas de 1 em diante.
        // Vazio em projetos sem vinculo TFS (cai na numeracao sintetica do Gantt).
        public ObservableCollection<Sprint> Sprints { get; set; } = new();

        // Tarefas raiz (hierarquia)
        public ObservableCollection<ProjectTask> Tasks { get; set; } = new();

        // Recursos do projeto
        public ObservableCollection<Resource> Resources { get; set; } = new();

        // Justificativas de gaps no cronograma de recursos
        public List<GapJustification> GapJustifications { get; set; } = new();

        // Caminho do arquivo salvo
        public string? FilePath { get; set; }

        public bool IsDirty { get; set; } = false;

        public SprintSettingsProfile GetSprintSettingsProfile()
        {
            return new SprintSettingsProfile
            {
                SprintDurationDays = SprintDurationDays,
                FirstSprintNumber = FirstSprintNumber,
                SprintNumberingMode = SprintNumberingMode,
                LowDaysPerSfp = LowDaysPerSfp,
                MediumDaysPerSfp = MediumDaysPerSfp,
                HighDaysPerSfp = HighDaysPerSfp
            };
        }

        public void ApplySprintSettingsProfile(SprintSettingsProfile? profile)
        {
            if (profile == null)
                return;

            SprintDurationDays = Math.Max(1, profile.SprintDurationDays);
            FirstSprintNumber = Math.Max(1, profile.FirstSprintNumber);
            SprintNumberingMode = string.IsNullOrWhiteSpace(profile.SprintNumberingMode)
                ? "Sequencial"
                : profile.SprintNumberingMode.Trim();
            LowDaysPerSfp = Math.Max(0, profile.LowDaysPerSfp);
            MediumDaysPerSfp = Math.Max(0, profile.MediumDaysPerSfp);
            HighDaysPerSfp = Math.Max(0, profile.HighDaysPerSfp);
        }
    }
}
