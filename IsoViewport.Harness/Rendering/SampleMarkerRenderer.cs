using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Harness.Rendering;

internal sealed class SampleMarkerRenderer : IMapPieceRenderer
{
    public static SampleMarkerRenderer Instance { get; } = new();

    private static readonly IBrush Fill = new SolidColorBrush(Color.FromRgb(244, 210, 82));
    private static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromRgb(61, 46, 12)), 1.5);

    private SampleMarkerRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var center = context.TileTopCenter;
        var radius = Math.Clamp(context.TileBounds.Width * 0.14d, 4d, 10d);
        context.DrawingContext.DrawEllipse(Fill, Stroke, center, radius, radius);
        context.DrawingContext.DrawLine(Stroke, center, new Point(center.X, center.Y - (radius * 2.2d)));
    }
}
