using System.Windows;
using System.Windows.Media;

namespace FikaHeadlessManager.Controls;

/// <summary>
/// Renders a lightweight filled line chart without external chart dependencies.
/// </summary>
public sealed class AreaChart : FrameworkElement
{
    /// <summary>Identifies the <see cref="Values"/> dependency property.</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(AreaChart),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the ordered values drawn by the chart.</summary>
    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        const double left = 42;
        const double top = 12;
        const double right = 12;
        const double bottom = 28;
        var plot = new Rect(left, top, Math.Max(1, bounds.Width - left - right), Math.Max(1, bounds.Height - top - bottom));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(238, 240, 243)), 1);
        gridPen.Freeze();

        for (var line = 0; line <= 4; line++)
        {
            var y = plot.Top + (plot.Height * line / 4);
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        if (Values.Count == 0)
        {
            DrawEmptyState(drawingContext, plot);
            return;
        }

        var max = Math.Max(1, Values.Max());
        var points = Values.Select((value, index) => new Point(
            plot.Left + (Values.Count == 1 ? plot.Width / 2 : plot.Width * index / (Values.Count - 1)),
            plot.Bottom - Math.Max(0, value) / max * plot.Height)).ToArray();

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(points[0].X, plot.Bottom), true, true);
            context.LineTo(points[0], true, false);
            for (var index = 1; index < points.Length; index++)
            {
                context.LineTo(points[index], true, false);
            }

            context.LineTo(new Point(points[^1].X, plot.Bottom), true, false);
        }

        area.Freeze();
        var fill = new LinearGradientBrush(
            Color.FromArgb(110, 99, 102, 241),
            Color.FromArgb(5, 99, 102, 241),
            new Point(0.5, 0), new Point(0.5, 1));
        fill.Freeze();
        drawingContext.DrawGeometry(fill, null, area);

        var lineGeometry = new StreamGeometry();
        using (var context = lineGeometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            foreach (var point in points.Skip(1))
            {
                context.LineTo(point, true, false);
            }
        }

        lineGeometry.Freeze();
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(79, 70, 229)), 2.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        linePen.Freeze();
        drawingContext.DrawGeometry(null, linePen, lineGeometry);

        foreach (var point in points)
        {
            drawingContext.DrawEllipse(Brushes.White, linePen, point, 3.5, 3.5);
        }

        DrawAxisText(drawingContext, plot, max);
    }

    private void DrawAxisText(DrawingContext drawingContext, Rect plot, double max)
    {
        var typeface = new Typeface("Segoe UI");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var index = 0; index <= 4; index++)
        {
            var value = max - max * index / 4;
            var text = new FormattedText(value.ToString("0.#"), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.FromRgb(156, 163, 175)), pixelsPerDip);
            drawingContext.DrawText(text, new Point(plot.Left - text.Width - 8, plot.Top + plot.Height * index / 4 - text.Height / 2));
        }

        var labels = new[] { "-60s", "-45s", "-30s", "-15s", "Now" };
        for (var index = 0; index < labels.Length; index++)
        {
            var text = new FormattedText(labels[index], System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.FromRgb(156, 163, 175)), pixelsPerDip);
            var x = plot.Left + plot.Width * index / (labels.Length - 1) - text.Width / 2;
            drawingContext.DrawText(text, new Point(x, plot.Bottom + 8));
        }
    }

    private void DrawEmptyState(DrawingContext drawingContext, Rect plot)
    {
        var text = new FormattedText("Waiting for session data", System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12,
            new SolidColorBrush(Color.FromRgb(156, 163, 175)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point(plot.Left + (plot.Width - text.Width) / 2, plot.Top + (plot.Height - text.Height) / 2));
    }
}
