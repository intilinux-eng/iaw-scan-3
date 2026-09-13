using System.Threading.Tasks;
using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class AdjustmentsView : UserControl
    {
        public AdjustmentsView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (DataContext is AdjustmentsViewModel vm)
                    vm.ShowOneByteDialog = ShowOneByteDialogAsync;
            };
        }

        private async Task ShowOneByteDialogAsync(OneByteAdjustViewModel dialogVm)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var window = new OneByteAdjustWindow(dialogVm);
            if (owner != null)
                await window.ShowDialog(owner);
            else
                window.Show();
        }
    }
}
