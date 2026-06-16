using System.Windows;
using NXProject.Models;

namespace NXProject.Views
{
    public partial class DevOpsProjectEditWindow : Window
    {
        public DevOpsProject? Result { get; private set; }

        public DevOpsProjectEditWindow(string name = "", int id = 0,
                                       bool isOpex = true, string costCenter = "")
        {
            InitializeComponent();
            NameBox.Text = name;
            IdBox.Text   = id > 0 ? id.ToString() : "";

            TypeBox.Items.Add("OPEX");
            TypeBox.Items.Add("CAPEX");
            TypeBox.SelectedIndex = isOpex ? 0 : 1;

            CcBox.Text = costCenter;

            Loaded += (_, _) => NameBox.Focus();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Informe o nome do projeto.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(IdBox.Text?.Trim(), out var id) || id <= 0)
            {
                MessageBox.Show("Informe um ID numérico válido (maior que zero).", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new DevOpsProject
            {
                Name           = name,
                RootWorkItemId = id,
                IsOpex         = TypeBox.SelectedIndex == 0,
                CostCenter     = CcBox.Text?.Trim() ?? ""
            };
            DialogResult = true;
            Close();
        }
    }
}
