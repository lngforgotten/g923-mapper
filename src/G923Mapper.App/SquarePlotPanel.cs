using System;
using System.Windows;
using System.Windows.Controls;

namespace G923Mapper.App;

/// <summary>
/// Heading, square plot, note, stacked. The plot (the second child) is as large as the column's width and the height left over allow,
/// so the response graph grows with the window instead of staying one size.
/// </summary>
public sealed class SquarePlotPanel : Panel
{
    private const double Smallest = 140, WithoutALimit = 230;
    private double _side;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 3) return default;
        UIElement head = InternalChildren[0], plot = InternalChildren[1], note = InternalChildren[2];
        double width = double.IsInfinity(available.Width) ? WithoutALimit : available.Width;
        head.Measure(new Size(width, double.PositiveInfinity));
        note.Measure(new Size(width, double.PositiveInfinity));
        double others = head.DesiredSize.Height + note.DesiredSize.Height;
        double room = double.IsInfinity(available.Height) ? width : available.Height - others;
        _side = Math.Min(width, Math.Max(Smallest, room));
        plot.Measure(new Size(_side, _side));
        return new Size(_side, others + _side);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count != 3) return final;
        UIElement head = InternalChildren[0], plot = InternalChildren[1], note = InternalChildren[2];
        double y = 0;
        head.Arrange(new Rect(0, y, final.Width, head.DesiredSize.Height));
        y += head.DesiredSize.Height;
        plot.Arrange(new Rect(0, y, _side, _side));
        y += _side;
        note.Arrange(new Rect(0, y, final.Width, note.DesiredSize.Height));
        return final;
    }
}
