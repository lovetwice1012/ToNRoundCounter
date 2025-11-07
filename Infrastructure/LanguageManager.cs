using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;

namespace ToNRoundCounter.Infrastructure
{
    public static class LanguageManager
    {
        private static readonly ResourceManager _resourceManager =
            new ResourceManager("ToNRoundCounter.Infrastructure.Resources.Strings", typeof(LanguageManager).Assembly);

        private static readonly (string Code, string[] Aliases)[] _supportedCultures = new[]
        {
            ("ja", new[] { "ja", "ja-JP", "Japanese", "日本語" }),
            ("en", new[] { "en", "English", "英語" }),
            ("en-US", new[] { "en-US", "English (United States)", "English (US)", "英語, アメリカ", "英語 (アメリカ)" }),
            ("en-GB", new[] { "en-GB", "English (United Kingdom)", "English (UK)", "英語, イギリス", "英語 (イギリス)" }),
            ("ko", new[] { "ko", "ko-KR", "Korean", "한국어", "韓国語" }),
            ("zh-Hans", new[] { "zh", "zh-CN", "zh-SG", "zh-Hans", "Chinese (Simplified)", "中文(简体)", "中国語(簡体字)" }),
            ("da", new[] { "da", "da-DK", "Danish", "デンマーク語", "덴마크어", "丹麦语" }),
            ("de", new[] { "de", "de-DE", "German", "ドイツ語", "독일어", "德语" }),
            ("es", new[] { "es", "es-ES", "Spanish", "スペイン語", "스페인어", "西班牙语" }),
            ("es-419", new[] { "es-419", "es-MX", "es-AR", "es-CO", "es-CL", "es-PE", "es-LA", "Spanish (Latin America)", "スペイン語 (ラテンアメリカ)", "스페인어 (라틴 아메리카)", "西班牙语（拉丁美洲）" }),
            ("fr", new[] { "fr", "fr-FR", "French", "フランス語", "프랑스어", "法语" }),
            ("hr", new[] { "hr", "hr-HR", "Croatian", "クロアチア語", "크로아티ア어", "克罗地亚语" }),
            ("it", new[] { "it", "it-IT", "Italian", "イタリア語", "이탈리아어", "意大利语" }),
            ("lt", new[] { "lt", "lt-LT", "Lithuanian", "リトアニア語", "리투아니아어", "立陶宛语" }),
            ("hu", new[] { "hu", "hu-HU", "Hungarian", "ハンガリー語", "헝가리어", "匈牙利语" }),
            ("nl", new[] { "nl", "nl-NL", "Dutch", "オランダ語", "네덜란드어", "荷兰语" }),
            ("nb", new[] { "nb", "nb-NO", "no", "no-NO", "Norwegian", "ノルウェー語", "노르웨이어", "挪威语" }),
            ("pl", new[] { "pl", "pl-PL", "Polish", "ポーランド語", "폴란드어", "波兰语" }),
            ("pt-BR", new[] { "pt-BR", "pt", "pt-PT", "Portuguese (Brazil)", "ポルトガル語, ブラジル", "ポルトガル語 (ブラジル)", "포르투갈어 (브라질)", "葡萄牙语（巴西）" }),
            ("ro", new[] { "ro", "ro-RO", "Romanian", "ルーマニア語", "루마니아어", "罗马尼亚语" }),
            ("fi", new[] { "fi", "fi-FI", "Finnish", "フィンランド語", "핀란드어", "芬兰语" }),
            ("sv", new[] { "sv", "sv-SE", "Swedish", "スウェーデン語", "스웨덴어", "瑞典语" }),
            ("vi", new[] { "vi", "vi-VN", "Vietnamese", "ベトナム語", "베트남어", "越南语" }),
            ("tr", new[] { "tr", "tr-TR", "Turkish", "トルコ語", "터키어", "土耳其语" }),
            ("th", new[] { "th", "th-TH", "Thai", "タイ語", "태국어", "泰语" }),
            ("el", new[] { "el", "el-GR", "Greek", "ギリシャ語", "그리스어", "希腊语" }),
            ("bg", new[] { "bg", "bg-BG", "Bulgarian", "ブルガリア語", "불가리아어", "保加利亚语" }),
            ("ru", new[] { "ru", "ru-RU", "Russian", "ロシア語", "러시아어", "俄语" }),
            ("uk", new[] { "uk", "uk-UA", "Ukrainian", "ウクライナ語", "우크라이나어", "乌克兰语" }),
        };

        private static readonly string[] _supportedCultureCodes = _supportedCultures.Select(c => c.Code).ToArray();

        private static readonly Dictionary<string, string> _cultureAliases = CreateAliasDictionary();

        private static Dictionary<string, string> CreateAliasDictionary()
        {
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (code, values) in _supportedCultures)
            {
                if (!string.IsNullOrWhiteSpace(code))
                {
                    aliases[code] = code;
                }

                if (values == null)
                {
                    continue;
                }

                foreach (var alias in values)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    aliases[alias] = code;
                }
            }

            return aliases;
        }

        private static readonly string _defaultCulture = DetermineDefaultCulture();

        private static CultureInfo _culture = CultureInfo.GetCultureInfo(_defaultCulture);

        public static IReadOnlyList<string> SupportedCultureCodes { get; } = Array.AsReadOnly(_supportedCultureCodes);

        public static string DefaultCulture => _defaultCulture;

        public static void SetLanguage(string cultureCode)
        {
            var normalized = NormalizeCulture(cultureCode);
            var culture = CultureInfo.GetCultureInfo(normalized);
            Interlocked.Exchange(ref _culture, culture);
        }

        public static string Translate(string key)
        {
            var culture = Volatile.Read(ref _culture);
            return _resourceManager.GetString(key, culture) ?? key;
        }

        public static string NormalizeCulture(string? cultureCode)
        {
            return NormalizeCultureInternal(cultureCode) ?? _defaultCulture;
        }

        private static string DetermineDefaultCulture()
        {
            return NormalizeCultureInternal(CultureInfo.CurrentUICulture.Name) ?? "ja";
        }

        private static string? NormalizeCultureInternal(string? cultureCode)
        {
            if (string.IsNullOrWhiteSpace(cultureCode))
            {
                return null;
            }

            var candidate = cultureCode!.Trim();

            if (candidate.Length == 0)
            {
                return null;
            }

            if (_cultureAliases.TryGetValue(candidate!, out var alias))
            {
                return alias;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(candidate);

                if (_cultureAliases.TryGetValue(culture.Name, out var fromName))
                {
                    return fromName;
                }

                if (_cultureAliases.TryGetValue(culture.TwoLetterISOLanguageName, out var fromTwoLetter))
                {
                    return fromTwoLetter;
                }

                var exactMatch = _supportedCultureCodes.FirstOrDefault(code =>
                    string.Equals(code, culture.Name, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(exactMatch))
                {
                    return exactMatch;
                }

                var neutralMatch = _supportedCultureCodes.FirstOrDefault(code =>
                    string.Equals(code, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(neutralMatch))
                {
                    return neutralMatch;
                }

                if (string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
                {
                    return "zh-Hans";
                }
            }
            catch (CultureNotFoundException)
            {
                // Ignore and fall back to default.
            }

            return null;
        }
    }
}
