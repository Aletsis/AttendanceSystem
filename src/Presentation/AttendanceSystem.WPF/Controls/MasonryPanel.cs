using System.Windows;
using System.Windows.Controls;

namespace AttendanceSystem.WPF.Controls
{
    /// <summary>
    /// Acomoda las tarjetas en tantas columnas como quepan (cada una de al menos <see cref="MinColumnWidth"/>) y pone
    /// cada tarjeta en la columna más corta. En pantallas anchas aprovecha el espacio; en angostas queda una sola columna.
    /// </summary>
    public class MasonryPanel : Panel
    {
        public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(480d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
            nameof(MaxColumns), typeof(int), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
            nameof(ColumnSpacing), typeof(double), typeof(MasonryPanel),
            new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
        public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
        public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

        private int ColumnCount(double width)
        {
            if (double.IsInfinity(width) || width <= 0) return 1;
            var count = (int)((width + ColumnSpacing) / (MinColumnWidth + ColumnSpacing));
            return Math.Clamp(count, 1, Math.Max(1, MaxColumns));
        }

        private double ColumnWidth(double width, int columns) =>
            double.IsInfinity(width) ? MinColumnWidth : Math.Max(0, (width - ColumnSpacing * (columns - 1)) / columns);

        protected override Size MeasureOverride(Size availableSize)
        {
            var columns = ColumnCount(availableSize.Width);
            var columnWidth = ColumnWidth(availableSize.Width, columns);
            var heights = new double[columns];

            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(columnWidth, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed) continue;
                var shortest = Array.IndexOf(heights, heights.Min());
                heights[shortest] += child.DesiredSize.Height;
            }

            var width = double.IsInfinity(availableSize.Width) ? columnWidth * columns + ColumnSpacing * (columns - 1) : availableSize.Width;
            return new Size(width, heights.Max());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var columns = ColumnCount(finalSize.Width);
            var columnWidth = ColumnWidth(finalSize.Width, columns);
            var heights = new double[columns];

            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new Rect());
                    continue;
                }
                var shortest = Array.IndexOf(heights, heights.Min());
                var x = shortest * (columnWidth + ColumnSpacing);
                child.Arrange(new Rect(x, heights[shortest], columnWidth, child.DesiredSize.Height));
                heights[shortest] += child.DesiredSize.Height;
            }

            return finalSize;
        }
    }
}
