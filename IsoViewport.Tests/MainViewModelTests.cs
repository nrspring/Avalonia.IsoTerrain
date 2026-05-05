using IsoViewport.Demo.ViewModels;
using Avalonia.Input;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task RealisticPresetCommandUpdatesTileMapDimensions()
    {
        var viewModel = new MainViewModel();

        Assert.Equal(MiniMapLocation.BottomRight, viewModel.MiniMapLocation);
        Assert.Equal(ViewProjectionMode.Isometric, viewModel.ViewProjectionMode);
        Assert.False(viewModel.IsTopDownView);
        Assert.Equal("2500x2500", viewModel.MapDimensions);

        await viewModel.LoadRealisticWorldCommand.ExecuteAsync(null);
        Assert.Equal("2500x2500", viewModel.MapDimensions);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public void HandleTileClickCommandUpdatesTileAndHoverText()
    {
        var viewModel = new MainViewModel
        {
            TileMap = TileMapPresets.Flat(4, 4, (byte)TileType.Grass),
            ObjectLayer = new ObjectLayer(),
            HoveredTile = (2, 1),
        };

        viewModel.HandleTileClickCommand.Execute(new TileClickedEventArgs(2, 1, MouseButton.Left));

        Assert.NotNull(viewModel.TileMap);
        Assert.Equal((byte)TileType.Sand, viewModel.TileMap!.TileType[1, 2]);
        Assert.Equal($"Hover: (2, 1) Sand elev {TileMap.LandMinElevation}", viewModel.HoveredTileText);
    }

    [Fact]
    public void RightClickTogglesUnitObject()
    {
        var viewModel = new MainViewModel
        {
            TileMap = TileMapPresets.Flat(4, 4),
            ObjectLayer = new ObjectLayer(),
        };

        viewModel.HandleTileClickCommand.Execute(new TileClickedEventArgs(1, 2, MouseButton.Right));
        Assert.Equal(1, viewModel.ObjectCount);
        Assert.True(viewModel.ObjectLayer!.Contains(1, 2, (byte)ObjectType.Unit));

        viewModel.HandleTileClickCommand.Execute(new TileClickedEventArgs(1, 2, MouseButton.Right));
        Assert.Equal(0, viewModel.ObjectCount);
        Assert.False(viewModel.ObjectLayer!.Contains(1, 2, (byte)ObjectType.Unit));
    }

    [Fact]
    public void RotateCommandsWrapCameraAngle()
    {
        var viewModel = new MainViewModel();

        viewModel.RotateRightCommand.Execute(null);
        Assert.Equal(90f, viewModel.CameraRotationDegrees);

        viewModel.RotateLeftCommand.Execute(null);
        Assert.Equal(0f, viewModel.CameraRotationDegrees);

        viewModel.RotateLeftCommand.Execute(null);
        Assert.Equal(270f, viewModel.CameraRotationDegrees);
    }

    [Fact]
    public void TopDownToggleTracksProjectionMode()
    {
        var viewModel = new MainViewModel();

        viewModel.IsTopDownView = true;
        Assert.Equal(ViewProjectionMode.TopDown, viewModel.ViewProjectionMode);
        Assert.True(viewModel.IsTopDownView);

        viewModel.ViewProjectionMode = ViewProjectionMode.Isometric;
        Assert.False(viewModel.IsTopDownView);
    }
}
