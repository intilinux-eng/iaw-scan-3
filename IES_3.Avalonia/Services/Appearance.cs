using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using IES_2.Avalonia.Localization;

namespace IES_2.Avalonia.Services
{
    /// <summary>Applies the language and theme chosen in the settings to the running application.</summary>
    public static class Appearance
    {
        public static void Apply(AppSettings settings)
        {
            ApplyTheme(settings.Theme);
            Loc.Apply(settings.Language);
        }

        public static void ApplyTheme(string theme)
        {
            if (Application.Current == null) return;
            Application.Current.RequestedThemeVariant = theme switch
            {
                AppSettings.ThemeLight => ThemeVariant.Light,
                AppSettings.ThemeSystem => ThemeVariant.Default,
                _ => ThemeVariant.Dark,
            };
        }
    }

    /// <summary>
    /// The UI's languages. Every text lives in Localization/Strings.*.axaml: views use them with
    /// {DynamicResource Key}, so switching language takes effect at once; view models go through T/F.
    /// The ECU engine's own texts (parameter and fault names) follow the same culture, but are read
    /// when an ECU connects: a session already open keeps the language it started with.
    /// </summary>
    public static class Loc
    {
        private static ResourceDictionary? _current;

        public static string Language { get; private set; } = AppSettings.LanguageEnglish;

        public static event Action? LanguageChanged;

        /// <summary>Italian on an Italian system, otherwise English.</summary>
        public static string SystemLanguage =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == AppSettings.LanguageItalian
                ? AppSettings.LanguageItalian
                : AppSettings.LanguageEnglish;

        /// <param name="language">"en", "it", or empty to follow the system.</param>
        public static void Apply(string language)
        {
            var effective = language == AppSettings.LanguageItalian || language == AppSettings.LanguageEnglish
                ? language
                : SystemLanguage;

            ResourceDictionary dictionary = effective == AppSettings.LanguageItalian ? new StringsItalian() : new StringsEnglish();
            var merged = Application.Current!.Resources.MergedDictionaries;
            if (_current != null) merged.Remove(_current);
            merged.Add(dictionary);
            _current = dictionary;
            Language = effective;

            var culture = CultureInfo.GetCultureInfo(effective == AppSettings.LanguageItalian ? "it-IT" : "en-GB");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            LanguageChanged?.Invoke();
        }

        public static string T(string key) =>
            _current != null && _current.TryGetValue(key, out var value) && value is string text ? text : key;

        public static string F(string key, params object?[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(key), args);
    }
}
