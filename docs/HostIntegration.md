# IsoViewport Host Integration

This guide describes the public integration surface for host projects that want to render a fixed terrain map plus project-specific pieces and tile highlights.

Use `IsoViewport.Controls.Controls.IsoViewport` as the only public viewer control. The lower-level controls remain in the library because the wrapper composes them, but new host projects should not build their own viewport by layering `IsoTileControl`, `IsoInputOverlay`, `TopoLabelOverlay`, and `MiniMapControl`.

## Setup Model

Setup is intentionally strict:

1. Assign `PieceTypeDefinitions`.
2. Assign `TileMap`.
3. After `TileMap` is assigned, setup locks.
4. Assign, replace, clear, or mutate `Pieces` and `TileHighlights` as runtime state.

`PieceTypeDefinitions` may be empty, but it must be assigned before `TileMap`. `TileMap` may not be cleared, replaced, or modified after setup locks. Type definitions may not be replaced or modified after setup locks.

Runtime collections are different. `Pieces` and `TileHighlights` may be null, replaced, or mutated after setup locks. The control treats null as an empty collection. Observable collections and observable item properties are tracked, and updates must happen on the owner UI thread.

If a host needs to load a different map, create a new `IsoViewport` instance for that map. The current v1 control does not support clearing, unloading, or reusing an instance for a different terrain map.

## XAML Binding Example

Declare `PieceTypeDefinitions` before `TileMap` so setup data reaches the control in the expected order.

```xml
<viewer:IsoViewport
    xmlns:viewer="using:IsoViewport.Controls.Controls"
    PieceTypeDefinitions="{Binding PieceTypeDefinitions}"
    TileMap="{Binding TileMap}"
    Pieces="{Binding Pieces}"
    TileHighlights="{Binding TileHighlights}"
    CameraZoom="{Binding CameraZoom, Mode=TwoWay}"
    CameraPanX="{Binding CameraPanX, Mode=TwoWay}"
    CameraPanY="{Binding CameraPanY, Mode=TwoWay}"
    CameraRotationDegrees="{Binding CameraRotationDegrees, Mode=TwoWay}"
    ViewProjectionMode="{Binding ViewProjectionMode, Mode=TwoWay}"
    RenderMode="{Binding RenderMode, Mode=TwoWay}"
    AnimationsEnabled="{Binding AnimationsEnabled, Mode=TwoWay}"
    IsMiniMapVisible="{Binding IsMiniMapVisible, Mode=TwoWay}"
    MiniMapLocation="{Binding MiniMapLocation, Mode=TwoWay}"
    IsSetupLocked="{Binding IsSetupLocked, Mode=OneWayToSource}"
    HoveredTile="{Binding HoveredTile, Mode=OneWayToSource}"
    TileHoverCommand="{Binding HandleTileHoverCommand}"
    TileClickCommand="{Binding HandleTileClickCommand}"
    VisibleTiles="{Binding VisibleTiles, Mode=OneWayToSource}"
    VertexCount="{Binding VertexCount, Mode=OneWayToSource}"
    VisibleChunks="{Binding VisibleChunks, Mode=OneWayToSource}"
    RenderedTiles="{Binding RenderedTiles, Mode=OneWayToSource}"
    Fps="{Binding Fps, Mode=OneWayToSource}" />
```

## View Model Example

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;

public sealed partial class ViewerViewModel : ObservableObject
{
    public IReadOnlyList<IMapPieceTypeDefinition> PieceTypeDefinitions { get; } =
    [
        new ObservableMapPieceTypeDefinition("unit", "Army Unit", 100, new UnitRenderer()),
        new ObservableMapPieceTypeDefinition("bridge", "Bridge", 10, NullMapPieceRenderer.Instance),
    ];

    public TileMap TileMap { get; } = TileMapPresets.Island(64, 64);

    public ObservableCollection<IMapPiece> Pieces { get; } = [];

    public ObservableCollection<ITileHighlight> TileHighlights { get; } = [];

    [ObservableProperty]
    private TileCoordinate? _hoveredTile;

    [ObservableProperty]
    private bool _isSetupLocked;

    [ObservableProperty]
    private float _cameraZoom = 1f;

    [ObservableProperty]
    private float _cameraPanX;

    [ObservableProperty]
    private float _cameraPanY;

    [ObservableProperty]
    private float _cameraRotationDegrees;

    [ObservableProperty]
    private ViewProjectionMode _viewProjectionMode = ViewProjectionMode.ThreeD;

    [ObservableProperty]
    private TerrainRenderMode _renderMode = TerrainRenderMode.Voxel;

    [ObservableProperty]
    private bool _animationsEnabled = true;

    [RelayCommand]
    private void HandleTileHover(TileHoverCommandParameter? args)
    {
        if (args is null)
        {
            return;
        }

        TileHighlights.Clear();
        TileHighlights.Add(new ObservableTileHighlight(args.Tile, Colors.DeepSkyBlue));
    }

    [RelayCommand]
    private void HandleTileClick(TileClickCommandParameter? args)
    {
        if (args is null || args.Button != MouseButton.Left)
        {
            return;
        }

        Pieces.Add(new ObservableMapPiece(
            $"unit-{Pieces.Count + 1}",
            "unit",
            args.Tile)
        {
            Metadata = new Dictionary<string, string> { ["faction"] = "blue" },
        });
    }
}

public sealed class UnitRenderer : IMapPieceRenderer
{
    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
        var color = piece.Metadata is not null &&
            piece.Metadata.TryGetValue("faction", out var faction) &&
            faction == "blue"
                ? Color.FromRgb(80, 146, 238)
                : Color.FromRgb(140, 145, 152);
        var brush = new SolidColorBrush(color);
        var radius = Math.Clamp(context.TileBounds.Width * 0.18, 5, 14);

        context.DrawingContext.DrawEllipse(
            brush,
            null,
            context.TileTopCenter,
            radius,
            radius * 0.7);
    }
}
```

## Contract Summary

`IMapPieceTypeDefinition` defines a project-specific renderable type:

- `TypeId`: stable id referenced by piece instances
- `DisplayName`: diagnostics-friendly name
- `DefaultZLayer`: same-tile ordering value
- `Renderer`: synchronous `IMapPieceRenderer`

`IMapPiece` defines one runtime piece instance:

- `Id`: unique host-owned id
- `TypeId`: type definition id
- `Tile`: tile-level placement
- `ZLayerOverride`: optional per-instance z-layer
- `IsVisible`: false hides rendering but does not skip validation
- `Orientation`: `Degrees0`, `Degrees90`, `Degrees180`, or `Degrees270`
- `Metadata`: optional `IReadOnlyDictionary<string, string>` for host-defined rendering hints

`ITileHighlight` defines one host-owned tile overlay:

- `Tile`: highlighted tile coordinate
- `Color`: base highlight color; the control derives fill/ring visuals internally

`TileHoverCommandParameter` includes `Tile` and `KeyModifiers`. Hover commands execute when the hovered tile changes, or when modifiers change while still hovering the same tile. Clearing hover sets `HoveredTile` to null but does not execute the hover command.

`TileClickCommandParameter` includes `Tile`, `Button`, and `KeyModifiers`. Click commands are reported at tile level; the host decides what, if anything, is on that tile.

## Renderer Responsibilities

Each piece type has one renderer. The renderer may inspect piece metadata and render different visual details internally, but the viewport only uses `TypeId` to choose the renderer.

`IMapPieceRenderContext` gives a renderer:

- raw Avalonia `DrawingContext`
- current `Tile`, `TileType`, and `TileElevation`
- current `RenderMode` and `ProjectionMode`
- camera zoom, pan, and rotation values
- projected tile top corners, center, and bounds
- helper methods for projecting tile points and reading projected bounds for other tiles

Renderers should be synchronous and deterministic. They should draw within the visual footprint of the piece's base tile for v1. They should not own game state, request redraws, mutate viewport collections, perform pathfinding, or interpret game rules.

Pieces render in `Voxel` and `ShadedRelief` terrain modes. They do not render in `Heat` or `Topographical` modes in v1.

## Ordering And Validation

The viewport validates runtime data promptly:

- piece ids and type ids must be non-empty
- piece type ids must exist in `PieceTypeDefinitions`
- piece and highlight coordinates must be inside the assigned map
- duplicate piece ids are allowed in a source collection, with the newest entry replacing the older one
- duplicate highlight tiles are allowed in a source collection, with the newest entry replacing the older one
- invalid runtime data throws `IsoViewportValidationException`
- setup lifecycle violations throw `IsoViewportSetupException`
- renderer failures are wrapped in `IsoViewportRendererException` and preserve the original exception as `InnerException`

Pieces are sorted by projected tile depth and effective z-layer. The effective z-layer is `ZLayerOverride` when present, otherwise the type definition's `DefaultZLayer`. Higher/front projected tiles obscure lower/back tiles through render ordering, and higher z-layers draw later within a tile.

## MiniMap

The minimap is terrain-only in v1. It follows camera pan/rotation and displays the assigned terrain map, but it does not render project-specific pieces or tile highlights.

## Harness

Run the dedicated harness:

```powershell
dotnet run --project IsoViewport.Harness\IsoViewport.Harness.csproj
```

The harness is the recommended manual integration reference. It assigns type definitions and one terrain map, then uses scenario presets to update runtime collections:

- empty runtime
- small tactical placement
- stacked bridge/unit
- dense unit spread
- dense highlights
- water/boat placement
- invalid-data diagnostics
- 1,000-piece performance target
- 500-highlight performance target
- mixed 1,000-piece/500-highlight target

The harness toolbar also exercises render modes, top-down/3D projection, animation toggle, minimap visibility/location, hover highlight behavior, selected piece type, selected piece rotation, selected visibility, metadata-driven faction rendering, null runtime collections, reassigned runtime collections, and validation diagnostics.

## Migration Notes

New host projects should bind to `IsoViewport.Controls.Controls.IsoViewport`.

Existing code that directly layers `IsoTileControl`, `IsoInputOverlay`, `TopoLabelOverlay`, and `MiniMapControl` should move to the wrapper when it is a viewer for a finalized map. Bind shared camera, minimap, diagnostics, hover, click, piece, and highlight state on the wrapper instead of wiring each lower-level control independently.

The NWorld world-generation app is still an editor/generator surface that repeatedly replaces terrain maps and mutates terrain. That workflow conflicts with the v1 wrapper's fixed-map setup lock, so it should remain on lower-level terrain/editor controls until it has a viewer-only surface or a control-recreation flow for finalized maps.

## V1 Non-Goals

The following are intentionally out of scope for v1:

- multi-tile piece footprints
- off-tile or fractional placement
- sprite-only piece definitions
- piece-shape hit-testing
- drag interaction
- double-click interaction
- animated or pulsing highlights
- minimap rendering of pieces or highlights
- exporting snapshots that include project-specific pieces or highlights
