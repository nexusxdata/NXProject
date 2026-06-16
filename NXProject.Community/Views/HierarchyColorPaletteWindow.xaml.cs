using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXProject.Models;

namespace NXProject.Community.Views
{
    public partial class HierarchyColorPaletteWindow : Window
    {
        private readonly Project _project;
        private bool _loading;

        private static readonly List<string> Defaults =
        [
            "#D8D8D8", "#E3E3E3", "#EEEEEE", "#F5F5F5", "#FAFAFA"
        ];

        private TextBox[] ColorBoxes => [Color0Box, Color1Box, Color2Box, Color3Box, Color4Box];
        private Border[] Previews    => [Preview0,  Preview1,  Preview2,  Preview3,  Preview4];

        public HierarchyColorPaletteWindow(Project project)
        {
            InitializeComponent();
            _project = project;
            Load();
        }

        private void Load()
        {
            _loading = true;
            EnabledCheckBox.IsChecked = _project.UseHierarchyColors;
            var colors = _project.HierarchyLevelColors;
            for (int i = 0; i < ColorBoxes.Length; i++)
            {
                string hex = i < colors.Count ? colors[i] : (i < Defaults.Count ? Defaults[i] : "#FFFFFF");
                ColorBoxes[i].Text = hex;
                UpdatePreview(i, hex);
            }
            _loading = false;
            UpdateEnabled();
        }

        private void UpdatePreview(int idx, string hex)
        {
            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
                Previews[idx].Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            else
                Previews[idx].Background = Brushes.Transparent;
        }

        private void UpdateEnabled()
        {
            LevelsGrid.IsEnabled = EnabledCheckBox.IsChecked == true;
        }

        private void OnEnabledChanged(object sender, RoutedEventArgs e) => UpdateEnabled();

        private void OnColorChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            for (int i = 0; i < ColorBoxes.Length; i++)
                if (sender == ColorBoxes[i])
                    UpdatePreview(i, ColorBoxes[i].Text);
        }

        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            _loading = true;
            for (int i = 0; i < ColorBoxes.Length; i++)
            {
                ColorBoxes[i].Text = Defaults[i];
                UpdatePreview(i, Defaults[i]);
            }
            _loading = false;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            _project.UseHierarchyColors = EnabledCheckBox.IsChecked == true;
            var newColors = new List<string>();
            for (int i = 0; i < ColorBoxes.Length; i++)
            {
                var hex = ColorBoxes[i].Text.Trim();
                if (!hex.StartsWith('#')) hex = "#" + hex;
                newColors.Add(hex);
            }
            _project.HierarchyLevelColors = newColors;
            _project.IsDirty = true;
            DialogResult = true;
        }
    }
}
