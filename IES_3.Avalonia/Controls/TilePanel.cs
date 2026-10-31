using System;
using Avalonia;
using Avalonia.Controls;

namespace IES_2.Avalonia.Controls
{
    /// <summary>
    /// The dashboard's tiles: wrapped at their own size or, in full screen, in the grid that makes them as
    /// large as the shape of the screen allows. A Viewbox around each tile does the actual scaling.
    /// </summary>
    public sealed class TilePanel : WrapPanel
    {
        public static readonly StyledProperty<bool> FillProperty =
            AvaloniaProperty.Register<TilePanel, bool>(nameof(Fill));

        static TilePanel()
        {
            AffectsMeasure<TilePanel>(FillProperty);
        }

        public bool Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

        /// <summary>How many columns and rows give the largest tiles, and how much larger they end up.</summary>
        public static (int Columns, int Rows, double Scale) BestGrid(int count, Size tile, Size space)
        {
            var best = (Columns: count, Rows: 1, Scale: 0.0);
            for (var columns = 1; columns <= count; columns++)
            {
                var rows = (int)Math.Ceiling(count / (double)columns);
                var scale = Math.Min(space.Width / columns / tile.Width, space.Height / rows / tile.Height);
                if (scale > best.Scale) best = (columns, rows, scale);
            }
            return best;
        }

        private bool CanFill(Size space) =>
            Fill && Children.Count > 0 && !double.IsInfinity(space.Width) && !double.IsInfinity(space.Height);

        private Size NaturalTileSize()
        {
            Children[0].Measure(Size.Infinity);
            return Children[0].DesiredSize;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (!CanFill(availableSize)) return base.MeasureOverride(availableSize);

            var tile = NaturalTileSize();
            var (_, _, scale) = BestGrid(Children.Count, tile, availableSize);
            var cell = new Size(tile.Width * scale, tile.Height * scale);
            foreach (var child in Children) child.Measure(cell);
            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (!CanFill(finalSize)) return base.ArrangeOverride(finalSize);

            var tile = NaturalTileSize();
            var (columns, rows, scale) = BestGrid(Children.Count, tile, finalSize);
            var cell = new Size(tile.Width * scale, tile.Height * scale);
            foreach (var child in Children) child.Measure(cell);

            var top = (finalSize.Height - rows * cell.Height) / 2;
            for (var i = 0; i < Children.Count; i++)
            {
                var row = i / columns;
                var inThisRow = Math.Min(columns, Children.Count - row * columns);
                var left = (finalSize.Width - inThisRow * cell.Width) / 2;
                Children[i].Arrange(new Rect(new Point(left + i % columns * cell.Width, top + row * cell.Height), cell));
            }
            return finalSize;
        }
    }
}
