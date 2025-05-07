using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ToNRoundCounter.Utils
{
    public static class LanguageManager
    {
        private static Dictionary<string, string> translations = new Dictionary<string, string>();
        static LanguageManager()
        {
            LoadTranslations();
        }
        private static void LoadTranslations()
        {
            try
            {
                if (File.Exists("override_lang.json"))
                {
                    string jsonContent = File.ReadAllText("override_lang.json", Encoding.UTF8);
                    JObject json = JObject.Parse(jsonContent);
                    foreach (var property in json.Properties())
                    {
                        translations[property.Name] = property.Value.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // エラー発生時はデフォルト文字列を使用
            }
        }
        public static string Translate(string text)
        {
            if (translations.ContainsKey(text))
                return translations[text];
            return text;
        }
    }
}
