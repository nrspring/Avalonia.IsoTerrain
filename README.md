> Note
> This project was vibe coded, so I can't take credit for writing the code itself. My contribution was in defining the requirements, steering the iteration, and testing the results.
>
> I run and test this on an NVIDIA GeForce RTX 4090 with 16 GB of VRAM, so your mileage may vary on different hardware.
# IsoViewport

`IsoViewport` is a .NET 10 / Avalonia 12 terrain viewer focused on large tile maps, isometric rendering, and smooth camera interaction.

It currently includes:

- isometric and top-down projection modes
- terrain, heat-map, and topographical render modes
- chunked OpenGL terrain rendering via `Silk.NET.OpenGL`
- animated deep/shallow water rendering
- minimap, tile picking, camera pan / zoom / rotation, and hover support
- project-specific map pieces rendered through host-provided renderers
- host-owned tile highlights for hover, selection, movement ranges, and similar overlays
- large-map performance work including far-zoom LOD rendering
- an Avalonia demo app, a focused viewer harness app, and xUnit coverage for core math, batching, overlays, and view-model behavior

## Public Viewer API

Host projects should integrate through `IsoViewport.Controls.Controls.IsoViewport`.
That wrapper is the public control surface for terrain, input, minimap settings, tile interaction commands, project-specific pieces, tile highlights, and render diagnostics.

The lower-level controls such as `IsoTileControl`, `IsoInputOverlay`, `TopoLabelOverlay`, and `MiniMapControl` are still present because the wrapper composes them internally and the terrain demo exercises renderer internals. New host projects should not bind to those lower-level controls directly.

See [HostIntegration.md](docs/HostIntegration.md) for setup order, MVVM binding examples, renderer responsibilities, and migration notes.

## Screenshots

### Terrain

![Isometric terrain view](docs/images/terrain-isometric.jpg)

### Terrain Close-Up

![Zoomed-in isometric terrain view](docs/images/terrain-isometric-closeup.jpg)

### Heat Map

![Heat map view](docs/images/terrain-heat.jpg)

### Topographical

![Topographical view](docs/images/terrain-topographical.jpg)

## Solution Layout

- `IsoViewport.Controls`
  Rendering control library with the public `IsoViewport` wrapper, terrain renderer, overlays, batching, camera math, map types, contracts, and helpers.

- `IsoViewport.Demo`
  Desktop terrain demo app for exercising renderer presets, render modes, terrain editing behavior, and debug stats.

- `IsoViewport.Harness`
  Focused manual test harness for validating the public wrapper, setup lifecycle, pieces, highlights, tile commands, minimap settings, diagnostics, and performance presets.

- `IsoViewport.Tests`
  xUnit coverage for renderer math, chunking, batching, colors, overlays, and demo view-model logic.

## Requirements

- .NET 10 SDK
- Windows desktop environment for the Avalonia demo
- GPU / driver support for the Avalonia OpenGL control path

## Run

```powershell
dotnet restore IsoViewport.sln --configfile NuGet.Config
dotnet build IsoViewport.sln --no-restore
dotnet run --project IsoViewport.Demo\IsoViewport.Demo.csproj --no-build
```

Run the focused viewer harness:

```powershell
dotnet run --project IsoViewport.Harness\IsoViewport.Harness.csproj
```

The harness starts with a single assigned terrain map, then scenario presets change the dynamic piece and highlight collections. Use it as the public integration example.

Run tests:

```powershell
dotnet test IsoViewport.Tests\IsoViewport.Tests.csproj
```

## Demo Controls

These controls apply to the terrain demo app:

- `WASD` / arrow keys: pan
- `Q` / `E`: rotate view
- mouse wheel / `+` / `-`: zoom
- `R`: reset camera
- `Space`: toggle animation
- left click: cycle tile type
- right click: toggle a unit on the tile
- right drag or middle drag: pan

## Harness Checks

Use `IsoViewport.Harness` when validating host integration behavior:

- choose scenario presets for empty runtime, tactical placement, stacked bridge/unit, dense units, dense highlights, water/boats, invalid-data diagnostics, and performance targets
- use render/projection/minimap controls to verify pieces and highlights in the supported modes
- left click places the selected piece type and toggles a host-owned selection highlight
- `Null Runtime` and `Assign Runtime` verify that `Pieces` and `TileHighlights` may be null, replaced, or reassigned after setup is locked
- diagnostic text shows setup lock state, binding state, piece/highlight counts, hover/click state, FPS, visible chunks, rendered tiles, and renderer errors

## Notes

- The repo currently targets a preview .NET 10 SDK, so `NETSDK1057` warnings are expected.
- Avalonia currently pulls in a transitive advisory warning for `Tmds.DBus.Protocol` in this environment.
- The public wrapper treats the assigned terrain map and piece type definitions as setup data. Assign type definitions first, assign the map once, then update pieces and highlights through runtime collections.
