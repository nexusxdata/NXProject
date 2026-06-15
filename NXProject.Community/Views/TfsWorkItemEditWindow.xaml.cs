using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NXProject.Services;
using NXProject.ViewModels;

namespace NXProject.Views
{
    public partial class TfsWorkItemEditWindow : Window
    {
        private readonly TaskViewModel _task;
        private string? _devOpsItemUrl;

        public TfsWorkItemEditWindow(TaskViewModel task)
        {
            InitializeComponent();
            _task = task ?? throw new ArgumentNullException(nameof(task));

            TaskNameText.Text = task.Name;
            IdBox.Text = task.TfsId?.ToString() ?? string.Empty;
            SelectByText(TypeBox, task.TfsType);
            SelectByText(StateBox, task.TfsState);

            TagsBox.Text = task.Tags ?? string.Empty;
            _suppressBlockToggle = true;
            BlockCheck.IsChecked = HasTag(TagsBox.Text, "Block");
            _suppressBlockToggle = false;

            LoadDevOpsExtras(task);
        }

        private void LoadDevOpsExtras(TaskViewModel task)
        {
            if (task.TfsId is > 0)
            {
                try
                {
                    var conn = TfsConnectionStore.Load();
                    if (!string.IsNullOrWhiteSpace(conn.OrganizationUrl) && !string.IsNullOrWhiteSpace(conn.TeamProject))
                    {
                        var org = conn.OrganizationUrl.TrimEnd('/');
                        var proj = Uri.EscapeDataString(conn.TeamProject.Trim());
                        _devOpsItemUrl = $"{org}/{proj}/_workitems/edit/{task.TfsId}";
                        OpenInDevOpsButton.Visibility = Visibility.Visible;
                        LoadOnlineTasksButton.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }

            var children = task.ChildrenViewModels
                .Where(c => c.TfsId is > 0)
                .Select(c => new ChildTaskRow(c.TfsId!.Value, c.Name, c.TfsState ?? ""))
                .ToList();

            if (children.Count > 0)
            {
                ChildTasksList.ItemsSource = children;
                ChildTasksPanel.Visibility = Visibility.Visible;
            }
        }

        private void OnOpenInDevOpsClick(object sender, RoutedEventArgs e)
        {
            if (_devOpsItemUrl is null) return;
            try { Process.Start(new ProcessStartInfo(_devOpsItemUrl) { UseShellExecute = true }); }
            catch { }
        }

        private async void OnLoadOnlineTasksClick(object sender, RoutedEventArgs e)
        {
            if (_task.TfsId is not > 0)
                return;

            try
            {
                SetOnlineTaskLoading(true, "Buscando Tasks online...");
                var options = TfsConnectionStore.Load("NXProject.Community");
                var rows = await TfsImportService.LoadOnlineChildTasksAsync(options, _task.TfsId.Value);
                SetOnlineTaskLoading(false);

                new TfsOnlineChildTasksWindow(_task.TfsId.Value, _task.Name, rows)
                {
                    Owner = this
                }.ShowDialog();
            }
            catch (Exception ex)
            {
                SetOnlineTaskLoading(false);
                StatusText.Text = $"Nao foi possivel ler Tasks online.\n{ex.Message}";
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void SetOnlineTaskLoading(bool isLoading, string? message = null)
        {
            LoadOnlineTasksButton.IsEnabled = !isLoading;
            OpenInDevOpsButton.IsEnabled = !isLoading;
            if (message != null)
            {
                StatusText.Text = message;
                StatusText.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Visibility = Visibility.Collapsed;
            }
        }

        private sealed record ChildTaskRow(int TfsId, string Name, string State)
        {
            public string TfsIdText => $"#{TfsId}";
        }

        private bool _suppressBlockToggle;

        private void OnBlockToggled(object sender, RoutedEventArgs e)
        {
            if (_suppressBlockToggle)
                return;

            var tags = SplitTags(TagsBox.Text);
            tags.RemoveAll(t => string.Equals(t, "Block", StringComparison.OrdinalIgnoreCase));
            if (BlockCheck.IsChecked == true)
                tags.Add("Block");
            TagsBox.Text = string.Join("; ", tags);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            StatusText.Visibility = Visibility.Collapsed;

            int? id = null;
            var idText = ExtractId(IdBox.Text);
            if (!string.IsNullOrEmpty(idText))
            {
                if (!int.TryParse(idText, out var parsed) || parsed < 0)
                {
                    StatusText.Text = "Informe um ID válido: número do DevOps, 0 para criar na sincronização, ou vazio para desvincular.";
                    StatusText.Visibility = Visibility.Visible;
                    return;
                }
                id = parsed;
                IdBox.Text = parsed.ToString(); // normaliza para só o número
            }

            // 0 = criar no DevOps: exige um tipo selecionado.
            if (id == 0 && StateBoxTypeMissing())
            {
                StatusText.Text = "Para criar (ID = 0), selecione o Tipo DevOps (Epic, Feature ou Story).";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            _task.TfsId = id;
            _task.TfsType = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            _task.TfsState = (StateBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            _task.Tags = string.Join("; ", SplitTags(TagsBox.Text));

            DialogResult = true;
            Close();
        }

        // Extrai o ID numérico de um texto que pode ser um número puro ou uma URL do DevOps.
        // Ex: "https://dev.azure.com/org/proj/_workitems/edit/12345" → "12345"
        private static string? ExtractId(string? text)
        {
            var raw = text?.Trim();
            if (string.IsNullOrEmpty(raw)) return raw;
            if (!raw.Contains('/') && !raw.Contains('?')) return raw; // já é número ou texto simples

            var match = System.Text.RegularExpressions.Regex.Match(raw, @"(?:^|[/?&=])(\d{3,7})(?:[/?&=]|$)");
            return match.Success ? match.Groups[1].Value : raw;
        }

        private static System.Collections.Generic.List<string> SplitTags(string? tags) =>
            string.IsNullOrWhiteSpace(tags)
                ? new System.Collections.Generic.List<string>()
                : tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static bool HasTag(string? tags, string tag) =>
            SplitTags(tags).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

        private bool StateBoxTypeMissing() =>
            (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() is not { Length: > 0 };

        private static void SelectByText(ComboBox combo, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                combo.SelectedIndex = -1;
                return;
            }

            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }

            // Tipo/estado não previsto: adiciona e seleciona.
            var added = new ComboBoxItem { Content = value };
            combo.Items.Add(added);
            combo.SelectedItem = added;
        }
    }
}
