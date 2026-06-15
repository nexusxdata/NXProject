using System;

namespace NXProject.Models
{
    public class Resource
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ResourceType Type { get; set; } = ResourceType.Work;

        // Capacidade em horas por dia (padrão 8h)
        public double MaxUnitsPerDay { get; set; } = 8.0;

        // Custo por hora (opcional)
        public decimal CostPerHour { get; set; } = 0;

        public string? Email { get; set; }
        public string? Notes { get; set; }

        public bool IsImportedFromTfs { get; set; }

        /// <summary>
        /// % do dia útil disponível para este projeto (0–100, padrão 100).
        /// Afeta o prazo das tarefas: 80 % → uma atividade de 40 h leva 40/(8×0,8)=6,25 dias.
        /// Diferente de AllocationPercent (por tarefa): este é o teto global da pessoa.
        /// </summary>
        public double AvailabilityPercent { get; set; } = 100.0;

        public string DisplayName => IsImportedFromTfs ? Name : $"*{Name}";

        public override string ToString() => Name;
    }

    public enum ResourceType
    {
        Work,       // Pessoa
        Material,   // Material/Equipamento
        Cost        // Custo fixo
    }

    public class TaskResource
    {
        public int ResourceId { get; set; }
        public Resource? Resource { get; set; }

        // % de alocação nesta tarefa (ex: 50 = 50%)
        public double AllocationPercent { get; set; } = 100.0;

        // Horas estimadas para esta tarefa (calculado ou manual)
        public double? EstimatedHours { get; set; }

        public override string ToString()
        {
            var name = Resource?.DisplayName ?? Resource?.Name ?? string.Empty;
            return Math.Abs(AllocationPercent - 100.0) < 0.01
                ? name
                : $"({AllocationPercent:0}%) {name}";
        }
    }
}
