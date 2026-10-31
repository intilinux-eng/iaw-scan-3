using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using IES_2.Avalonia;
using IES_2.Avalonia.Services;
using IES_2.Avalonia.ViewModels;
using IES_2.Avalonia.Views;
using IES_2.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace IES_2.Tests
{
    public static class TestAppBuilder
    {
        // The real application, its theme and styles, rendered off-screen with the real drawing engine.
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>A running main window over a throwaway settings file and session folder.</summary>
    public sealed class UiFixture : IDisposable
    {
        public string Directory { get; }
        public AppSettings Settings { get; }
        public ShellViewModel Shell { get; }
        public MainWindow Window { get; }

        public UiFixture(string language = AppSettings.LanguageEnglish, string theme = AppSettings.ThemeDark, int width = 1280, int height = 800)
        {
            Directory = Path.Combine(Path.GetTempPath(), "iawscan-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Settings = AppSettings.Load(Path.Combine(Directory, "settings.json"));
            Settings.Language = language;
            Settings.Theme = theme;
            Appearance.Apply(Settings);
            Shell = new ShellViewModel(Settings, Path.Combine(Directory, "Sessions"));
            Window = new MainWindow { DataContext = Shell, Width = width, Height = height };
            Window.Show();
            Pump();
        }

        public static void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>Lets the session's timers and polling loop run for a while.</summary>
        public static async Task RunFor(int milliseconds)
        {
            var end = Environment.TickCount64 + milliseconds;
            while (Environment.TickCount64 < end)
            {
                await Task.Delay(20);
                Pump();
            }
        }

        public async Task ConnectSimulated(string vehicle = "Coupe S 2.0")
        {
            var connect = Shell.Connect;
            connect.SearchText = vehicle;
            Pump();
            connect.SelectedVehicle = null;
            foreach (var choice in connect.Vehicles)
            {
                if (choice.Name != vehicle) continue;
                connect.SelectedVehicle = choice;
                break;
            }
            connect.IsSimulation = true;
            await connect.ConnectCommand.ExecuteAsync(null);
            Pump();
        }

        public void Save(string path)
        {
            Pump();
            var frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            Shell.Shutdown();
            Window.Close();
            Pump();
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { /* a leftover temp folder harms nothing */ }
        }
    }
}
