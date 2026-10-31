using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Controls;
using IES_2.Avalonia.Services;
using IES_2.Session;
using Avalonia.Media;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>One line of the session log.</summary>
    public sealed record SessionItem(SessionSummary Summary, bool IsInProgress = false)
    {
        public string Id => Summary.Id;
        public string Vehicle => Summary.Header.Vehicle;
        public string EcuType => Summary.Header.EcuType;
        public bool IsSimulation => Summary.Header.Simulation;
        public bool HasData => Summary.HasData;
        public bool HasFaults => Summary.Faults.Count > 0;
        public string FaultsText => Loc.F("Sessions_FaultCount", Summary.Faults.Count);
        public string When => Summary.Header.StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Duration => IsInProgress ? Loc.T("Sessions_InProgress") : SessionsViewModel.FormatDuration(Summary.DurationSeconds);
        public string Size => SessionsViewModel.FormatSize(Summary.SizeBytes);
    }

    /// <summary>The log of past sessions: browse, open, compare, export for analysis, delete.</summary>
    public partial class SessionsViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private bool _stale = true;

        public ObservableCollection<SessionItem> Items { get; } = new();

        [ObservableProperty]
        private SessionDetailViewModel? detail;

        [ObservableProperty]
        private SessionItem? selectedItem;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string? message;

        [ObservableProperty]
        private string usedSpace = "";

        public bool IsEmpty => Items.Count == 0;

        public SessionsViewModel(ShellViewModel shell)
        {
            _shell = shell;
        }

        public void Activate()
        {
            if (_stale || _shell.IsConnected) _ = RefreshAsync();
        }

        public void MarkStale() => _stale = true;

        public void RefreshTexts()
        {
            // Dates, sizes and the like are worded when the list is built.
            _ = RefreshAsync();
            Detail?.RefreshTexts();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            _stale = false;
            IsLoading = true;
            var store = _shell.Store;
            var (summaries, total) = await Task.Run(() => (store.List(), store.TotalBytes()));
            var keep = SelectedItem?.Id;
            Items.Clear();
            var liveId = _shell.Live?.Recorder?.Id;
            foreach (var summary in summaries) Items.Add(new SessionItem(summary, summary.Id == liveId));
            SelectedItem = Items.FirstOrDefault(i => i.Id == keep);
            UsedSpace = Loc.F("Sessions_Used", FormatSize(total), FormatSize(_shell.Settings.MaxLogMegabytes * 1024L * 1024L));
            OnPropertyChanged(nameof(IsEmpty));
            IsLoading = false;
        }

        [RelayCommand]
        private async Task Open(SessionItem? item)
        {
            if (item == null) return;
            IsLoading = true;
            Message = null;
            try
            {
                var store = _shell.Store;
                var loaded = await Task.Run(() => store.Load(item.Id));
                Detail = new SessionDetailViewModel(_shell, this, item, loaded, Items.Where(i => i.Id != item.Id && i.HasData).ToList());
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException)
            {
                Message = Loc.F("Sessions_OpenFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Recordings from earlier versions, or any data.csv from another machine.</summary>
        [RelayCommand]
        private async Task ImportCsv()
        {
            var files = _shell.Files;
            if (files == null) return;
            var path = await files.PickCsvAsync();
            if (path == null) return;

            IsLoading = true;
            Message = null;
            try
            {
                var loaded = await Task.Run(() => SessionStore.LoadCsv(path));
                Detail = new SessionDetailViewModel(_shell, this, null, loaded, Items.Where(i => i.HasData).ToList());
            }
            catch (FormatException)
            {
                Message = Loc.T("Sessions_ImportNotRecognised");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Message = Loc.F("Sessions_OpenFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task Export(SessionItem? item)
        {
            if (item != null) await ExportAsync(new[] { item.Id }, $"iaw-scan-{item.Id}.zip");
        }

        [RelayCommand]
        private async Task ExportAll()
        {
            if (Items.Count > 0) await ExportAsync(Items.Select(i => i.Id).ToList(), "iaw-scan-sessions.zip");
        }

        public async Task ExportAsync(IReadOnlyList<string> ids, string fileName)
        {
            var files = _shell.Files;
            if (files == null) return;
            var target = await files.PickSaveAsync(fileName);
            if (target == null) return;

            IsLoading = true;
            Message = null;
            try
            {
                var store = _shell.Store;
                var language = Loc.Language;
                await Task.Run(() =>
                {
                    using (target) store.ExportZip(target, ids, language);
                });
                Message = Loc.T("Sessions_Exported");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Message = Loc.F("Sessions_ExportFailed", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task Delete(SessionItem? item)
        {
            if (item == null) return;
            if (_shell.Live?.Recorder?.Id == item.Id)
            {
                Message = Loc.T("Sessions_InUse");
                return;
            }
            var confirmed = await _shell.Dialog.ConfirmAsync(
                Loc.T("Sessions_DeleteTitle"), Loc.F("Sessions_DeleteBody", item.Vehicle, item.When), Loc.T("Sessions_DeleteConfirm"), dangerous: true);
            if (!confirmed) return;

            try
            {
                _shell.Store.Delete(item.Id);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Message = Loc.F("Sessions_DeleteFailed", ex.Message);
                return;
            }
            if (Detail?.Item?.Id == item.Id) Detail = null;
            await RefreshAsync();
        }

        [RelayCommand]
        private void OpenFolder()
        {
            System.IO.Directory.CreateDirectory(_shell.Store.Root);
            _shell.Files?.OpenFolder(_shell.Store.Root);
        }

        public static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalHours >= 1) return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min");
            if (span.TotalMinutes >= 1) return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes} min {span.Seconds} s");
            return string.Create(CultureInfo.InvariantCulture, $"{span.Seconds} s");
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return string.Create(CultureInfo.CurrentCulture, $"{bytes} B");
            if (bytes < 1024 * 1024) return string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} kB");
            if (bytes < 1024L * 1024 * 1024) return string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0 / 1024:0.#} MB");
            return string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0 / 1024 / 1024:0.#} GB");
        }
    }

    /// <summary>One session opened for review: chart, figures per parameter, what happened, and comparing with another.</summary>
    public partial class SessionDetailViewModel : ObservableObject
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x4F, 0x8C, 0xFF), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x22, 0xC5, 0x5E),
            Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x06, 0xB6, 0xD4),
            Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x84, 0xCC, 0x16),
        };

        private readonly ShellViewModel _shell;
        private readonly SessionsViewModel _sessions;
        private readonly LoadedSession _loaded;
        private readonly List<SeriesChoice> _allSeries = new();

        public SessionItem? Item { get; }
        public IReadOnlyList<SessionItem> CompareChoices { get; }
        public ObservableCollection<SeriesChoice> Series { get; } = new();
        public ObservableCollection<SessionEventRow> Events { get; } = new();

        public string Title => _loaded.Title;
        public string Subtitle { get; private set; } = "";
        public bool HasData => _allSeries.Count > 0;
        public bool HasEvents => Events.Count > 0;
        public bool CanExport => Item != null;
        public double Duration => _loaded.DurationSeconds;

        [ObservableProperty]
        private IReadOnlyList<ChartTrace> traces = Array.Empty<ChartTrace>();

        [ObservableProperty]
        private bool isOverlay;

        [ObservableProperty]
        private bool follow;

        [ObservableProperty]
        private double windowSeconds;

        [ObservableProperty]
        private double viewEnd;

        [ObservableProperty]
        private SessionItem? compareWith;

        [ObservableProperty]
        private string? compareMessage;

        private LoadedSession? _compare;

        public SessionDetailViewModel(ShellViewModel shell, SessionsViewModel sessions, SessionItem? item, LoadedSession loaded, IReadOnlyList<SessionItem> compareChoices)
        {
            _shell = shell;
            _sessions = sessions;
            _loaded = loaded;
            Item = item;
            CompareChoices = compareChoices;

            var i = 0;
            foreach (var series in loaded.Series)
            {
                var stats = series.Data.Stats(double.NegativeInfinity, double.PositiveInfinity);
                _allSeries.Add(new SeriesChoice(series, Palette[i++ % Palette.Length], stats, RebuildTraces));
            }
            foreach (var choice in _allSeries) Series.Add(choice);

            // A sensible first view: not all 26 parameters squeezed together.
            foreach (var choice in _allSeries.Skip(6)) choice.SetPlottedQuietly(false);

            FitAll();
            RebuildTraces();
            RefreshTexts();
        }

        public void RefreshTexts()
        {
            var parts = new List<string>();
            if (Item != null)
            {
                parts.Add(Item.When);
                parts.Add(Item.EcuType);
                if (Item.Summary.Header.IsoCode != "-") parts.Add("ISO " + Item.Summary.Header.IsoCode);
            }
            parts.Add(SessionsViewModel.FormatDuration(Duration));
            Subtitle = string.Join("  ·  ", parts);
            OnPropertyChanged(nameof(Subtitle));

            Events.Clear();
            foreach (var e in _loaded.Events)
                if (SessionEventRow.From(e) is { } row) Events.Add(row);
            OnPropertyChanged(nameof(HasEvents));
        }

        private void RebuildTraces()
        {
            var compare = _compare?.Series.ToDictionary(s => s.Name);
            Traces = _allSeries
                .Where(s => s.IsPlotted)
                .Select(s => new ChartTrace(s.Name, s.Unit, s.Color, s.Series.Data,
                    compare != null && compare.TryGetValue(s.Name, out var other) ? other.Data : null))
                .ToList();
        }

        [RelayCommand]
        private void FitAll()
        {
            Follow = false;
            var end = Math.Max(_loaded.DurationSeconds, _compare?.DurationSeconds ?? 0);
            var start = Math.Min(FirstValueTime(_loaded), _compare != null ? FirstValueTime(_compare) : end);
            WindowSeconds = Math.Max(2, end - start);
            ViewEnd = start + WindowSeconds;
        }

        /// <summary>Recording may have started long after connecting: no point in showing that stretch.</summary>
        private static double FirstValueTime(LoadedSession session) =>
            session.Series.Select(s => s.Data.FirstTime).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty(0).Min();

        /// <summary>Centres the chart on the moment an event happened.</summary>
        [RelayCommand]
        private void JumpTo(SessionEventRow? row)
        {
            if (row == null) return;
            Follow = false;
            var window = Math.Min(Math.Max(2, _loaded.DurationSeconds), Math.Max(30, WindowSeconds / 4));
            WindowSeconds = window;
            ViewEnd = Math.Max(window, row.T + window / 2);
        }

        partial void OnCompareWithChanged(SessionItem? value) => _ = LoadCompareAsync(value);

        private async Task LoadCompareAsync(SessionItem? item)
        {
            CompareMessage = null;
            if (item == null)
            {
                _compare = null;
            }
            else
            {
                try
                {
                    var store = _shell.Store;
                    _compare = await Task.Run(() => store.Load(item.Id));
                    var names = _compare.Series.Select(s => s.Name).ToHashSet();
                    if (!_allSeries.Any(s => names.Contains(s.Name)))
                        CompareMessage = Loc.T("Sessions_CompareNothingInCommon");
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException)
                {
                    _compare = null;
                    CompareMessage = Loc.F("Sessions_OpenFailed", ex.Message);
                }
            }
            RebuildTraces();
            FitAll();
        }

        [RelayCommand]
        private void ClearCompare() => CompareWith = null;

        [RelayCommand]
        private void Close() => _sessions.Detail = null;

        [RelayCommand]
        private async Task Export()
        {
            if (Item != null) await _sessions.ExportAsync(new[] { Item.Id }, $"iaw-scan-{Item.Id}.zip");
        }
    }

    /// <summary>A recorded parameter: on the chart or not, with its figures over the session.</summary>
    public sealed partial class SeriesChoice : ObservableObject
    {
        private readonly Action _changed;
        private bool _quiet;

        public LoadedSeries Series { get; }
        public Color Color { get; }
        public IBrush Brush { get; }
        public string Name => Series.Name;
        public string Unit => Series.Unit;
        public string MinText { get; }
        public string AverageText { get; }
        public string MaxText { get; }

        [ObservableProperty]
        private bool isPlotted = true;

        public SeriesChoice(LoadedSeries series, Color color, SeriesStats stats, Action changed)
        {
            Series = series;
            Color = color;
            Brush = new SolidColorBrush(color);
            _changed = changed;
            MinText = stats.Count == 0 ? "–" : LaneChart.FormatValue(stats.Min);
            AverageText = stats.Count == 0 ? "–" : LaneChart.FormatValue(stats.Average);
            MaxText = stats.Count == 0 ? "–" : LaneChart.FormatValue(stats.Max);
        }

        public void SetPlottedQuietly(bool value)
        {
            _quiet = true;
            IsPlotted = value;
            _quiet = false;
        }

        partial void OnIsPlottedChanged(bool value)
        {
            if (!_quiet) _changed();
        }
    }

    /// <summary>A session event, in words of the current language.</summary>
    public sealed record SessionEventRow(double T, string Time, string Text, bool IsProblem)
    {
        public static SessionEventRow? From(SessionEvent e)
        {
            var (text, problem) = Describe(e);
            return text == null ? null : new SessionEventRow(e.T, LaneChart.FormatTime(e.T, showTenths: false), text, problem);
        }

        private static (string? Text, bool IsProblem) Describe(SessionEvent e)
        {
            switch (e.Type)
            {
                case SessionEventTypes.Connected:
                    return (Loc.T("Event_Connected"), false);
                case SessionEventTypes.Disconnected:
                    return (Loc.T("Event_Disconnected"), false);
                case SessionEventTypes.CommLost:
                    return (Loc.T("Event_CommLost"), true);
                case SessionEventTypes.CommRestored:
                    return (Loc.T("Event_CommRestored"), false);
                case SessionEventTypes.FaultsRead:
                {
                    var count = (e.Data?["faults"] as JsonArray)?.Count ?? 0;
                    return (count == 0 ? Loc.T("Event_FaultsNone") : Loc.F("Event_FaultsFound", count), count > 0);
                }
                case SessionEventTypes.FaultsChanged:
                {
                    var added = Names(e.Data?["added"]);
                    var removed = Names(e.Data?["removed"]);
                    var parts = new List<string>();
                    if (added.Length > 0) parts.Add(Loc.F("Event_FaultsAdded", added));
                    if (removed.Length > 0) parts.Add(Loc.F("Event_FaultsRemoved", removed));
                    return (parts.Count == 0 ? Loc.T("Event_FaultsChanged") : string.Join("  ", parts), added.Length > 0);
                }
                case SessionEventTypes.ClearCodes:
                    return (Loc.F("Event_ClearCodes", OutcomeText(e)), false);
                case SessionEventTypes.ActuatorTest:
                    return (Loc.F("Event_Test", e.Data?["test"]?.GetValue<string>(), OutcomeText(e)), false);
                case SessionEventTypes.Adjustment:
                    return (Loc.F("Event_Adjustment", e.Data?["adjustment"]?.GetValue<string>(), OutcomeText(e)), false);
                case SessionEventTypes.DataStarted:
                    return (Loc.T("Event_DataStarted"), false);
                case SessionEventTypes.DataStopped:
                    return (Loc.T("Event_DataStopped"), false);
                default:
                    return (null, false);
            }
        }

        private static string Names(JsonNode? list) =>
            list is JsonArray array
                ? string.Join(", ", array.Select(f => f?["description"]?.GetValue<string>()).Where(n => n != null))
                : "";

        private static string OutcomeText(SessionEvent e) => e.Data?["outcome"]?.GetValue<string>() switch
        {
            "Demo" => Loc.T("Outcome_Demo"),
            "Passed" => Loc.T("Outcome_Passed"),
            "Timeout" => Loc.T("Outcome_Timeout"),
            "InitFailed" => Loc.T("Outcome_InitFailed"),
            "Cancelled" => Loc.T("Outcome_Cancelled"),
            _ => Loc.T("Outcome_Failed"),
        };
    }
}
