using System.Windows;
using System.Windows.Media;

namespace FikaHeadlessManager.Controls;

/// <summary>
/// Renders a compact categorical bar chart without external chart dependencies.
/// </summary>
public sealed class BarChart : FrameworkElement
{
    /// <summary>Identifies the <see cref="Values"/> dependency property.</summary>
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(BarChart),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Labels"/> dependency property.</summary>
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IReadOnlyList<string>), typeof(BarChart),
        new FrameworkPropertyMetadata(Array.Empty<string>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gets or sets the category values.</summary>
    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Gets or sets the category labels.</summary>
    public IReadOnlyList<string> Labels
    {
        get => (IReadOnlyList<string>)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0 || Values.Count == 0)
        {
            return;
        }

        const double top = 16;
        const double bottom = 36;
        const double side = 14;
        var plotHeight = Math.Max(1, ActualHeight - top - bottom);
        var plotWidth = Math.Max(1, ActualWidth - side * 2);
        var max = Math.Max(1, Values.Max());
        var slotWidth = plotWidth / Values.Count;
        var barWidth = Math.Min(46, slotWidth * 0.55);
        var colors = new[]
        {
            Color.FromRgb(79, 70, 229), Color.FromRgb(14, 165, 233),
            Color.FromRgb(16, 185, 129), Color.FromRgb(203, 213, 225)
        };
        var typeface = new Typeface("Segoe UI");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var index = 0; index < Values.Count; index++)
        {
            var height = Math.Max(3, Math.Max(0, Values[index]) / max * (plotHeight - 24));
            var x = side + slotWidth * index + (slotWidth - barWidth) / 2;
            var rect = new Rect(x, top + plotHeight - height, barWidth, height);
            var brush = new SolidColorBrush(colors[index % colors.Length]);
            brush.Freeze();
            drawingContext.DrawRoundedRectangle(brush, null, rect, 5, 5);

            var valueText = new FormattedText(Values[index].ToString("0"), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, new SolidColorBrush(Color.FromRgb(55, 65, 81)), pixelsPerDip);
            drawingContext.DrawText(valueText, new Point(x + (barWidth - valueText.Width) / 2, rect.Top - valueText.Height - 5));

            var label = index < Labels.Count ? Labels[index] : string.Empty;
            var labelText = new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.FromRgb(107, 114, 128)), pixelsPerDip);
            drawingContext.DrawText(labelText, new Point(side + slotWidth * index + (slotWidth - labelText.Width) / 2, top + plotHeight + 10));
        }
    }
}
