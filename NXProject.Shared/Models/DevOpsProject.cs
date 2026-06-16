namespace NXProject.Models
{
    public class DevOpsProject
    {
        public string Name           { get; set; } = "";
        public int    RootWorkItemId { get; set; }
        public bool   IsOpex         { get; set; } = true;
        public string CostCenter     { get; set; } = "";

        public override string ToString() => Name;
    }
}
