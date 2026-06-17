using Avalonia.Input;
using IsoViewport.Controls.Contracts;
using IsoViewport.Harness.ViewModels;
using Xunit;

namespace IsoViewport.Tests;

public sealed class HarnessViewModelTests
{
    [Fact]
    public void ConstructorStartsWithAssignedRuntimeCollectionsAndScenario()
    {
        var viewModel = new MainViewModel();

        Assert.NotNull(viewModel.TileMap);
        Assert.NotNull(viewModel.Pieces);
        Assert.NotNull(viewModel.TileHighlights);
        Assert.Equal("Small tactical map", viewModel.ScenarioName);
        Assert.Contains("runtime collections assigned", viewModel.SetupStatusText);
    }

    [Fact]
    public void StackedScenarioCreatesBridgeAndUnitOnSameTile()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedScenarioPreset = viewModel.ScenarioPresets.Single(scenario => scenario.Id == "stacked");

        viewModel.ApplySelectedScenarioCommand.Execute(null);

        var pieces = viewModel.Pieces!.Cast<IMapPiece>().ToArray();
        Assert.Equal(2, pieces.Count(piece => piece.Tile == new TileCoordinate(10, 10)));
        Assert.Contains(pieces, piece => piece.TypeId == "bridge");
        Assert.Contains(pieces, piece => piece.TypeId == "unit");
        Assert.True(viewModel.HighlightCount > 0);
    }

    [Fact]
    public void ApplyingScenarioDoesNotReplaceAssignedMap()
    {
        var viewModel = new MainViewModel();
        var assignedMap = viewModel.TileMap;
        viewModel.SelectedScenarioPreset = viewModel.ScenarioPresets.Single(scenario => scenario.Id == "perf-mixed");

        viewModel.ApplySelectedScenarioCommand.Execute(null);

        Assert.Same(assignedMap, viewModel.TileMap);
        Assert.Equal(1_000, viewModel.PieceCount);
        Assert.Equal(500, viewModel.HighlightCount);
    }

    [Fact]
    public void RuntimeCollectionsCanBeSetNullAndAssignedLater()
    {
        var viewModel = new MainViewModel();

        viewModel.UseNullRuntimeCollectionsCommand.Execute(null);

        Assert.Null(viewModel.Pieces);
        Assert.Null(viewModel.TileHighlights);
        Assert.Contains("null", viewModel.SetupStatusText);

        viewModel.AssignRuntimeCollectionsCommand.Execute(null);

        Assert.NotNull(viewModel.Pieces);
        Assert.NotNull(viewModel.TileHighlights);
        Assert.Contains("assigned", viewModel.SetupStatusText);
    }

    [Fact]
    public void ClickWorkflowPlacesPieceAndSelectionHighlight()
    {
        var viewModel = new MainViewModel
        {
            SelectedPieceTypeId = "unit",
        };

        viewModel.HandleTileClickCommand.Execute(
            new TileClickCommandParameter(new TileCoordinate(3, 4), MouseButton.Left, KeyModifiers.None));

        Assert.Contains(viewModel.Pieces!.Cast<IMapPiece>(), piece => piece.Tile == new TileCoordinate(3, 4) && piece.TypeId == "unit");
        Assert.True(viewModel.HighlightCount > 0);
        Assert.Contains("(4, 3)", viewModel.LastClickText);
    }

    [Fact]
    public void InvalidDataDiagnosticCapturesValidationMessage()
    {
        var viewModel = new MainViewModel();

        viewModel.RunInvalidDataDiagnosticCommand.Execute(null);

        Assert.Contains("IsoViewportValidationException", viewModel.RendererErrorText);
        Assert.Contains("missing-type", viewModel.RendererErrorText);
    }

    [Fact]
    public void PerformancePieceScenarioCreatesOneThousandPieces()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedScenarioPreset = viewModel.ScenarioPresets.Single(scenario => scenario.Id == "perf-pieces");

        viewModel.ApplySelectedScenarioCommand.Execute(null);

        Assert.Equal(1_000, viewModel.PieceCount);
        Assert.Equal(0, viewModel.HighlightCount);
        Assert.Contains("pieces 1000", viewModel.DiagnosticsText);
    }

    [Fact]
    public void PerformanceHighlightScenarioCreatesFiveHundredHighlights()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedScenarioPreset = viewModel.ScenarioPresets.Single(scenario => scenario.Id == "perf-highlights");

        viewModel.ApplySelectedScenarioCommand.Execute(null);

        Assert.Equal(0, viewModel.PieceCount);
        Assert.Equal(500, viewModel.HighlightCount);
        Assert.Contains("highlights 500", viewModel.DiagnosticsText);
    }

    [Fact]
    public void MixedPerformanceScenarioCreatesTargetCounts()
    {
        var viewModel = new MainViewModel();
        viewModel.SelectedScenarioPreset = viewModel.ScenarioPresets.Single(scenario => scenario.Id == "perf-mixed");

        viewModel.ApplySelectedScenarioCommand.Execute(null);

        Assert.Equal(1_000, viewModel.PieceCount);
        Assert.Equal(500, viewModel.HighlightCount);
        Assert.Contains("binding assigned", viewModel.DiagnosticsText);
    }
}
