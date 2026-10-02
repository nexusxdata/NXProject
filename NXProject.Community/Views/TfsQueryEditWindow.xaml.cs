// Copyright (c) Nexus XData Tecnologia Ltda — Todos os direitos reservados.
// NXProject — licenciado sob a NXProject License 2.0 (Open Core / licenciamento dual).
// Licença: LICENSE.txt (oficial, em português) | LICENSE.en.txt (English version).
// Distribuição comercial somente mediante contrato: comercial.nexus.xdata@gmail.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NXProject.Services;

namespace NXProject.Views
{
    /// <summary>
    /// Editor da query do DevOps dentro do NXProject: filtros em CLÁUSULAS (E/Ou, Campo, Operador,
    /// Valor), o WIQL gerado logo abaixo e, à direita, as colunas exibidas — o equivalente ao
    /// "Editor" e ao "Column options" da tela do Azure DevOps.
    ///
    /// Três escolhas de desenho, todas a favor de quem usa:
    ///
    ///  • **As cláusulas mandam, o WIQL acompanha.** Montar o filtro na grade é o caminho normal;
    ///    o texto fica visível para conferir e aprender, e só vira editável quando a pessoa pede.
    ///  • **Executar não grava.** O WIQL roda avulso e o resultado volta para a janela de queries.
    ///    Dá para experimentar à vontade sobre uma query compartilhada sem tocar nela.
    ///  • **Salvar é explícito e confirmado.** Criar e excluir query continuam fora do NX, de
    ///    propósito: isso se faz no DevOps, onde há pasta, permissão e histórico.
    ///
    /// Quando o WHERE tem algo que o leitor não garante reescrever igual (parênteses, por
    /// exemplo), a grade some e fica só o texto — ver <see cref="WiqlQueryModel.Editable"/>.
    /// </summary>
    public partial class TfsQueryEditWindow : Window
    {
        private readonly TfsConnectionOptions _options;
        private readonly string _queryId;
        private readonly List<(CheckBox Box, TfsImportService.DevOpsQueryColumn Column)> _columns = new();
        private readonly List<TfsImportService.DevOpsQueryColumn> _allFields = new();

        private WiqlQueryModel _model = new();
        /// <summary>Evita que redesenhar a grade dispare os eventos que regeram o WIQL.</summary>
        private bool _building;

        /// <summary>Resultado do último "Executar", para a janela de queries exibir na grade.</summary>
        public TfsImportService.DevOpsQueryRunResult? LastRun { get; private set; }

        public TfsQueryEditWindow(TfsConnectionOptions options, string queryId, string queryName)
        {
            InitializeComponent();
            _options = options;
            _queryId = queryId;
            QueryNameText.Text = queryName;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            StatusText.Text = AppStrings.Get("QueryEd_Loading");
            try
            {
                // A lista de campos vem primeiro: ela alimenta tanto o combo de campo das
                // cláusulas quanto as colunas exibidas.
                try { _allFields.AddRange(await TfsImportService.ListOrganizationFieldsAsync(_options)); }
                catch { /* sem a lista, os combos ficam com o que a query já traz */ }

                var def = await TfsImportService.LoadQueryWiqlAsync(_options, _queryId);
                _model = WiqlQueryModel.Parse(def.Wiql);
                WiqlBox.Text = def.Wiql;
                BuildClauseRows();

                var atuais = (await TfsImportService.RunSavedQueryAsync(_options, _queryId)).Columns;
                BuildColumnList(atuais);
                StatusText.Text = "";
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                MessageBox.Show(this, ex.Message, "NXProject", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Filtros em cláusulas ────────────────────────────────────────────────────────────

        /// <summary>Redesenha a grade de filtros a partir do modelo.</summary>
        private void BuildClauseRows()
        {
            _building = true;
            try
            {
                ClauseList.Items.Clear();
                var host = new StackPanel();

                // WHERE que o leitor não garante reescrever: some a grade, fica só o texto.
                if (!_model.Editable)
                {
                    TextOnlyPanel.Visibility = Visibility.Visible;
                    TextOnlyText.Text = AppStrings.Get("QueryEd_TextOnly", _model.NotEditableReason);
                    AddClauseBtn.IsEnabled = false;
                    EditWiqlCheck.IsChecked = true;
                    ApplyWiqlEditable(true);
                    ClauseList.Items.Add(host);
                    return;
                }

                TextOnlyPanel.Visibility = Visibility.Collapsed;
                AddClauseBtn.IsEnabled = true;
                for (var i = 0; i < _model.Clauses.Count; i++)
                    host.Children.Add(BuildClauseRow(_model.Clauses[i], i));
                ClauseList.Items.Add(host);
            }
            finally { _building = false; }
            RefreshWiqlFromClauses();
        }

        private UIElement BuildClauseRow(WiqlQueryModel.Clause clause, int index)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ✖ remove a linha
            var del = new Button
            {
                Content = "✖", FontSize = 11, Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(0, 0, 4, 0), Foreground = System.Windows.Media.Brushes.Firebrick,
                ToolTip = AppStrings.Get("QueryEd_RemoveClause")
            };
            del.Click += (_, _) => { _model.Clauses.Remove(clause); BuildClauseRows(); };
            Grid.SetColumn(del, 0); grid.Children.Add(del);

            // E/Ou — a primeira linha não tem conector
            var conn = new ComboBox { FontSize = 11, Margin = new Thickness(0, 0, 4, 0), Height = 22 };
            conn.Items.Add("And"); conn.Items.Add("Or");
            conn.SelectedItem = string.Equals(clause.Connector, "Or", StringComparison.OrdinalIgnoreCase) ? "Or" : "And";
            conn.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
            conn.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                clause.Connector = conn.SelectedItem as string ?? "And";
                RefreshWiqlFromClauses();
            };
            Grid.SetColumn(conn, 1); grid.Children.Add(conn);

            // Campo — editável, com a lista da organização para escolher
            var field = new ComboBox
            {
                IsEditable = true, FontSize = 11, Height = 22, Margin = new Thickness(0, 0, 4, 0),
                Text = clause.Field, ToolTip = AppStrings.Get("QueryEd_FieldTip")
            };
            foreach (var f in _allFields)
                field.Items.Add(new ComboBoxItem { Content = f.Name, Tag = f.ReferenceName, ToolTip = f.ReferenceName });
            field.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                if (field.SelectedItem is ComboBoxItem it && it.Tag is string rn)
                {
                    clause.Field = rn;
                    field.Text = it.Content?.ToString() ?? rn;
                }
                RefreshWiqlFromClauses();
            };
            field.LostFocus += (_, _) =>
            {
                if (_building) return;
                // Digitou direto: aceita nome de exibição (vira referência) ou a própria referência.
                var txt = (field.Text ?? "").Trim();
                var achado = _allFields.FirstOrDefault(f =>
                    string.Equals(f.Name, txt, StringComparison.CurrentCultureIgnoreCase)
                    || string.Equals(f.ReferenceName, txt, StringComparison.OrdinalIgnoreCase));
                clause.Field = achado?.ReferenceName ?? txt;
                RefreshWiqlFromClauses();
            };
            Grid.SetColumn(field, 2); grid.Children.Add(field);

            // Operador
            var op = new ComboBox { FontSize = 11, Height = 22, Margin = new Thickness(0, 0, 4, 0) };
            foreach (var o in WiqlQueryModel.Operators) op.Items.Add(o);
            op.SelectedItem = WiqlQueryModel.Operators.Contains(clause.Operator) ? clause.Operator : "=";
            Grid.SetColumn(op, 3); grid.Children.Add(op);

            // Valor — some nos operadores que não levam valor (Is Empty)
            var value = new TextBox { FontSize = 11, Height = 22, Text = clause.Value };
            value.TextChanged += (_, _) =>
            {
                if (_building) return;
                clause.Value = value.Text;
                RefreshWiqlFromClauses();
            };
            op.SelectionChanged += (_, _) =>
            {
                if (_building) return;
                clause.Operator = op.SelectedItem as string ?? "=";
                value.Visibility = WiqlQueryModel.OperatorHasNoValue(clause.Operator)
                    ? Visibility.Hidden : Visibility.Visible;
                RefreshWiqlFromClauses();
            };
            value.Visibility = WiqlQueryModel.OperatorHasNoValue(clause.Operator)
                ? Visibility.Hidden : Visibility.Visible;
            Grid.SetColumn(value, 4); grid.Children.Add(value);

            // Mostra o nome de exibição do campo quando ele é conhecido.
            var conhecido = _allFields.FirstOrDefault(f =>
                string.Equals(f.ReferenceName, clause.Field, StringComparison.OrdinalIgnoreCase));
            if (conhecido != null && !string.IsNullOrWhiteSpace(conhecido.Name)) field.Text = conhecido.Name;

            return grid;
        }

        private void OnAddClauseClick(object sender, RoutedEventArgs e)
        {
            _model.Clauses.Add(new WiqlQueryModel.Clause
            {
                Connector = _model.Clauses.Count == 0 ? "" : "And",
                Field = "System.WorkItemType",
                Operator = "=",
                Value = ""
            });
            BuildClauseRows();
        }

        /// <summary>Regera o WIQL a partir das cláusulas — a não ser que a pessoa esteja editando o texto.</summary>
        private void RefreshWiqlFromClauses()
        {
            if (_building || EditWiqlCheck.IsChecked == true || !_model.Editable) return;
            WiqlBox.Text = _model.Build();
        }

        /// <summary>
        /// Editar o texto à mão desliga a geração automática: as duas direções ao mesmo tempo
        /// apagariam o que a pessoa acabou de escrever. Ao desmarcar, o texto é relido de volta
        /// para cláusulas (e, se não der, a grade avisa).
        /// </summary>
        private void OnEditWiqlToggled(object sender, RoutedEventArgs e)
        {
            var manual = EditWiqlCheck.IsChecked == true;
            ApplyWiqlEditable(manual);
            if (manual) return;

            _model = WiqlQueryModel.Parse(WiqlBox.Text ?? "");
            BuildClauseRows();
        }

        private void ApplyWiqlEditable(bool manual)
        {
            WiqlBox.IsReadOnly = !manual;
            WiqlBox.Background = manual
                ? System.Windows.Media.Brushes.White
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF7, 0xF8, 0xFA));
        }

        // ── Colunas exibidas ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Monta a lista de colunas: as da query primeiro (marcadas, na ordem em que aparecem),
        /// depois todo o resto dos campos da organização, desmarcado. A ordem desta lista é a
        /// ordem da grade.
        /// </summary>
        private void BuildColumnList(IReadOnlyList<TfsImportService.DevOpsQueryColumn> atuais)
        {
            var host = new StackPanel();
            _columns.Clear();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(TfsImportService.DevOpsQueryColumn col, bool marcada)
            {
                if (!vistos.Add(col.ReferenceName)) return;
                var cb = new CheckBox
                {
                    Content = string.IsNullOrWhiteSpace(col.Name) ? col.ReferenceName : col.Name,
                    ToolTip = col.ReferenceName,
                    IsChecked = marcada,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 1, 0, 1)
                };
                host.Children.Add(cb);
                _columns.Add((cb, col));
            }

            foreach (var c in atuais) Add(c, true);
            foreach (var f in _allFields) Add(f, false);

            ColumnList.Items.Clear();
            ColumnList.Items.Add(host);
        }

        /// <summary>Esconde o que não casa com a busca — a lista de campos de uma organização é longa.</summary>
        private void OnColumnSearchChanged(object sender, TextChangedEventArgs e)
        {
            var q = (ColumnSearchBox.Text ?? "").Trim();
            foreach (var (box, col) in _columns)
            {
                var casa = q.Length == 0
                    || (box.Content?.ToString() ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || col.ReferenceName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                // Marcada continua visível mesmo fora da busca: senão some o que já foi escolhido.
                box.Visibility = casa || box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private List<TfsImportService.DevOpsQueryColumn> ChosenColumns() =>
            _columns.Where(x => x.Box.IsChecked == true).Select(x => x.Column).ToList();

        // ── Executar e salvar ───────────────────────────────────────────────────────────────

        private async void OnRunClick(object sender, RoutedEventArgs e)
        {
            var wiql = (WiqlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(wiql)) return;
            RunBtn.IsEnabled = false;
            StatusText.Text = AppStrings.Get("QueryEd_Running");
            try
            {
                LastRun = await TfsImportService.RunWiqlTextAsync(_options, wiql, ChosenColumns());
                StatusText.Text = AppStrings.Get("QueryEd_RunOk", LastRun.Rows.Count.ToString());
            }
            catch (Exception ex)
            {
                StatusText.Text = "";
                // A mensagem do DevOps nomeia o campo ou o ponto da sintaxe que não aceitou.
                MessageBox.Show(this, ex.Message, AppStrings.Get("QueryEd_RunError"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { RunBtn.IsEnabled = true; }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var wiql = (WiqlBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(wiql)) return;
            var ask = MessageBox.Show(this, AppStrings.Get("QueryEd_SaveConfirm", QueryNameText.Text),
                AppStrings.Get("QueryEd_Save"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ask != MessageBoxResult.OK) return;

            SaveBtn.IsEnabled = false;
            StatusText.Text = AppStrings.Get("QueryEd_Saving");
            try
            {
                var (ok, msg) = await TfsImportService.SaveQueryWiqlAsync(
                    _options, _queryId, wiql, ChosenColumns());
                StatusText.Text = ok ? AppStrings.Get("QueryEd_SaveOk") : "";
                if (!ok)
                    MessageBox.Show(this, msg, AppStrings.Get("QueryEd_Save"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SaveBtn.IsEnabled = true; }
        }
    }
}
