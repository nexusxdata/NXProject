namespace NXProject.Models
{
    public class DevOpsProject
    {
        public string Name { get; set; } = "";
        public int RootWorkItemId { get; set; }

        public override string ToString() => Name;
    }
}
