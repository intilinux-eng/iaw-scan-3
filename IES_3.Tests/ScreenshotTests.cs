using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IES_2.Avalonia.Services;
using IES_2.Avalonia.ViewModels;
using IES_2.Session;
using Xunit;

namespace IES_2.Tests
{
    /// <summary>
    /// Renders every screen of the real application to PNG, for the documentation and for looking at the
    /// result. Only runs when IAW_SCREENSHOTS names the output folder, because it takes a while: the
    /// graph needs some simulated time to fill up.
    /// </summary>
    [Collection(EcuStaticState.Name)]
    public class ScreenshotTests
    {
        private static string? OutputFolder => Environment.GetEnvironmentVariable("IAW_SCREENSHOTS");

        [AvaloniaFact]
        public async Task English() => await Capture(AppSettings.LanguageEnglish, "en", AppSettings.ThemeDark);

        [AvaloniaFact]
        public async Task Italian() => await Capture(AppSettings.LanguageItalian, "it", AppSettings.ThemeDark);

        [AvaloniaFact]
        public async Task EnglishLight() => await Capture(AppSettings.LanguageEnglish, "en-light", AppSettings.ThemeLight, dashboardOnly: true);

        private static async Task Capture(string language, string folder, string theme, bool dashboardOnly = false)
        {
            if (string.IsNullOrEmpty(OutputFolder)) return;
            var target = Path.Combine(OutputFolder, folder);

            using var ui = new UiFixture(language, theme);
            SampleSessions.Write(ui.Shell.Store.Root, language);

            // There may be no cable where these are generated: show the page as it looks with one.
            var connect = ui.Shell.Connect;
            connect.Deactivate();
            connect.SearchText = "coupe";
            connect.Ports.Clear();
            connect.Ports.Add(new SerialPortInfo("COM5", "USB-SERIAL CH340", true));
            connect.SelectedPortInfo = connect.Ports[0];
            connect.SelectedVehicle = connect.Vehicles.First(v => v.Name == "Coupe S 2.0");
            connect.CableTestPassed = true;
            connect.CableTestMessage = Loc.T("Connect_TestOk");
            UiFixture.Pump();
            if (!dashboardOnly) ui.Save(Path.Combine(target, "01-connect.png"));

            await ui.ConnectSimulated();
            var live = ui.Shell.Live!;
            await UiFixture.RunFor(14000);
            ui.Save(Path.Combine(target, "02-dashboard.png"));
            if (dashboardOnly)
            {
                await ui.Shell.DisconnectCommand.ExecuteAsync(null);
                return;
            }

            ui.Shell.Navigate(ShellViewModel.PageParameters);
            await UiFixture.RunFor(300);
            ui.Save(Path.Combine(target, "03-parameters.png"));

            ui.Shell.Navigate(ShellViewModel.PageFaults);
            await UiFixture.RunFor(300);
            ui.Save(Path.Combine(target, "04-faults.png"));

            ui.Shell.Navigate(ShellViewModel.PageTests);
            await UiFixture.RunFor(300);
            ui.Save(Path.Combine(target, "05-tests.png"));

            ui.Shell.Navigate(ShellViewModel.PageGraph);
            ui.Shell.Graph!.WindowSeconds = 15;
            await UiFixture.RunFor(500);
            ui.Save(Path.Combine(target, "06-graph.png"));

            ui.Shell.Navigate(ShellViewModel.PageSessions);
            await UiFixture.RunFor(800);
            ui.Save(Path.Combine(target, "07-sessions.png"));

            var sessions = ui.Shell.Sessions;
            await sessions.OpenCommand.ExecuteAsync(sessions.Items[sessions.Items.Count - 1]);
            await UiFixture.RunFor(300);
            sessions.Detail!.CompareWith = sessions.Detail.CompareChoices[0];
            await UiFixture.RunFor(800);
            ui.Save(Path.Combine(target, "08-session.png"));
            sessions.Detail = null;

            ui.Shell.Navigate(ShellViewModel.PageSettings);
            await UiFixture.RunFor(200);
            // The temporary folder's path would name whoever generated the screenshots.
            var shownPath = ui.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == ui.Shell.Store.Root).ToList();
            Assert.NotEmpty(shownPath);
            foreach (var text in shownPath) text.Text = @"C:\Users\you\AppData\Local\IAWScan3\Sessions";
            UiFixture.Pump();
            ui.Save(Path.Combine(target, "09-settings.png"));

            await ui.Shell.DisconnectCommand.ExecuteAsync(null);
            await UiFixture.RunFor(100);
            Assert.Null(ui.Shell.Live);
            Assert.NotNull(live);
        }
    }

    /// <summary>
    /// Believable recorded drives for screenshots and tests, written straight in the session format so
    /// they can span many minutes without waiting for them.
    /// </summary>
    public static class SampleSessions
    {
        public static IReadOnlyList<string> Write(string root, string language = "en")
        {
            var italian = language == "it";
            return new[]
            {
                Drive(root, new DateTime(2026, 10, 25, 7, 42, 0, DateTimeKind.Utc), 900, 0.0, italian, faultAt: null),
                Drive(root, new DateTime(2026, 10, 28, 16, 5, 0, DateTimeKind.Utc), 1200, 3.5, italian, faultAt: 410),
            };
        }

        private static string Drive(string root, DateTime started, int seconds, double batteryDrop, bool italian, int? faultAt)
        {
            var id = started.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-IAW_04K_P8";
            var directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);

            var header = new JsonObject
            {
                ["format_version"] = 1,
                ["app_version"] = "3.0.0.0",
                ["ecu_id"] = "IAW-04K.P8",
                ["ecu_type"] = "Weber-Marelli IAW-04K.P8 MPI",
                ["vehicle"] = "Coupe S 2.0",
                ["iso_code"] = "55-CE-85-83-94-BF",
                ["rep_code"] = "61602.063.01",
                ["simulation"] = true,
                ["port"] = null,
                ["started_utc"] = started,
                ["ended_utc"] = started.AddSeconds(seconds),
                ["duration_seconds"] = (double)seconds,
                ["data_rows"] = seconds * 2,
                ["columns"] = new JsonArray(),
            };
            File.WriteAllText(Path.Combine(directory, SessionRecorder.HeaderFileName), header.ToJsonString());

            var events = new List<string>
            {
                Event(1, 0, started, SessionEventTypes.Connected, new JsonObject()),
                Event(2, 1, started, SessionEventTypes.FaultsRead, new JsonObject { ["faults"] = new JsonArray() }),
                Event(3, 2, started, SessionEventTypes.DataStarted, new JsonObject()),
            };
            if (faultAt is { } at)
            {
                events.Add(Event(4, at, started, SessionEventTypes.FaultsChanged, new JsonObject
                {
                    ["faults"] = new JsonArray(Lambda(italian)),
                    ["added"] = new JsonArray(Lambda(italian)),
                    ["removed"] = new JsonArray(),
                }));
            }
            events.Add(Event(5, seconds, started, SessionEventTypes.Disconnected, new JsonObject()));
            File.WriteAllLines(Path.Combine(directory, SessionRecorder.EventsFileName), events);

            var random = new Random(started.Day);
            using var data = new StreamWriter(Path.Combine(directory, SessionRecorder.DataFileName));
            data.WriteLine(italian
                ? "t_s;utc;Velocità motore [RPM];Temperatura Acqua [°C];Temperatura Aria [°C];Voltaggio batteria [V];Anticipo di accensione [°];Pressione collettore [hPa]"
                : "t_s;utc;Engine Speed [RPM];Water Temperature [°C];Air Temperature [°C];Battery Voltage [V];Ignition Advance [°];Manifold Pressure [hPa]");
            for (double t = 0; t < seconds; t += 0.5)
            {
                var warm = Math.Min(1, t / 500);
                var rpm = 900 + 1700 * (0.5 + 0.5 * Math.Sin(t / 37)) * (0.6 + 0.4 * Math.Sin(t / 11)) + random.NextDouble() * 40;
                var coolant = 22 + 66 * warm + random.NextDouble() * 0.4;
                var air = 18 + 12 * warm + Math.Sin(t / 60);
                var battery = 14.1 - batteryDrop * Math.Max(0, Math.Sin(t / 200)) * 0.3 + random.NextDouble() * 0.05;
                var advance = 12 + rpm / 150 + random.NextDouble();
                var map = 350 + rpm / 6 + 60 * Math.Sin(t / 9);
                data.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t:0.000};{SessionRecorder.FormatUtc(started.AddSeconds(t))};{rpm:0};{coolant:0.0};{air:0.0};{battery:0.00};{advance:0.00};{map:0}"));
            }
            return id;
        }

        private static JsonObject Lambda(bool italian) => new()
        {
            ["description"] = italian ? "Sensore Lambda" : "Lambda Sensor",
            ["reason"] = "",
            ["state"] = "Active",
            ["mil"] = false,
        };

        private static string Event(int seq, double t, DateTime started, string type, JsonObject data) => new JsonObject
        {
            ["seq"] = seq,
            ["t"] = t,
            ["utc"] = SessionRecorder.FormatUtc(started.AddSeconds(t)),
            ["type"] = type,
            ["data"] = data,
        }.ToJsonString();
    }
}
