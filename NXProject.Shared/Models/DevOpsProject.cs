namespace NXProject.Models
{
    public class DevOpsProject
    {
        public string Name             { get; set; } = "";
        public int    RootWorkItemId   { get; set; }
        public bool   IsOpex           { get; set; } = true;
        public string CostCenter       { get; set; } = "";
        // "CAPEX", "OPEX" ou "EPIC" (lê do campo Tipo_Centro_Custo de cada EPIC).
        // String vazia/null = derivado de IsOpex para compatibilidade.
        public string CostCenterSource { get; set; } = "";

        public override string ToString() => Name;
    }
}
