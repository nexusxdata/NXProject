using System.Collections.Generic;
using System.Windows;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class ClassificationEditWindow : Window
    {
        public string? SelectedValue { get; private set; }

        // Valores padrão do picklist; substituídos pelos da configuração quando disponíveis.
        private static readonly List<string> DefaultValues =
            ["Architecture", "Burocracy", "Docs", "Feature", "Hotfix", "Refactor"];

        public ClassificationEditWindow(string currentValue, string tfsType)
        {
            InitializeComponent();

            LabelText.Text = $"Classificação para \"{tfsType}\" (campo obrigatório na criação no DevOps):";

            var options = TfsConnectionStore.Load("NXProject.Community");
            var items = options.ClassificationPicklistValues.Count > 0
                ? options.ClassificationPicklistValues
                : DefaultValues;

            foreach (var v in items)
                ValueCombo.Items.Add(v);

            ValueCombo.Text = !string.IsNullOrWhiteSpace(currentValue) ? currentValue : tfsType;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedValue = ValueCombo.Text?.Trim();
            DialogResult = true;
        }
    }
}
