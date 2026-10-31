using System;
using Avalonia;
using Avalonia.Controls;
using IES_2.Avalonia.ViewModels;

namespace IES_2.Avalonia.Views
{
    public partial class GraphView : UserControl
    {
        private LiveSession? _live;

        public GraphView()
        {
            InitializeComponent();
            Chart.ViewReset += () =>
            {
                if (DataContext is GraphViewModel vm) vm.Follow = true;
            };
        }

        // The chart repaints with the session's tick, and only while it's on screen.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Unsubscribe();
            Subscribe();
        }

        private void Subscribe()
        {
            if (_live != null || DataContext is not GraphViewModel vm || VisualRoot == null) return;
            _live = vm.Live;
            _live.Ticked += OnTick;
        }

        private void Unsubscribe()
        {
            if (_live == null) return;
            _live.Ticked -= OnTick;
            _live = null;
        }

        private void OnTick() => Chart.InvalidateVisual();
    }
}
