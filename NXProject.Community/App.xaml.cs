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

            // Carrega idioma salvo; se vazio, detecta pelo Windows
            var saved = TfsConnectionStore.Load();
            var lang = string.IsNullOrWhiteSpace(saved.Language)
                ? LanguageService.DetectFromWindows()
                : saved.Language;

            LanguageService.Apply(lang);
        }
    }
}
