using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.Avalonia.Controls;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.Session;

namespace IES_2.Avalonia.ViewModels
{
    /// <summary>The chosen parameters as gauges, in an order and at a size the user decides.</summary>
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

        public LiveSession Live { get; }
        public ShellViewModel Shell { get; }
        public ObservableCollection<ParameterRow> Tiles => Live.Selected;

        [ObservableProperty]
        private bool isEditing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSmall), nameof(IsNormal), nameof(IsLarge))]
        private string tileSize = AppSettings.TileNormal;

        public bool IsSmall => TileSize == AppSettings.TileSmall;
        public bool IsNormal => TileSize == AppSettings.TileNormal;
        public bool IsLarge => TileSize == AppSettings.TileLarge;
        public bool IsEmpty => Live.Selected.Count == 0;

        public DashboardViewModel(LiveSession live, AppSettings settings, ShellViewModel shell)
        {
            Live = live;
            Shell = shell;
            _settings = settings;
            TileSize = settings.TileSize;
            live.Selected.CollectionChanged += OnSelectedChanged;
        }

        private void OnSelectedChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
            OnPropertyChanged(nameof(IsEmpty));

        public void Detach() => Live.Selected.CollectionChanged -= OnSelectedChanged;

        [RelayCommand]
        private void SetSize(string size)
        {
            TileSize = size;
            _settings.TileSize = size;
            _settings.Save();
        }

        [RelayCommand]
        private void MoveBack(ParameterRow row) => Move(row, -1);

        [RelayCommand]
        private void MoveForward(ParameterRow row) => Move(row, 1);

        private void Move(ParameterRow row, int direction)
        {
            var index = Live.Selected.IndexOf(row);
            var target = index + direction;
            if (index < 0 || target < 0 || target >= Live.Selected.Count) return;
            Live.Selected.Move(index, target);
            Live.SaveSelection();
        }

        [RelayCommand]
        private void Remove(ParameterRow row) => row.IsChecked = false;

        [RelayCommand]
        private void ResetExtremes()
        {
            foreach (var row in Live.Selected) row.ResetExtremes();
        }
    }

    /// <summary>Every parameter of the ECU: choose which ones to read.</summary>
    public partial class ParametersViewModel : ObservableObject
    {
        public LiveSession Live { get; }
        public ObservableCollection<ParameterRow> Visible { get; } = new();

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private bool selectedOnly;

        public ParametersViewModel(LiveSession live)
        {
            Live = live;
            RefreshVisible();
        }

        partial void OnSearchTextChanged(string value) => RefreshVisible();
        partial void OnSelectedOnlyChanged(bool value) => RefreshVisible();

        private void RefreshVisible()
        {
            var needle = SearchText.Trim();
            Visible.Clear();
            foreach (var row in Live.Rows)
            {
                if (SelectedOnly && !row.IsChecked) continue;
                if (needle.Length > 0 && !row.Description.Contains(needle, StringComparison.CurrentCultureIgnoreCase)) continue;
                Visible.Add(row);
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var row in Visible) row.IsChecked = true;
        }

        [RelayCommand]
        private void SelectNone()
        {
            foreach (var row in Live.Rows) row.IsChecked = false;
            RefreshVisible();
        }
    }

    /// <summary>Fault codes present now, what changed during the session, and clearing them.</summary>
    public partial class FaultsViewModel : ObservableObject
    {
        private readonly DialogViewModel _dialog;
        private ActiveOutcome? _clearOutcome;

        public LiveSession Live { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ClearCodesCommand))]
        private bool isClearing;

        [ObservableProperty]
        private string? statusMessage;

        /// <summary>Read, and nothing to report.</summary>
        public bool IsClean => Live.FaultsRead && Live.Faults.Count == 0;

        public FaultsViewModel(LiveSession live, DialogViewModel dialog)
        {
            Live = live;
            _dialog = dialog;
            live.Faults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsClean));
            live.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LiveSession.FaultsRead)) OnPropertyChanged(nameof(IsClean));
            };
        }

        private bool CanClear() => !IsClearing && !Live.IsSimulation;

        /// <summary>Clearing can't be undone, so it always asks first; there's no keyboard shortcut for it.</summary>
        [RelayCommand(CanExecute = nameof(CanClear))]
        private async Task ClearCodes()
        {
            var confirmed = await _dialog.ConfirmAsync(
                Loc.T("Faults_ClearTitle"), Loc.T("Faults_ClearBody"), Loc.T("Faults_ClearConfirm"), dangerous: true);
            if (!confirmed) return;

            IsClearing = true;
            StatusMessage = null;
            ActiveOutcome outcome;
            try
            {
                outcome = await Live.Session.ClearCodesAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = ActiveOutcome.Failed;
            }
            _clearOutcome = outcome;
            StatusMessage = DescribeClear(outcome);
            Live.Recorder?.Event(SessionEventTypes.ClearCodes, new JsonObject { ["outcome"] = outcome.ToString() });
            IsClearing = false;
        }

        private static string DescribeClear(ActiveOutcome outcome) => outcome switch
        {
            ActiveOutcome.Passed => Loc.T("Faults_Cleared"),
            ActiveOutcome.Cancelled => Loc.T("Outcome_Cancelled"),
            ActiveOutcome.InitFailed => Loc.T("Outcome_InitFailed"),
            _ => Loc.T("Faults_ClearFailed"),
        };

        public void RefreshTexts()
        {
            if (_clearOutcome is { } outcome) StatusMessage = DescribeClear(outcome);
        }
    }

    /// <summary>Actuator tests. They move real components, so each one asks first (except in simulation).</summary>
    public partial class TestsViewModel : ObservableObject
    {
        private readonly DialogViewModel _dialog;
        private CancellationTokenSource? _cts;

        public LiveSession Live { get; }
        public ObservableCollection<TestRow> Rows { get; } = new();
        public bool IsEmpty => Rows.Count == 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(CancelCommand))]
        private bool isBusy;

        public TestsViewModel(LiveSession live, DialogViewModel dialog)
        {
            Live = live;
            _dialog = dialog;
            foreach (var test in live.Session.Ecu.activeTest) Rows.Add(new TestRow(test));
        }

        private bool CanRun(TestRow? row) => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task Run(TestRow? row)
        {
            if (row == null) return;
            if (!Live.IsSimulation)
            {
                var body = Loc.F(row.EngineRunning ? "Tests_ConfirmBodyRunning" : "Tests_ConfirmBodyOff", row.MaxSeconds);
                if (!await _dialog.ConfirmAsync(row.Description, body, Loc.T("Tests_Run"))) return;
            }

            IsBusy = true;
            row.IsRunning = true;
            row.Outcome = null;
            _cts = new CancellationTokenSource();
            ActiveOutcome outcome;
            try
            {
                outcome = await Live.Session.RunTestAsync(row.Test, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                outcome = ActiveOutcome.Cancelled;
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                outcome = ActiveOutcome.Failed;
            }
            row.Outcome = outcome;
            row.IsRunning = false;
            Live.Recorder?.Event(SessionEventTypes.ActuatorTest, new JsonObject { ["test"] = row.Description, ["outcome"] = outcome.ToString() });
            _cts = null;
            IsBusy = false;
        }

        private bool CanCancel() => IsBusy;

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel() => _cts?.Cancel();

        public void RefreshTexts()
        {
            foreach (var row in Rows) row.RefreshText();
        }
    }

    /// <summary>ECU adjustments, including the byte-value kind that asks for a number along the way.</summary>
    public partial class AdjustmentsViewModel : ObservableObject
    {
        private readonly DialogViewModel _dialog;

        public LiveSession Live { get; }
        public ObservableCollection<AdjustRow> Rows { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool isBusy;

        public AdjustmentsViewModel(LiveSession live, DialogViewModel dialog)
        {
            Live = live;
            _dialog = dialog;
            foreach (var adjustment in live.Session.Ecu.adjustments) Rows.Add(new AdjustRow(adjustment));
        }

        private bool CanRun(AdjustRow? row) => !IsBusy;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task Run(AdjustRow? row)
        {
            if (row == null) return;
            if (!Live.IsSimulation
                && !await _dialog.ConfirmAsync(row.Description, Loc.T("Adjust_ConfirmBody"), Loc.T("Adjust_Run")))
                return;

            IsBusy = true;
            row.IsRunning = true;
            row.Outcome = null;
            ActiveOutcome outcome;
            try
            {
                Func<byte, Func<byte, bool>, Task>? oneByteStep = null;
                if (row.Adjust.Type == "onebyte")
                {
                    // DiagnosticsSession runs this off the UI thread: hop back to show the dialog.
                    oneByteStep = (initial, applyQuery) => Dispatcher.UIThread.InvokeAsync(
                        () => _dialog.ShowOneByteAsync(new OneByteAdjustViewModel(row.Description, initial, applyQuery)));
                }
                outcome = await Live.Session.RunAdjustmentAsync(row.Adjust, oneByteStep, CancellationToken.None);
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                outcome = ActiveOutcome.Failed;
            }
            row.Outcome = outcome;
            row.IsRunning = false;
            Live.Recorder?.Event(SessionEventTypes.Adjustment, new JsonObject { ["adjustment"] = row.Description, ["outcome"] = outcome.ToString() });
            IsBusy = false;
        }

        public void RefreshTexts()
        {
            foreach (var row in Rows) row.RefreshText();
        }
    }

    /// <summary>The live chart of the parameters being read; history is kept from the moment they're selected.</summary>
    public partial class GraphViewModel : ObservableObject
    {
        public LiveSession Live { get; }

        [ObservableProperty]
        private IReadOnlyList<ChartTrace> traces = Array.Empty<ChartTrace>();

        [ObservableProperty]
        private bool isOverlay;

        [ObservableProperty]
        private bool follow = true;

        [ObservableProperty]
        private double windowSeconds = 60;

        [ObservableProperty]
        private double viewEnd;

        public GraphViewModel(LiveSession live)
        {
            Live = live;
            live.SelectionChanged += RebuildTraces;
            foreach (var row in live.Rows) row.PropertyChanged += OnRowChanged;
            RebuildTraces();
        }

        private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ParameterRow.IsPlotted)) RebuildTraces();
        }

        private void RebuildTraces()
        {
            Traces = Live.Selected
                .Where(r => r.IsPlotted && r.Buffer != null)
                .Select(r => new ChartTrace(r.Description, r.Unit, r.TraceColor, r.Buffer!))
                .ToList();
        }

        [RelayCommand]
        private void SetWindow(string seconds)
        {
            if (!double.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
            WindowSeconds = value;
            Follow = true;
        }

        public void Detach()
        {
            Live.SelectionChanged -= RebuildTraces;
            foreach (var row in Live.Rows) row.PropertyChanged -= OnRowChanged;
        }
    }

    /// <summary>Language, theme, logging, and the credits and license the original project requires.</summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;

        public IReadOnlyList<ChoiceItem> Languages { get; } = new[]
        {
            new ChoiceItem("", "Settings_LanguageSystem"),
            new ChoiceItem(AppSettings.LanguageEnglish, "Settings_LanguageEnglish"),
            new ChoiceItem(AppSettings.LanguageItalian, "Settings_LanguageItalian"),
        };

        public IReadOnlyList<ChoiceItem> Themes { get; } = new[]
        {
            new ChoiceItem(AppSettings.ThemeDark, "Settings_ThemeDark"),
            new ChoiceItem(AppSettings.ThemeLight, "Settings_ThemeLight"),
            new ChoiceItem(AppSettings.ThemeSystem, "Settings_ThemeSystem"),
        };

        private readonly bool _ready;

        [ObservableProperty]
        private ChoiceItem selectedLanguage = null!;

        [ObservableProperty]
        private ChoiceItem selectedTheme = null!;

        [ObservableProperty]
        private bool autoRecord;

        [ObservableProperty]
        private decimal? maxLogMegabytes;

        [ObservableProperty]
        private AboutContent about = AboutContent.English;

        public string DataFolder => _shell.Store.Root;
        public string ProductName { get; }
        public string VersionText { get; }
        public string ForkRepoUrl => AboutContent.ForkRepoUrl;
        public string OriginalProjectUrl => AboutContent.OriginalProjectUrl;

        public SettingsViewModel(ShellViewModel shell)
        {
            _shell = shell;
            var settings = shell.Settings;
            SelectedLanguage = Languages.FirstOrDefault(l => l.Value == settings.Language) ?? Languages[0];
            SelectedTheme = Themes.FirstOrDefault(t => t.Value == settings.Theme) ?? Themes[0];
            AutoRecord = settings.AutoRecord;
            MaxLogMegabytes = settings.MaxLogMegabytes;
            About = AboutContent.For(Loc.Language);

            ProductName = "IAW Scan 3";
            VersionText = AppInfo.Version;
            _ready = true;
        }

        public void RefreshTexts()
        {
            foreach (var item in Languages) item.RefreshText();
            foreach (var item in Themes) item.RefreshText();
        }

        partial void OnSelectedLanguageChanged(ChoiceItem value)
        {
            if (!_ready || value == null) return;
            _shell.Settings.Language = value.Value;
            _shell.Settings.Save();
            Loc.Apply(value.Value);
            About = AboutContent.For(Loc.Language);
        }

        partial void OnSelectedThemeChanged(ChoiceItem value)
        {
            if (!_ready || value == null) return;
            _shell.Settings.Theme = value.Value;
            _shell.Settings.Save();
            Appearance.ApplyTheme(value.Value);
        }

        partial void OnAutoRecordChanged(bool value)
        {
            if (!_ready) return;
            _shell.Settings.AutoRecord = value;
            _shell.Settings.Save();
        }

        partial void OnMaxLogMegabytesChanged(decimal? value)
        {
            if (!_ready || value == null) return;
            _shell.Settings.MaxLogMegabytes = (int)Math.Clamp(value.Value, 50, 100_000);
            _shell.Settings.Save();
        }

        [RelayCommand]
        private void OpenDataFolder()
        {
            System.IO.Directory.CreateDirectory(DataFolder);
            _shell.Files?.OpenFolder(DataFolder);
        }

        [RelayCommand]
        private void OpenUrl(string url) => _shell.Files?.OpenUrl(url);
    }

    /// <summary>A choice in a combo box: its stored value, and the text resource it shows.</summary>
    public sealed class ChoiceItem : ObservableObject
    {
        private readonly string _textKey;

        public ChoiceItem(string value, string textKey)
        {
            Value = value;
            _textKey = textKey;
        }

        public string Value { get; }
        public string Text => Loc.T(_textKey);

        public void RefreshText() => OnPropertyChanged(nameof(Text));
    }
}
