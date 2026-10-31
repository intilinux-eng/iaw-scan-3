using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class SessionDetailView : UserControl
    {
        public SessionDetailView()
        {
            InitializeComponent();
            Chart.ViewReset += () =>
            {
                if (DataContext is SessionDetailViewModel vm) vm.FitAllCommand.Execute(null);
            };
        }

        private void OnEventSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is SessionDetailViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is SessionEventRow row)
                vm.JumpToCommand.Execute(row);
        }
    }
}
