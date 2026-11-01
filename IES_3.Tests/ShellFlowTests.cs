using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IES_2.Avalonia.Models;
using IES_2.Avalonia.Services;
using IES_2.Avalonia.ViewModels;
using IES_2.ECU;
using IES_2.Session;
using Xunit;

namespace IES_2.Tests
{
    /// <summary>The whole application, driven in simulation: real window, real view models, real polling loop.</summary>
    [Collection(EcuStaticState.Name)]
    public class ShellFlowTests
    {
        [AvaloniaFact]
        public void StartsOnTheConnectPageOfferingEveryEcuFamily()
        {
            using var ui = new UiFixture();

            Assert.Equal(ShellViewModel.PageConnect, ui.Shell.CurrentKey);
            Assert.IsType<ConnectViewModel>(ui.Shell.CurrentPage);
            Assert.False(ui.Shell.IsConnected);
            // "All", the five engine families, and the immobiliser.
            Assert.Equal(7, ui.Shell.Connect.Filters.Count);
            Assert.NotEmpty(ui.Shell.Connect.Vehicles);
        }

        [AvaloniaFact]
        public void SearchingNarrowsTheCarsAndWordsCanComeInAnyOrder()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;

            connect.SearchText = "t/c coupe";

            Assert.NotEmpty(connect.Vehicles);
            Assert.All(connect.Vehicles, v => Assert.Contains("Coupe", v.Name));
            Assert.All(connect.Vehicles, v => Assert.Contains("T/C", v.Name));

            connect.SearchText = "no such car";
            Assert.Empty(connect.Vehicles);
        }

        [AvaloniaFact]
        public void TheImmobiliserFilterListsItsOwnCars()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            var filter = connect.Filters.Single(f => f.Ecu?.Id == code.name);

            connect.ChooseFilterCommand.Execute(filter);

            Assert.True(connect.HasChoice);
            Assert.Equal(code.name, connect.ChosenEcu!.Id);
            Assert.All(connect.Vehicles, v => Assert.Equal(code.name, v.Ecu.Id));
            Assert.True(filter.IsSelected);
            Assert.Single(connect.Filters, f => f.IsSelected);
        }

        [AvaloniaFact]
        public void ConnectingNeedsACarAndACableOrTheSimulation()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.Deactivate();
            connect.SelectedPortInfo = null;

            Assert.False(connect.ConnectCommand.CanExecute(null));

            connect.SelectedVehicle = connect.Vehicles[0];
            Assert.False(connect.ConnectCommand.CanExecute(null));

            connect.IsSimulation = true;
            Assert.True(connect.ConnectCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public async Task ASimulatedSessionShowsTheChosenCarAndItsValues()
        {
            using var ui = new UiFixture();

            await ui.ConnectSimulated("Coupe S 2.0");

            Assert.True(ui.Shell.IsConnected);
            Assert.True(ui.Shell.IsSimulation);
            Assert.Equal(ShellViewModel.PageDashboard, ui.Shell.CurrentKey);
            Assert.Equal("Coupe S 2.0", ui.Shell.Live!.CarModel);

            await UiFixture.RunFor(1200);
            var tiles = ui.Shell.Dashboard!.Tiles;
            Assert.NotEmpty(tiles);
            Assert.All(tiles, t => Assert.NotEqual("", t.Value));
            Assert.All(tiles, t => Assert.False(t.IsInvalid));
            Assert.All(tiles, t => Assert.True(t.Buffer!.Count > 2));
            Assert.All(tiles, t => Assert.False(double.IsNaN(t.SessionMax)));

            await ui.Shell.DisconnectCommand.ExecuteAsync(null);
            Assert.False(ui.Shell.IsConnected);
            Assert.IsType<ConnectViewModel>(ui.Shell.CurrentPage);
        }

        [AvaloniaFact]
        public async Task SimulatedValuesAreBelievable()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated("Coupe S 2.0");
            await UiFixture.RunFor(800);

            var battery = ui.Shell.Live!.Rows.Single(r => r.Unit == "V");

            Assert.InRange(battery.Numeric, 10, 16);
        }

        [AvaloniaFact]
        public async Task EveryPageOpensAndRendersWhileConnected()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            await UiFixture.RunFor(300);

            foreach (var page in new[]
            {
                ShellViewModel.PageDashboard, ShellViewModel.PageParameters, ShellViewModel.PageFaults,
                ShellViewModel.PageTests, ShellViewModel.PageGraph, ShellViewModel.PageSessions, ShellViewModel.PageSettings,
            })
            {
                ui.Shell.Navigate(page);
                await UiFixture.RunFor(150);
                Assert.Equal(page, ui.Shell.CurrentKey);
                Assert.NotNull(ui.Window.CaptureRenderedFrame());
            }
        }

        [AvaloniaFact]
        public async Task TurningOffOneParameterDoesntStopAnotherSharingItsByte()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var live = ui.Shell.Live!;

            // The two manifold pressure scales read the same byte.
            var shared = live.Rows.Where(r => r.Unit == "hPa").ToList();
            Assert.Equal(2, shared.Count);
            var request = shared[0].Data.RequestSet[0];
            Assert.Equal(request, shared[1].Data.RequestSet[0]);

            shared[0].IsChecked = true;
            shared[1].IsChecked = true;
            Assert.True(live.Session.RequestEnabled[request]);

            shared[1].IsChecked = false;
            Assert.True(live.Session.RequestEnabled[request]);

            shared[0].IsChecked = false;
            Assert.False(live.Session.RequestEnabled[request]);
        }

        [AvaloniaFact]
        public async Task TheChosenParametersComeBackAtTheNextConnection()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var chosen = ui.Shell.Live!.Rows.Single(r => r.Description == "Injection Timing Angle");
            chosen.IsChecked = true;
            var order = ui.Shell.Live.Selected.Select(r => r.Index).ToArray();
            await ui.Shell.DisconnectCommand.ExecuteAsync(null);

            ui.Shell.Connect.IsSimulation = true;
            await ui.Shell.Connect.ConnectCommand.ExecuteAsync(null);

            Assert.Equal(order, ui.Shell.Live!.Selected.Select(r => r.Index).ToArray());
            var reloaded = AppSettings.Load(Path.Combine(ui.Directory, "settings.json"));
            Assert.Equal(order, reloaded.Selection[iaw04k.name]);
        }

        [AvaloniaFact]
        public async Task TilesCanBeReorderedAndRemoved()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var dashboard = ui.Shell.Dashboard!;
            var first = dashboard.Tiles[0];
            var second = dashboard.Tiles[1];

            dashboard.MoveForwardCommand.Execute(first);
            Assert.Same(second, dashboard.Tiles[0]);
            Assert.Same(first, dashboard.Tiles[1]);

            dashboard.MoveBackCommand.Execute(second); // already first: nothing happens
            Assert.Same(second, dashboard.Tiles[0]);

            dashboard.RemoveCommand.Execute(first);
            Assert.DoesNotContain(first, dashboard.Tiles);
            Assert.False(first.IsChecked);
        }

        [AvaloniaFact]
        public async Task FaultCodesAreReadWhateverPageIsOpen()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();

            await UiFixture.RunFor(1200);

            Assert.True(ui.Shell.Live!.FaultsRead);
            Assert.NotEmpty(ui.Shell.Live.Faults);
            Assert.NotEmpty(ui.Shell.Live.FaultHistory);
            Assert.False(ui.Shell.Faults!.IsClean);
        }

        [AvaloniaFact]
        public async Task ClearingCodesIsntOfferedInSimulation()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();

            Assert.False(ui.Shell.Faults!.ClearCodesCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public void AQuestionWaitsForItsAnswer()
        {
            var dialog = new DialogViewModel();

            var accepted = dialog.ConfirmAsync("Title", "Body", "OK");
            Assert.True(dialog.IsConfirmOpen);
            Assert.False(accepted.IsCompleted);
            dialog.AcceptCommand.Execute(null);
            Assert.True(accepted.Result);
            Assert.False(dialog.IsConfirmOpen);

            var cancelled = dialog.ConfirmAsync("Title", "Body", "OK");
            dialog.CancelCommand.Execute(null);
            Assert.False(cancelled.Result);
        }

        [AvaloniaFact]
        public async Task ActuatorTestsInSimulationJustReportDemo()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var tests = ui.Shell.Tests!;
            var row = tests.Rows[0];

            await tests.RunCommand.ExecuteAsync(row);

            Assert.Equal(ActiveOutcome.Demo, row.Outcome);
            Assert.False(row.IsRunning);
            Assert.True(row.Passed);
            Assert.False(tests.IsBusy);
        }

        [AvaloniaFact]
        public async Task PagesWithNothingToShowAreHidden()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.ChooseFilterCommand.Execute(connect.Filters.Single(f => f.Ecu?.Id == code.name));
            connect.IsSimulation = true;

            await connect.ConnectCommand.ExecuteAsync(null);

            Assert.True(ui.Shell.IsConnected);
            Assert.False(ui.Shell.HasTests);
            Assert.False(ui.Shell.HasAdjustments);
        }

        [AvaloniaFact]
        public async Task AdjustmentsInSimulationJustReportDemo()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.ChooseFilterCommand.Execute(connect.Filters.Single(f => f.Ecu?.Id == iaw16f.name));
            connect.IsSimulation = true;
            await connect.ConnectCommand.ExecuteAsync(null);
            Assert.True(ui.Shell.HasAdjustments);

            var adjustments = ui.Shell.Adjustments!;
            var row = adjustments.Rows[0];
            await adjustments.RunCommand.ExecuteAsync(row);

            Assert.Equal(ActiveOutcome.Demo, row.Outcome);
        }

        [AvaloniaFact]
        public async Task TheByteValueAdjustmentStaysInRangeAndReportsWhatItSent()
        {
            using var ui = new UiFixture();
            byte? sent = null;
            var adjust = new OneByteAdjustViewModel("Trim", 254, value => { sent = value; return true; });

            adjust.StepCommand.Execute("1");
            adjust.StepCommand.Execute("1");
            Assert.Equal(255, adjust.Value);

            await adjust.ApplyCommand.ExecuteAsync(null);
            Assert.Equal((byte)255, sent);
            Assert.Contains("255", adjust.StatusMessage);

            var failing = new OneByteAdjustViewModel("Trim", 10, _ => false);
            await failing.ApplyCommand.ExecuteAsync(null);
            Assert.Equal(Loc.T("Adjust_SendFailed"), failing.StatusMessage);
        }

        [AvaloniaFact]
        public async Task RecordingWritesTheChosenParametersAndTheSessionOpensAgain()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var live = ui.Shell.Live!;
            var id = live.Recorder!.Id;
            Assert.False(live.IsRecording); // simulations don't record by themselves

            ui.Shell.ToggleRecordingCommand.Execute(null);
            Assert.True(live.IsRecording);
            await UiFixture.RunFor(1200);
            ui.Shell.ToggleRecordingCommand.Execute(null);
            Assert.False(live.IsRecording);
            var rows = live.Recorder.DataRows;
            Assert.True(rows > 2);

            ui.Shell.Navigate(ShellViewModel.PageSessions);
            await ui.Shell.Sessions.RefreshCommand.ExecuteAsync(null);
            var inProgress = ui.Shell.Sessions.Items.Single(i => i.Id == id);
            Assert.True(inProgress.IsInProgress);

            await ui.Shell.DisconnectCommand.ExecuteAsync(null);
            await ui.Shell.Sessions.RefreshCommand.ExecuteAsync(null);
            var item = ui.Shell.Sessions.Items.Single(i => i.Id == id);
            Assert.True(item.HasData);
            Assert.True(item.IsSimulation);
            Assert.False(item.IsInProgress);

            await ui.Shell.Sessions.OpenCommand.ExecuteAsync(item);
            var detail = ui.Shell.Sessions.Detail!;
            Assert.True(detail.HasData);
            Assert.True(detail.HasEvents);
            Assert.NotEmpty(detail.Traces);
            Assert.All(detail.Series, s => Assert.NotEqual("–", s.MaxText));
        }

        [AvaloniaFact]
        public async Task ASimulationThatRecordedNothingLeavesNoTrace()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            var id = ui.Shell.Live!.Recorder!.Id;

            await ui.Shell.DisconnectCommand.ExecuteAsync(null);

            Assert.Null(ui.Shell.Store.Find(id));
        }

        [AvaloniaFact]
        public async Task TwoSessionsCanBeCompared()
        {
            using var ui = new UiFixture();
            SampleSessions.Write(ui.Shell.Store.Root);
            var sessions = ui.Shell.Sessions;
            await sessions.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(2, sessions.Items.Count);

            await sessions.OpenCommand.ExecuteAsync(sessions.Items[0]);
            var detail = sessions.Detail!;
            Assert.All(detail.Traces, t => Assert.Null(t.Compare));

            detail.CompareWith = detail.CompareChoices[0];
            await UiFixture.RunFor(400);

            Assert.All(detail.Traces, t => Assert.NotNull(t.Compare));

            detail.ClearCompareCommand.Execute(null);
            await UiFixture.RunFor(100);
            Assert.All(detail.Traces, t => Assert.Null(t.Compare));
        }

        [AvaloniaFact]
        public async Task AnEventJumpsTheChartToThatMoment()
        {
            using var ui = new UiFixture();
            SampleSessions.Write(ui.Shell.Store.Root);
            var sessions = ui.Shell.Sessions;
            await sessions.RefreshCommand.ExecuteAsync(null);
            await sessions.OpenCommand.ExecuteAsync(sessions.Items[0]);
            var detail = sessions.Detail!;
            var fault = detail.Events.First(e => e.IsProblem);

            detail.JumpToCommand.Execute(fault);

            Assert.True(detail.WindowSeconds < detail.Duration);
            Assert.InRange(fault.T, detail.ViewEnd - detail.WindowSeconds, detail.ViewEnd);
        }

        [AvaloniaFact]
        public async Task DeletingASessionAsksFirst()
        {
            using var ui = new UiFixture();
            SampleSessions.Write(ui.Shell.Store.Root);
            var sessions = ui.Shell.Sessions;
            await sessions.RefreshCommand.ExecuteAsync(null);
            var item = sessions.Items[0];

            var refused = sessions.DeleteCommand.ExecuteAsync(item);
            Assert.True(ui.Shell.Dialog.IsConfirmOpen);
            ui.Shell.Dialog.CancelCommand.Execute(null);
            await refused;
            Assert.NotNull(ui.Shell.Store.Find(item.Id));

            var accepted = sessions.DeleteCommand.ExecuteAsync(item);
            ui.Shell.Dialog.AcceptCommand.Execute(null);
            await accepted;
            Assert.Null(ui.Shell.Store.Find(item.Id));
            Assert.Single(sessions.Items);
        }

        [AvaloniaFact]
        public void TheLanguageChangesLiveAndIsRemembered()
        {
            using var ui = new UiFixture(AppSettings.LanguageEnglish);
            Assert.Equal("Dashboard", Loc.T("Nav_Dashboard"));

            ui.Shell.SettingsPage.SelectedLanguage = ui.Shell.SettingsPage.Languages.Single(l => l.Value == AppSettings.LanguageItalian);

            Assert.Equal("Cruscotto", Loc.T("Nav_Dashboard"));
            Assert.Equal("Scuro", ui.Shell.SettingsPage.Themes[0].Text);
            Assert.Equal(AboutContent.Italian, ui.Shell.SettingsPage.About);
            Assert.Equal(AppSettings.LanguageItalian, AppSettings.Load(Path.Combine(ui.Directory, "settings.json")).Language);
        }

        [AvaloniaFact]
        public async Task ParameterNamesFollowTheLanguageAtConnection()
        {
            using var english = new UiFixture(AppSettings.LanguageEnglish);
            await english.ConnectSimulated();
            Assert.Contains(english.Shell.Live!.Rows, r => r.Description == "Engine Speed");
            await english.Shell.DisconnectCommand.ExecuteAsync(null);

            using var italian = new UiFixture(AppSettings.LanguageItalian);
            await italian.ConnectSimulated();
            Assert.Contains(italian.Shell.Live!.Rows, r => r.Description == "Velocità motore");
        }

        [AvaloniaFact]
        public void TheThemeCanBeChanged()
        {
            using var ui = new UiFixture();
            ui.Shell.SettingsPage.SelectedTheme = ui.Shell.SettingsPage.Themes.Single(t => t.Value == AppSettings.ThemeLight);
            UiFixture.Pump();
            Assert.Equal(global::Avalonia.Styling.ThemeVariant.Light, global::Avalonia.Application.Current!.RequestedThemeVariant);
            Assert.NotNull(ui.Window.CaptureRenderedFrame());
        }

        [AvaloniaFact]
        public async Task TheDashboardCanGoFullscreenAndBack()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();

            ui.Shell.ToggleImmersiveCommand.Execute(null);
            Assert.True(ui.Shell.IsImmersive);
            Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, ui.Window.WindowState);

            ui.Shell.Navigate(ShellViewModel.PageSettings);
            Assert.False(ui.Shell.IsImmersive);
        }

        [AvaloniaFact]
        public async Task FullscreenGrowsTheTilesToFillTheScreen()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            UiFixture.Pump();
            System.Collections.Generic.List<global::Avalonia.Controls.Viewbox> Tiles() => ui.Window.GetVisualDescendants()
                .OfType<global::Avalonia.Controls.Viewbox>().Where(v => v.Classes.Contains("tileScale")).ToList();
            Assert.NotEmpty(Tiles());
            var normalWidth = Tiles()[0].Bounds.Width;

            ui.Shell.ToggleImmersiveCommand.Execute(null);
            UiFixture.Pump();

            var tiles = Tiles();
            Assert.True(tiles[0].Bounds.Width > normalWidth * 1.3, $"{normalWidth} -> {tiles[0].Bounds.Width}");
            foreach (var tile in tiles)
            {
                var corner = tile.TranslatePoint(new global::Avalonia.Point(tile.Bounds.Width, tile.Bounds.Height), ui.Window)!.Value;
                Assert.InRange(corner.X, 0, ui.Window.Bounds.Width + 1);
                Assert.InRange(corner.Y, 0, ui.Window.Bounds.Height + 1);
            }

            ui.Shell.ToggleImmersiveCommand.Execute(null);
            UiFixture.Pump();
            Assert.Equal(normalWidth, Tiles()[0].Bounds.Width, 1);
        }

        [Theory]
        [InlineData(1, 800, 480, 1, 1)]
        [InlineData(6, 1920, 1080, 3, 2)]
        [InlineData(6, 5120, 1440, 6, 1)]
        [InlineData(6, 800, 480, 3, 2)]
        [InlineData(12, 1024, 600, 4, 3)]
        public void FullscreenPicksTheGridWithTheLargestTiles(int tiles, double width, double height, int columns, int rows)
        {
            var grid = IES_2.Avalonia.Controls.TilePanel.BestGrid(tiles, new global::Avalonia.Size(242, 226), new global::Avalonia.Size(width, height));
            Assert.Equal((columns, rows), (grid.Columns, grid.Rows));
        }

        [AvaloniaFact]
        public async Task ASessionOpensOnItsValuesEvenIfRecordingStartedLate()
        {
            using var ui = new UiFixture();
            var csv = Path.Combine(ui.Directory, "late.csv");
            File.WriteAllLines(csv, new[] { "t_s;utc;Engine Speed [RPM]", "600.0;2026-01-01T10:10:00.000Z;900", "660.0;2026-01-01T10:11:00.000Z;950" });
            ui.Shell.Files = new CsvPicker(csv);

            ui.Shell.Navigate(ShellViewModel.PageSessions);
            await ui.Shell.Sessions.ImportCsvCommand.ExecuteAsync(null);

            var detail = ui.Shell.Sessions.Detail!;
            Assert.Equal(660, detail.ViewEnd, 1);
            Assert.Equal(60, detail.WindowSeconds, 1);
        }

        private sealed class CsvPicker : IUserFiles
        {
            private readonly string _path;
            public CsvPicker(string path) => _path = path;
            public Task<Stream?> PickSaveAsync(string suggestedName) => Task.FromResult<Stream?>(null);
            public Task<string?> PickCsvAsync() => Task.FromResult<string?>(_path);
            public void OpenFolder(string path) { }
            public void OpenUrl(string url) { }
        }

        [AvaloniaFact]
        public void ChoosingAnIaw16fExplainsWhyTheOrderMatters()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;

            connect.SelectedVehicle = connect.Vehicles.First(v => v.Ecu.Id == iaw16f.name);
            Assert.True(connect.IsoOnlyAtIgnitionOn);

            connect.SelectedVehicle = connect.Vehicles.First(v => v.Ecu.Id == iaw04k.name);
            Assert.False(connect.IsoOnlyAtIgnitionOn);
        }

        [AvaloniaFact]
        public async Task TheCableTestSaysWhenThePortCantBeOpened()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.Deactivate();
            connect.Ports.Clear();
            connect.Ports.Add(new SerialPortInfo("NOT_A_PORT"));
            connect.SelectedPortInfo = connect.Ports[0];
            Assert.True(connect.TestCableCommand.CanExecute(null));

            await connect.TestCableCommand.ExecuteAsync(null);

            Assert.False(connect.CableTestPassed);
            Assert.Contains("NOT_A_PORT", connect.CableTestMessage);
            Assert.True(connect.TestCableCommand.CanExecute(null));

            connect.SelectedPortInfo = null;
            Assert.Null(connect.CableTestMessage);
            Assert.False(connect.TestCableCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public void TheCableTestIsNotOfferedInSimulation()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.Deactivate();
            connect.Ports.Clear();
            connect.Ports.Add(new SerialPortInfo("COM5", "USB-SERIAL CH340", true));
            connect.SelectedPortInfo = connect.Ports[0];

            connect.IsSimulation = true;

            Assert.False(connect.TestCableCommand.CanExecute(null));
        }

        [AvaloniaFact]
        public void ACableWithoutADriverIsPointedAtItsMaker()
        {
            using var ui = new UiFixture();
            var connect = ui.Shell.Connect;
            connect.Deactivate();
            var files = new OpenedUrls();
            ui.Shell.Files = files;

            connect.CableWithoutDriver = UsbSerialDevices.Identify(0x1A86, 0x7523);
            UiFixture.Pump();

            Assert.Contains("CH340", connect.DriverMessage);
            Assert.True(connect.HasDriverPage);
            connect.GetDriverCommand.Execute(null);
            Assert.Contains("wch-ic.com", Assert.Single(files.Urls));

            connect.CableWithoutDriver = null;
            Assert.Null(connect.DriverMessage);
        }

        private sealed class OpenedUrls : IUserFiles
        {
            public System.Collections.Generic.List<string> Urls { get; } = new();
            public Task<Stream?> PickSaveAsync(string suggestedName) => Task.FromResult<Stream?>(null);
            public Task<string?> PickCsvAsync() => Task.FromResult<string?>(null);
            public void OpenFolder(string path) { }
            public void OpenUrl(string url) => Urls.Add(url);
        }

        [AvaloniaFact]
        public async Task EscapeClosesAQuestionFirstThenFullscreen()
        {
            using var ui = new UiFixture();
            await ui.ConnectSimulated();
            ui.Shell.ToggleImmersiveCommand.Execute(null);

            var answer = ui.Shell.Dialog.ConfirmAsync("title", "body", "ok");
            ui.Shell.EscapeCommand.Execute(null);
            Assert.False(await answer);
            Assert.True(ui.Shell.IsImmersive);

            ui.Shell.EscapeCommand.Execute(null);
            Assert.False(ui.Shell.IsImmersive);
            Assert.NotEqual(global::Avalonia.Controls.WindowState.FullScreen, ui.Window.WindowState);
        }

        [Fact]
        public void TheVersionShownIsTheOneTheBuildWasPublishedAs()
        {
            Assert.Matches(@"^\d+\.\d+\.\d+", AppInfo.Version);
            Assert.DoesNotContain("+", AppInfo.Version);
        }

        [AvaloniaFact]
        public void FunctionKeysDontGoAnywhereWithoutASession()
        {
            using var ui = new UiFixture();
            ui.Shell.Navigate(ShellViewModel.PageDashboard);
            Assert.IsType<ConnectViewModel>(ui.Shell.CurrentPage);
            ui.Shell.Navigate(ShellViewModel.PageTests);
            Assert.IsType<ConnectViewModel>(ui.Shell.CurrentPage);
        }
    }

    public class GaugeRangeTests
    {
        [Theory]
        [InlineData(7500, 8000)]
        [InlineData(1, 1)]
        [InlineData(0.3, 0.3)]
        [InlineData(1300, 1500)]
        public void WideningRoundsUp(double value, double expected)
        {
            Assert.Equal(expected, GaugeRange.Ceiling(value), 6);
        }
    }

    public class SettingsTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "iawscan-tests", Guid.NewGuid().ToString("N"), "settings.json");

        public void Dispose()
        {
            var directory = Path.GetDirectoryName(_path)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Fact]
        public void SurviveARestart()
        {
            var settings = AppSettings.Load(_path);
            settings.Language = AppSettings.LanguageItalian;
            settings.LastPort = "COM7";
            settings.Selection["IAW-04K.P8"] = new[] { 3, 1 };
            settings.Save();

            var reloaded = AppSettings.Load(_path);

            Assert.Equal(AppSettings.LanguageItalian, reloaded.Language);
            Assert.Equal("COM7", reloaded.LastPort);
            Assert.Equal(new[] { 3, 1 }, reloaded.Selection["IAW-04K.P8"]);
        }

        [Fact]
        public void ACorruptFileMeansDefaults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, "{ not json");

            var settings = AppSettings.Load(_path);

            Assert.Equal(AppSettings.ThemeDark, settings.Theme);
            Assert.True(settings.AutoRecord);
        }
    }
}
