using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IES_2.Avalonia.Services
{
    /// <summary>
    /// Where this program keeps its own files: per user, never next to the executable. The IAWSCAN_DATA
    /// environment variable moves them elsewhere (to a USB stick, say, for portable use).
    /// </summary>
    public static class AppPaths
    {
        public static string DataDirectory { get; } =
            Environment.GetEnvironmentVariable("IAWSCAN_DATA") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IAWScan3");

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
        public static string SessionsDirectory => Path.Combine(DataDirectory, "Sessions");
    }

    /// <summary>The version this build was published as, without the commit hash the SDK appends.</summary>
    public static class AppInfo
    {
        public static string Version { get; } =
            (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
                .Split('+')[0];
    }

    /// <summary>
    /// The user's choices, saved between runs as settings.json. Written atomically, so that a crash or a
    /// power cut mid-write can't lose them; an unreadable file just means starting from the defaults.
    /// </summary>
    public sealed class AppSettings
    {
        public const string LanguageEnglish = "en";
        public const string LanguageItalian = "it";
        public const string ThemeDark = "dark";
        public const string ThemeLight = "light";
        public const string ThemeSystem = "system";
        public const string TileSmall = "small";
        public const string TileNormal = "normal";
        public const string TileLarge = "large";

        private string? _path;

        /// <summary>Empty until the user chooses: follows the system language.</summary>
        public string Language { get; set; } = "";
        public string Theme { get; set; } = ThemeDark;

        /// <summary>Start recording data as soon as a real ECU is connected.</summary>
        public bool AutoRecord { get; set; } = true;

        /// <summary>The session log's size limit; beyond it, the oldest sessions are deleted.</summary>
        public int MaxLogMegabytes { get; set; } = 2048;

        public string? LastPort { get; set; }
        public string? LastEcuId { get; set; }
        public string? LastVehicle { get; set; }
        public string TileSize { get; set; } = TileNormal;

        /// <summary>Per ECU, the indices of the parameters being read, in dashboard order.</summary>
        public Dictionary<string, int[]> Selection { get; set; } = new();

        public static AppSettings Load(string path)
        {
            AppSettings? settings = null;
            try
            {
                if (File.Exists(path))
                    settings = JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJsonContext.Default.AppSettings);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Starting from the defaults is better than not starting.
            }
            settings ??= new AppSettings();
            settings._path = path;
            return settings;
        }

        public void Save()
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    JsonSerializer.Serialize(stream, this, AppSettingsJsonContext.Default.AppSettings);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: losing a preference is not worth interrupting a diagnosis.
            }
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppSettings))]
    internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
    {
    }
}
