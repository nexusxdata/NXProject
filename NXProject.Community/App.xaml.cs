using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using NXProject.Community.Services;
using NXProject.Services;

namespace NXProject
{
    public partial class CommunityApp : Application
    {
        public CommunityApp()
        {
            var culture = CultureInfo.CurrentCulture;

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            // Captura exceções não tratadas para exibir mensagem em vez de fechar silenciosamente
            DispatcherUnhandledException += (_, args) =>
            {
                args.Handled = true;
                MessageBox.Show(
                    $"Erro inesperado:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "Erro — NXProject",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var msg = args.ExceptionObject?.ToString() ?? "(sem detalhes)";
                MessageBox.Show($"Erro crítico:\n\n{msg}", "Erro — NXProject", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Carrega idioma salvo; se vazio, detecta pelo Windows
            var saved = TfsConnectionStore.Load();

            SprintAlertLog.Enabled = saved.DebugLogEnabled;

            var lang = string.IsNullOrWhiteSpace(saved.Language)
                ? LanguageService.DetectFromWindows()
                : saved.Language;

            LanguageService.Apply(lang);
        }
    }
}
