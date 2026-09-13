using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class OneByteAdjustWindow : Window
    {
        public OneByteAdjustWindow()
        {
            InitializeComponent();
        }

        public OneByteAdjustWindow(OneByteAdjustViewModel vm) : this()
        {
            DataContext = vm;
            vm.CloseRequested += Close;
        }
    }
}
