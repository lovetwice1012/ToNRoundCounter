using System.Globalization;
using System.Resources;
using System.Threading;

namespace ToNRoundCounter.Infrastructure
{
    public static class LanguageManager
    {
        private static readonly ResourceManager _resourceManager =
            new ResourceManager("ToNRoundCounter.Infrastructure.Resources.Strings", typeof(LanguageManager).Assembly);

        private static CultureInfo _culture = CultureInfo.CurrentUICulture;

        public static void SetLanguage(string cultureCode)
        {
            Interlocked.Exchange(ref _culture, new CultureInfo(cultureCode));
        }

        public static string Translate(string key)
        {
            var culture = Volatile.Read(ref _culture);
            return _resourceManager.GetString(key, culture) ?? key;
        }
    }
}
