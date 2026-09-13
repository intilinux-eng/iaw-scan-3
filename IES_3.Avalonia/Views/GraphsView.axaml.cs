using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class GraphsView : UserControl
    {
        private GraphsViewModel? _vm;

        public GraphsView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                if (_vm != null) _vm.Redraw -= OnRedraw;
                _vm = DataContext as GraphsViewModel;
                if (_vm != null) _vm.Redraw += OnRedraw;
            };
        }

        private void OnRedraw() => Chart.InvalidateVisual();
    }
}
