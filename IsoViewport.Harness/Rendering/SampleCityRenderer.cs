using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Harness.Rendering;

internal sealed class SampleCityRenderer : IMapPieceRenderer
{
    public static SampleCityRenderer Instance { get; } = new();

    private static readonly IBrush WallBrush = new SolidColorBrush(Color.FromRgb(183, 190, 184));
    private static readonly IBrush RoofBrush = new SolidColorBrush(Color.FromRgb(120, 54, 44));
    private static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromRgb(54, 60, 58)), 1.2);

    private SampleCityRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var center = context.TileTopCenter;
        var width = Math.Clamp(context.TileBounds.Width * 0.22d, 10d, 20d);
        var height = Math.Clamp(context.TileBounds.Height * 0.42d, 10d, 22d);
        var body = new Rect(center.X - (width * 0.5d), center.Y - height, width, height);
        var roofTop = new Point(center.X, body.Top - (height * 0.45d));

        context.DrawingContext.DrawRectangle(WallBrush, Stroke, body);
        var roof = new StreamGeometry();

        using (var geometry = roof.Open())
        {
            geometry.BeginFigure(roofTop, true);
            geometry.LineTo(new Point(body.Right + 2d, body.Top + 2d));
            geometry.LineTo(new Point(body.Left - 2d, body.Top + 2d));
            geometry.EndFigure(true);
        }

        context.DrawingContext.DrawGeometry(RoofBrush, Stroke, roof);
    }
}
