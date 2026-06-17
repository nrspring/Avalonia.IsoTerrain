using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Harness.Rendering;

internal sealed class SampleUnitRenderer : IMapPieceRenderer
{
    public static SampleUnitRenderer Instance { get; } = new();

    private static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromRgb(27, 34, 42)), 1.5);
    private static readonly IBrush BlueBrush = new SolidColorBrush(Color.FromRgb(80, 146, 238));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.FromRgb(220, 76, 68));
    private static readonly IBrush GoldBrush = new SolidColorBrush(Color.FromRgb(236, 188, 73));

    private SampleUnitRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var center = context.TileTopCenter;
        var radius = Math.Clamp(context.TileBounds.Width * 0.18d, 6d, 13d);
        var brush = GetFactionBrush(piece);

        context.DrawingContext.DrawEllipse(brush, Stroke, center, radius, radius * 0.72d);
        context.DrawingContext.DrawEllipse(null, Stroke, new Point(center.X, center.Y - (radius * 0.85d)), radius * 0.42d, radius * 0.42d);
    }

    private static IBrush GetFactionBrush(IMapPiece piece)
    {
        if (piece.Metadata is null || !piece.Metadata.TryGetValue("faction", out var faction))
        {
            return BlueBrush;
        }

        return faction.ToUpperInvariant() switch
        {
            "RED" => RedBrush,
            "GOLD" => GoldBrush,
            _ => BlueBrush,
        };
    }
}
