using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class AboutWindow : Window
    {
        private AboutViewModel ViewModel => (AboutViewModel)DataContext!;

        public AboutWindow()
        {
            InitializeComponent();
            DataContext = new AboutViewModel();
        }

        private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

        private void OnForkLinkClick(object? sender, RoutedEventArgs e) => OpenUrl(ViewModel.ForkRepoUrl);

        private void OnOriginalLinkClick(object? sender, RoutedEventArgs e) => OpenUrl(ViewModel.OriginalProjectUrl);

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort: no default browser handler available, nothing sensible to do here.
            }
        }
    }
}
