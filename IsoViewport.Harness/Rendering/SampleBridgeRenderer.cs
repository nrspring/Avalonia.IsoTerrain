using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Harness.Rendering;

internal sealed class SampleBridgeRenderer : IMapPieceRenderer
{
    public static SampleBridgeRenderer Instance { get; } = new();

    private static readonly IPen ShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 36, 25, 16)), 9);
    private static readonly IPen DeckPen = new Pen(new SolidColorBrush(Color.FromRgb(137, 101, 62)), 7);
    private static readonly IPen RailPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 199, 151)), 2);

    private SampleBridgeRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var corners = context.TileTopCorners;
        var horizontal = piece.Orientation is PieceOrientation.Degrees0 or PieceOrientation.Degrees180;
        var a = horizontal ? corners[3] : corners[0];
        var b = horizontal ? corners[1] : corners[2];

        context.DrawingContext.DrawLine(ShadowPen, a, b);
        context.DrawingContext.DrawLine(DeckPen, a, b);
        context.DrawingContext.DrawLine(RailPen, a, b);
    }
}
