using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IES_2.Coupe.Diagnostics
{
    /// <summary>
    /// The few choices worth remembering across power cycles of an in-car unit: which parameters
    /// each ECU shows on the dashboard, the last ECU/port used (what automatic logging connects to)
    /// and whether automatic logging is on. Stored as settings.json in the working directory,
    /// written atomically so a power cut mid-write can't corrupt it.
    /// </summary>
    public sealed class SettingsStore
    {
        private readonly string _path = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");
        private readonly object _gate = new();
        private Data _data;

        public SettingsStore()
        {
            _data = Load();
        }

        public string? LastEcu { get { lock (_gate) return _data.LastEcu; } }
        public string? LastPort { get { lock (_gate) return _data.LastPort; } }

        /// <summary>Connect to the ECU by itself at power-on and log everything (on unless turned off).</summary>
        public bool AutoLog
        {
            get { lock (_gate) return _data.AutoLog; }
            set
            {
                lock (_gate)
                {
                    _data.AutoLog = value;
                    Save();
                }
            }
        }

        public IReadOnlyCollection<int> GetSelection(string ecuId)
        {
            lock (_gate)
                return _data.Selected.TryGetValue(ecuId, out var indices) ? indices : Array.Empty<int>();
        }

        public void SetSelection(string ecuId, IEnumerable<int> indices)
        {
            lock (_gate)
            {
                _data.Selected[ecuId] = new List<int>(indices).ToArray();
                Save();
            }
        }

        public void SetLastConnection(string ecuId, string? port)
        {
            lock (_gate)
            {
                _data.LastEcu = ecuId;
                if (port != null) _data.LastPort = port;
                Save();
            }
        }

        private Data Load()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Unreadable settings just mean starting from defaults.
            }
            return new Data();
        }

        private void Save()
        {
            try
            {
                var tmp = _path + ".tmp";
                using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    JsonSerializer.Serialize(stream, _data);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort: losing a remembered selection is not worth failing the request over.
            }
        }

        private sealed class Data
        {
            public Dictionary<string, int[]> Selected { get; set; } = new();
            public string? LastEcu { get; set; }
            public string? LastPort { get; set; }
            public bool AutoLog { get; set; } = true;
        }
    }
}
