using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Harness.Rendering;

internal sealed class SampleBoatRenderer : IMapPieceRenderer
{
    public static SampleBoatRenderer Instance { get; } = new();

    private static readonly IBrush HullBrush = new SolidColorBrush(Color.FromRgb(78, 90, 101));
    private static readonly IBrush SailBrush = new SolidColorBrush(Color.FromRgb(238, 241, 232));
    private static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromRgb(31, 40, 48)), 1.4);

    private SampleBoatRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var center = context.TileTopCenter;
        var radius = Math.Clamp(context.TileBounds.Width * 0.16d, 6d, 12d);
        var hull = new Rect(center.X - radius, center.Y - (radius * 0.25d), radius * 2d, radius * 0.7d);

        context.DrawingContext.DrawRectangle(HullBrush, Stroke, hull);

        var sail = new StreamGeometry();

        using (var geometry = sail.Open())
        {
            geometry.BeginFigure(new Point(center.X, center.Y - (radius * 1.75d)), true);
            geometry.LineTo(new Point(center.X + (radius * 0.8d), center.Y - (radius * 0.1d)));
            geometry.LineTo(new Point(center.X, center.Y - (radius * 0.1d)));
            geometry.EndFigure(true);
        }

        context.DrawingContext.DrawGeometry(SailBrush, Stroke, sail);
    }
}
