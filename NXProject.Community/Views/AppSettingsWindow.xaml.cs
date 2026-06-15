using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NXProject.Community.Services;
using NXProject.Services;

namespace NXProject.Views
{
    public partial class AppSettingsWindow : Window
    {
        private const int MaxLogoBytes = 5 * 1024 * 1024; // 5 MB
        private const int NormalizedWidth  = 300;
        private const int NormalizedHeight = 80;

        private string _logoBase64 = string.Empty;
        private string _selectedLanguage;

        public AppSettingsWindow()
        {
            InitializeComponent();
            _selectedLanguage = LanguageService.CurrentLanguage;
            LoadCurrentSettings();
        }

        // ── Inicialização ──────────────────────────────────────────────────

        private void LoadCurrentSettings()
        {
            var opts = TfsConnectionStore.Load();

            CompanyNameBox.Text = opts.CompanyName ?? string.Empty;
            _logoBase64         = opts.CompanyLogoBase64 ?? string.Empty;

            if (!string.IsNullOrEmpty(_logoBase64))
                ShowLogoPreview(_logoBase64);

            // Idioma
            foreach (ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == _selectedLanguage)
                {
                    LanguageCombo.SelectedItem = item;
                    break;
                }
            }
            if (LanguageCombo.SelectedItem == null)
                LanguageCombo.SelectedIndex = 0;
        }

        // ── Logo ──────────────────────────────────────────────────────────

        private void OnBrowseLogoClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Selecionar logo da empresa",
                Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp"
            };
            if (dlg.ShowDialog(this) != true) return;

            var info = new FileInfo(dlg.FileName);
            if (info.Length > MaxLogoBytes)
            {
                MessageBox.Show("O arquivo selecionado excede o limite de 5 MB.",
                    "Logo muito grande", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var normalized = NormalizeImage(dlg.FileName);
                _logoBase64 = Convert.ToBase64String(normalized);
                ShowLogoPreview(_logoBase64);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível processar a imagem:\n{ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClearLogoClick(object sender, RoutedEventArgs e)
        {
            _logoBase64 = string.Empty;
            LogoPreviewImage.Source    = null;
            LogoPreviewImage.Visibility = Visibility.Collapsed;
            LogoPlaceholder.Visibility  = Visibility.Visible;
            ClearLogoButton.IsEnabled   = false;
        }

        private void ShowLogoPreview(string base64)
        {
            try
            {
                var bytes  = Convert.FromBase64String(base64);
                var bitmap = LoadBitmapFromBytes(bytes);
                LogoPreviewImage.Source    = bitmap;
                LogoPreviewImage.Visibility = Visibility.Visible;
                LogoPlaceholder.Visibility  = Visibility.Collapsed;
                ClearLogoButton.IsEnabled   = true;
            }
            catch { /* preview opcional */ }
        }

        // ── Normalização da imagem ─────────────────────────────────────────
        // Redimensiona para caber em NormalizedWidth × NormalizedHeight mantendo proporção.
        // Sempre converte para PNG (fundo branco se não houver canal alpha).

        private static byte[] NormalizeImage(string filePath)
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource   = new Uri(filePath, UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            src.Freeze();

            // Calcula escala para caber no envelope mantendo proporção
            double scaleX = NormalizedWidth  / (double)src.PixelWidth;
            double scaleY = NormalizedHeight / (double)src.PixelHeight;
            double scale  = Math.Min(scaleX, scaleY);

            int dstW = Math.Max(1, (int)(src.PixelWidth  * scale));
            int dstH = Math.Max(1, (int)(src.PixelHeight * scale));

            var tb = new System.Windows.Media.Imaging.TransformedBitmap(
                src,
                new System.Windows.Media.ScaleTransform(scale, scale));

            var rtb = new RenderTargetBitmap(dstW, dstH, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);

            var dv = new System.Windows.Media.DrawingVisual();
            using (var ctx = dv.RenderOpen())
                ctx.DrawImage(tb, new Rect(0, 0, dstW, dstH));
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ── Idioma ────────────────────────────────────────────────────────

        private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem is ComboBoxItem item)
                _selectedLanguage = item.Tag?.ToString() ?? "pt-BR";
        }

        // ── Salvar / Cancelar ─────────────────────────────────────────────

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // Aplica idioma em tempo real
            LanguageService.Apply(_selectedLanguage);

            // Persiste tudo
            var opts = TfsConnectionStore.Load();
            opts.Language         = _selectedLanguage;
            opts.CompanyName      = CompanyNameBox.Text.Trim();
            opts.CompanyLogoBase64 = _logoBase64;

            var rememberToken = !string.IsNullOrEmpty(opts.PersonalAccessToken);
            TfsConnectionStore.Save(opts, rememberToken);

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
