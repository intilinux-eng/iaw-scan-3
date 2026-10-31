using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class MainWindow : Window, IUserFiles
    {
        private WindowState _stateBeforeFullscreen = WindowState.Normal;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is ShellViewModel shell)
                {
                    shell.Files = this;
                    shell.SetFullscreen = SetFullscreen;
                }
            };
            Closing += (_, _) => (DataContext as ShellViewModel)?.Shutdown();
        }

        private void SetFullscreen(bool fullscreen)
        {
            if (fullscreen)
            {
                _stateBeforeFullscreen = WindowState;
                WindowState = WindowState.FullScreen;
            }
            else
            {
                WindowState = _stateBeforeFullscreen;
            }
        }

        public async Task<Stream?> PickSaveAsync(string suggestedName)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                DefaultExtension = "zip",
                FileTypeChoices = new[] { new FilePickerFileType("ZIP") { Patterns = new[] { "*.zip" } } },
            });
            return file == null ? null : await file.OpenWriteAsync();
        }

        public async Task<string?> PickCsvAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        public void OpenFolder(string path) => _ = Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));

        public void OpenUrl(string url) => _ = Launcher.LaunchUriAsync(new Uri(url));
    }
}
