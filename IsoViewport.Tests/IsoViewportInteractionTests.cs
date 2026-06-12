using System.Windows.Input;
using Avalonia.Input;
using IsoViewport.Controls.Contracts;
using Xunit;
using ViewerControl = IsoViewport.Controls.Controls.IsoViewport;

namespace IsoViewport.Tests;

public sealed class IsoViewportInteractionTests
{
    [Fact]
    public void HoverCommandRunsAfterHoveredTileUpdates()
    {
        var viewport = new ViewerControl();
        TileHoverCommandParameter? captured = null;
        viewport.TileHoverCommand = new RecordingCommand<TileHoverCommandParameter>(parameter =>
        {
            captured = parameter;
            Assert.Equal(parameter.Tile, viewport.HoveredTile);
        });

        viewport.HandleInputTileHovered((3, 4), KeyModifiers.Shift);

        Assert.Equal(new TileCoordinate(4, 3), viewport.HoveredTile);
        Assert.NotNull(captured);
        Assert.Equal(new TileCoordinate(4, 3), captured.Tile);
        Assert.Equal(KeyModifiers.Shift, captured.KeyModifiers);
    }

    [Fact]
    public void HoverCommandRunsWhenModifiersChangeForSameTile()
    {
        var viewport = new ViewerControl();
        var calls = new List<TileHoverCommandParameter>();
        viewport.TileHoverCommand = new RecordingCommand<TileHoverCommandParameter>(calls.Add);

        viewport.HandleInputTileHovered((3, 4), KeyModifiers.None);
        viewport.HandleInputTileHovered((3, 4), KeyModifiers.Control);

        Assert.Equal(2, calls.Count);
        Assert.Equal(KeyModifiers.None, calls[0].KeyModifiers);
        Assert.Equal(KeyModifiers.Control, calls[1].KeyModifiers);
    }

    [Fact]
    public void HoverClearDoesNotRunHoverCommand()
    {
        var viewport = new ViewerControl();
        var calls = 0;
        viewport.TileHoverCommand = new RecordingCommand<TileHoverCommandParameter>(_ => calls++);

        viewport.HandleInputTileHovered((3, 4), KeyModifiers.None);
        viewport.HandleInputTileHovered(null, KeyModifiers.None);

        Assert.Null(viewport.HoveredTile);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ClickCommandReceivesTileButtonAndModifiers()
    {
        var viewport = new ViewerControl();
        TileClickCommandParameter? captured = null;
        viewport.TileClickCommand = new RecordingCommand<TileClickCommandParameter>(parameter => captured = parameter);

        viewport.HandleInputTileClicked(new TileCoordinate(2, 1), MouseButton.Middle, KeyModifiers.Alt);

        Assert.Equal(new TileCoordinate(2, 1), viewport.HoveredTile);
        Assert.NotNull(captured);
        Assert.Equal(new TileCoordinate(2, 1), captured.Tile);
        Assert.Equal(MouseButton.Middle, captured.Button);
        Assert.Equal(KeyModifiers.Alt, captured.KeyModifiers);
    }

    private sealed class RecordingCommand<T> : ICommand
    {
        private readonly Action<T> _execute;

        public RecordingCommand(Action<T> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is T;
        }

        public void Execute(object? parameter)
        {
            _execute(Assert.IsType<T>(parameter));
        }
    }
}
