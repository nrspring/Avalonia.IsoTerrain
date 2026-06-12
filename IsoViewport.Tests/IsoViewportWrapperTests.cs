using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Avalonia.Input;
using Xunit;
using ViewerControl = IsoViewport.Controls.Controls.IsoViewport;

namespace IsoViewport.Tests;

public sealed class IsoViewportWrapperTests
{
    [Fact]
    public void WrapperComposesExistingViewerControls()
    {
        var viewport = new ViewerControl();

        Assert.Single(viewport.Children.OfType<IsoTileControl>());
        Assert.Single(viewport.Children.OfType<IsoInputOverlay>());
        Assert.Single(viewport.Children.OfType<TopoLabelOverlay>());
        Assert.Single(viewport.Children.OfType<MiniMapControl>());
    }

    [Fact]
    public void WrapperPushesStateToInternalControls()
    {
        var map = TileMapPresets.Flat(4, 5);
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = Array.Empty<IMapPieceTypeDefinition>(),
            TileMap = map,
            CameraZoom = 1.75f,
            CameraPanX = 12f,
            CameraPanY = 24f,
            CameraRotationDegrees = 90f,
            ViewProjectionMode = ViewProjectionMode.TopDown,
            RenderMode = TerrainRenderMode.Heat,
            AnimationsEnabled = false,
            IsMiniMapVisible = false,
            MiniMapLocation = MiniMapLocation.TopLeft,
        };

        var terrain = viewport.Children.OfType<IsoTileControl>().Single();
        var input = viewport.Children.OfType<IsoInputOverlay>().Single();
        var labels = viewport.Children.OfType<TopoLabelOverlay>().Single();
        var miniMap = viewport.Children.OfType<MiniMapControl>().Single();

        Assert.Same(map, terrain.TileMap);
        Assert.Same(map, input.TileMap);
        Assert.Same(map, labels.TileMap);
        Assert.Same(map, miniMap.TileMap);
        Assert.Equal(1.75f, terrain.CameraZoom);
        Assert.Equal(1.75f, input.CameraZoom);
        Assert.Equal(1.75f, miniMap.CameraZoom);
        Assert.Equal(12f, input.CameraPanX);
        Assert.Equal(24f, input.CameraPanY);
        Assert.Equal(90f, labels.CameraRotationDegrees);
        Assert.Equal(ViewProjectionMode.TopDown, terrain.ViewProjectionMode);
        Assert.Equal(TerrainRenderMode.Heat, terrain.RenderMode);
        Assert.False(input.AnimationsEnabled);
        Assert.False(miniMap.IsVisible);
        Assert.Equal(MiniMapLocation.TopLeft, miniMap.Location);
    }

    [Fact]
    public void WrapperReceivesStateFromInputOverlay()
    {
        var viewport = new ViewerControl();
        var input = viewport.Children.OfType<IsoInputOverlay>().Single();

        input.CameraZoom = 2f;
        input.CameraPanX = 18f;
        input.CameraPanY = 27f;
        viewport.HandleInputTileHovered((3, 4), KeyModifiers.None);

        Assert.Equal(2f, viewport.CameraZoom);
        Assert.Equal(18f, viewport.CameraPanX);
        Assert.Equal(27f, viewport.CameraPanY);
        Assert.Equal(new TileCoordinate(4, 3), viewport.HoveredTile);
    }
}
