using System.Windows;
using System.Windows.Media;

namespace DSCons.RevitMcp.Core;

/// <summary>Small vector icons keep the loader self-contained; no DSCons MEP Tools assets are reused.</summary>
internal static class McpRibbonImages
{
    private static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(24, 66, 112));
    private static readonly Brush Cyan = new SolidColorBrush(Color.FromRgb(63, 190, 210));
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(42, 168, 98));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(112, 122, 132));
    private static readonly Brush White = Brushes.White;

    public static ImageSource Reload() => Draw(context =>
    {
        var pen = new Pen(Cyan, 2.2);
        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(new Point(12.5, 4), false, false);
            geometry.ArcTo(new Point(4, 11.5), new Size(5, 5), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        context.DrawGeometry(null, pen, arc);
        var arrow = new StreamGeometry();
        using (var geometry = arrow.Open())
        {
            geometry.BeginFigure(new Point(12.5, 4), true, true);
            geometry.LineTo(new Point(8.8, 4), true, false);
            geometry.LineTo(new Point(12.5, 7.2), true, false);
        }
        context.DrawGeometry(Cyan, null, arrow);
        context.DrawEllipse(Navy, new Pen(White, 1), new Point(8, 8), 5.5, 5.5);
    });

    public static ImageSource Power(bool running) => Draw(context =>
    {
        var color = running ? Green : Gray;
        var pen = new Pen(color, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.DrawLine(pen, new Point(8, 1.8), new Point(8, 7.5));
        var arc = new StreamGeometry();
        using (var geometry = arc.Open())
        {
            geometry.BeginFigure(new Point(4.5, 4.2), false, false);
            geometry.ArcTo(new Point(11.5, 4.2), new Size(5, 5), 0, true, SweepDirection.Counterclockwise, true, false);
        }
        context.DrawGeometry(null, pen, arc);
        context.DrawEllipse(null, new Pen(Navy, 0.8), new Point(8, 8), 6.5, 6.5);
    });

    public static ImageSource Chat() => Draw(context =>
    {
        var bubble = new StreamGeometry();
        using (var geometry = bubble.Open())
        {
            geometry.BeginFigure(new Point(2, 3), true, true);
            geometry.LineTo(new Point(14, 3), true, false);
            geometry.LineTo(new Point(14, 11), true, false);
            geometry.LineTo(new Point(8, 11), true, false);
            geometry.LineTo(new Point(5, 14), true, false);
            geometry.LineTo(new Point(5, 11), true, false);
            geometry.LineTo(new Point(2, 11), true, false);
        }
        context.DrawGeometry(Navy, new Pen(Cyan, 1), bubble);
        context.DrawEllipse(White, null, new Point(5, 7), 1, 1);
        context.DrawEllipse(White, null, new Point(8, 7), 1, 1);
        context.DrawEllipse(White, null, new Point(11, 7), 1, 1);
    });

    private static ImageSource Draw(Action<DrawingContext> draw)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) draw(context);
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
