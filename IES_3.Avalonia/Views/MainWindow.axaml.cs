using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.ShowAboutDialogRequested = ShowAbout;
            };
        }

        private void ShowAbout() => new AboutWindow().ShowDialog(this);
    }
}
