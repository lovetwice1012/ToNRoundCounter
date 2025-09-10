using System.Globalization;
using System.Resources;

namespace ToNRoundCounter.Infrastructure
{
    public static class LanguageManager
    {
        private static readonly ResourceManager _resourceManager =
            new ResourceManager("ToNRoundCounter.Infrastructure.Resources.Strings", typeof(LanguageManager).Assembly);

        private static CultureInfo _culture = CultureInfo.CurrentUICulture;

        public static void SetLanguage(string cultureCode)
        {
            _culture = new CultureInfo(cultureCode);
        }

        public static string Translate(string key)
        {
            return _resourceManager.GetString(key, _culture) ?? key;
        }
    }
}
