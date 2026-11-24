using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PcPanel.Core
{
    /// <summary>
    /// Translations. The JSON files in /i18n are shared with the Android app and embedded into the exe.
    /// Missing keys fall back to English.
    /// </summary>
    public static class Loc
    {
        public static readonly (string Code, string Name)[] Languages =
        {
            ("en", "English"), ("tr", "Türkçe"), ("de", "Deutsch"), ("fr", "Français"),
            ("es", "Español"), ("it", "Italiano"), ("pt", "Português"), ("ru", "Русский"),
            ("pl", "Polski"), ("az", "Azərbaycan"), ("ja", "日本語"), ("zh", "中文"),
        };

        public static string Lang { get; private set; } = "en";
        static Dictionary<string, string> _en = new Dictionary<string, string>();
        static Dictionary<string, string> _cur = new Dictionary<string, string>();

        static Dictionary<string, string> Read(string code)
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("i18n." + code + ".json");
                if (s == null) return new Dictionary<string, string>();
                using var r = new StreamReader(s);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(r.ReadToEnd()) ?? new Dictionary<string, string>();
            }
            catch (Exception ex) { Log.Write("i18n " + code + ": " + ex.Message); return new Dictionary<string, string>(); }
        }

        public static void Load(string code)
        {
            if (_en.Count == 0) _en = Read("en");
            if (!IsSupported(code)) code = "en";
            Lang = code;
            _cur = code == "en" ? _en : Read(code);
        }

        public static bool IsSupported(string code)
        {
            foreach (var l in Languages) if (l.Code == code) return true;
            return false;
        }

        public static string GuessSystemLanguage()
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return IsSupported(two) ? two : "en";
        }

        public static string T(string key)
        {
            if (_cur.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (_en.TryGetValue(key, out v)) return v;
            return key;
        }

        /// <summary>T(key) with {0}, {1}… replaced.</summary>
        public static string F(string key, params object[] args)
        {
            var s = T(key);
            for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.CurrentCulture));
            return s;
        }
    }
}
