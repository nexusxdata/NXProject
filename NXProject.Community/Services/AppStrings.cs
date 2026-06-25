using System.Windows;

namespace NXProject.Services
{
    /// <summary>Gets localized strings from the active ResourceDictionary (Strings.*.xaml).</summary>
    public static class AppStrings
    {
        public static string Get(string key, params object[] args)
        {
            var val = Application.Current?.TryFindResource(key) as string ?? key;
            return args.Length > 0 ? string.Format(val, args) : val;
        }
    }
}
