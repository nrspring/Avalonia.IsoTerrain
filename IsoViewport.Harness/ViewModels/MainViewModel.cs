using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using IsoViewport.Harness.Rendering;

namespace IsoViewport.Harness.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int SmallRows = 32;
    private const int SmallCols = 48;
    private const int MediumRows = 120;
    private const int MediumCols = 160;

    private static readonly IReadOnlyList<IMapPieceTypeDefinition> SamplePieceTypes =
    [
        new ObservableMapPieceTypeDefinition("bridge", "Bridge", 10, SampleBridgeRenderer.Instance),
        new ObservableMapPieceTypeDefinition("boat", "Boat", 80, SampleBoatRenderer.Instance),
        new ObservableMapPieceTypeDefinition("unit", "Army Unit", 100, SampleUnitRenderer.Instance),
        new ObservableMapPieceTypeDefinition("marker", "Marker", 200, SampleMarkerRenderer.Instance),
        new ObservableMapPieceTypeDefinition("city", "City", 30, SampleCityRenderer.Instance),
    ];

    private static readonly IReadOnlyList<ScenarioPreset> ScenarioPresetValues =
    [
        new("empty", "Empty map"),
        new("small", "Small tactical map"),
        new("stacked", "Stacked bridge/unit"),
        new("dense-units", "Dense unit spread"),
        new("dense-highlights", "Dense highlights"),
        new("water", "Water and boats"),
        new("invalid", "Invalid-data diagnostics"),
        new("perf-pieces", "Perf: 1,000 pieces"),
        new("perf-highlights", "Perf: 500 highlights"),
        new("perf-mixed", "Perf: mixed target"),
    ];

    private readonly ObservableCollection<IMapPiece> _pieceItems = [];
    private readonly ObservableCollection<ITileHighlight> _tileHighlightItems = [];
    private int _nextPieceIndex = 1;
    private ObservableTileHighlight? _hoverHighlight;
    private ObservableMapPiece? _selectedPiece;

    [ObservableProperty]
    private TileMap? _tileMap;

    [ObservableProperty]
    private IReadOnlyList<IMapPieceTypeDefinition> _pieceTypeDefinitions = SamplePieceTypes;

    [ObservableProperty]
    private IEnumerable? _pieces;

    [ObservableProperty]
    private IEnumerable? _tileHighlights;

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

    [ObservableProperty]
    private bool _isMiniMapVisible = true;

    [ObservableProperty]
    private MiniMapLocation _miniMapLocation = MiniMapLocation.BottomRight;

    [ObservableProperty]
    private int _visibleTiles;

    [ObservableProperty]
    private int _vertexCount;

    [ObservableProperty]
    private int _visibleChunks;

    [ObservableProperty]
    private int _renderedTiles;

    [ObservableProperty]
    private double _fps;

    [ObservableProperty]
    private TileCoordinate? _hoveredTile;

    [ObservableProperty]
    private string _hoveredTileText = "Hover: none";

    [ObservableProperty]
    private string _lastClickText = "Click: none";

    [ObservableProperty]
    private string _scenarioName = "Small tactical map";

    [ObservableProperty]
    private ScenarioPreset _selectedScenarioPreset = ScenarioPresetValues[1];

    [ObservableProperty]
    private bool _isHoverHighlightEnabled = true;

    [ObservableProperty]
    private string _selectedPieceTypeId = "unit";

    [ObservableProperty]
    private string _selectedPieceText = "Selected piece: none";

    [ObservableProperty]
    private bool _isSetupLocked;

    [ObservableProperty]
    private string _setupStatusText = "Setup: type definitions assigned, map assigned, runtime collections assigned";

    [ObservableProperty]
    private string _diagnosticsText = "Diagnostics: ready";

    [ObservableProperty]
    private string _rendererErrorText = "Renderer: none";

    public MainViewModel()
    {
        _pieceItems.CollectionChanged += OnRuntimeCollectionChanged;
        _tileHighlightItems.CollectionChanged += OnRuntimeCollectionChanged;
        Pieces = _pieceItems;
        TileHighlights = _tileHighlightItems;
        TileMap = BuildMixedMap(MediumRows, MediumCols);
        ApplyScenario(SelectedScenarioPreset);
    }

    public IReadOnlyList<TerrainRenderMode> RenderModes { get; } = Enum.GetValues<TerrainRenderMode>();

    public IReadOnlyList<MiniMapLocation> MiniMapLocations { get; } = Enum.GetValues<MiniMapLocation>();

    public IReadOnlyList<ScenarioPreset> ScenarioPresets => ScenarioPresetValues;

    public IReadOnlyList<string> PieceTypeIds { get; } = SamplePieceTypes.Select(definition => definition.TypeId).ToArray();

    public string MapDimensions => TileMap is { } map ? $"{map.Rows}x{map.Cols}" : "No map";

    public int PieceCount => _pieceItems.Count;

    public int VisiblePieceCount => _pieceItems.Count(piece => piece.IsVisible);

    public int HighlightCount => _tileHighlightItems.Count;

    public bool IsTopDownView
    {
        get => ViewProjectionMode == ViewProjectionMode.TopDown;
        set
        {
            var next = value ? ViewProjectionMode.TopDown : ViewProjectionMode.ThreeD;

            if (ViewProjectionMode != next)
            {
                ViewProjectionMode = next;
            }
        }
    }

    [RelayCommand]
    private void ApplySelectedScenario()
    {
        ApplyScenario(SelectedScenarioPreset);
    }

    [RelayCommand]
    private void ResetView()
    {
        CameraZoom = 1f;
        CameraPanX = 0f;
        CameraPanY = 0f;
        CameraRotationDegrees = 0f;
        ViewProjectionMode = ViewProjectionMode.ThreeD;
    }

    [RelayCommand]
    private void RotateLeft()
    {
        CameraRotationDegrees = IsoMath.NormalizeRotationDegrees(CameraRotationDegrees - 90f);
    }

    [RelayCommand]
    private void RotateRight()
    {
        CameraRotationDegrees = IsoMath.NormalizeRotationDegrees(CameraRotationDegrees + 90f);
    }

    [RelayCommand]
    private void HandleTileClick(TileClickCommandParameter? args)
    {
        if (args is null || TileMap is not { } map)
        {
            return;
        }

        var tile = args.Tile;
        LastClickText = $"Click: ({tile.Column}, {tile.Row}) {args.Button}";

        if ((uint)tile.Row >= (uint)map.Rows || (uint)tile.Column >= (uint)map.Cols)
        {
            return;
        }

        if (args.Button == MouseButton.Left)
        {
            EnsureRuntimeCollectionsAssigned();
            PlacePiece(tile, SelectedPieceTypeId);
            ToggleSelectionHighlight(tile);
            UpdateDiagnostics();
        }
    }

    [RelayCommand]
    private void HandleTileHover(TileHoverCommandParameter? args)
    {
        if (args is null || !IsHoverHighlightEnabled)
        {
            return;
        }

        EnsureRuntimeCollectionsAssigned();
        SetHoverHighlight(args.Tile);
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void AddSampleHighlights()
    {
        EnsureRuntimeCollectionsAssigned();
        AddMovementHighlights(new TileCoordinate(5, 5));
    }

    [RelayCommand]
    private void ClearHighlights()
    {
        _tileHighlightItems.Clear();
        _hoverHighlight = null;
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void AddSamplePieces()
    {
        EnsureRuntimeCollectionsAssigned();
        _pieceItems.Clear();
        _selectedPiece = null;
        _nextPieceIndex = 1;
        AddStackedBridgeUnit(new TileCoordinate(9, 9));
        _pieceItems.Add(CreatePiece("city", new TileCoordinate(12, 11)));
        _pieceItems.Add(CreatePiece("marker", new TileCoordinate(7, 14)));
        _pieceItems.Add(CreatePiece("unit", new TileCoordinate(14, 9), metadata: new Dictionary<string, string> { ["faction"] = "red" }));
        SelectPiece(_pieceItems.OfType<ObservableMapPiece>().LastOrDefault());
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void ClearPieces()
    {
        _pieceItems.Clear();
        SelectPiece(null);
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void UseNullRuntimeCollections()
    {
        Pieces = null;
        TileHighlights = null;
        SetupStatusText = "Setup: runtime collections set to null after map lock";
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void AssignRuntimeCollections()
    {
        Pieces = _pieceItems;
        TileHighlights = _tileHighlightItems;
        SetupStatusText = "Setup: runtime collections assigned after map lock";
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void RotateSelectedPiece()
    {
        if (_selectedPiece is null)
        {
            return;
        }

        _selectedPiece.Orientation = _selectedPiece.Orientation switch
        {
            PieceOrientation.Degrees0 => PieceOrientation.Degrees90,
            PieceOrientation.Degrees90 => PieceOrientation.Degrees180,
            PieceOrientation.Degrees180 => PieceOrientation.Degrees270,
            _ => PieceOrientation.Degrees0,
        };
        UpdateSelectedPieceText();
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void ToggleSelectedVisibility()
    {
        if (_selectedPiece is null)
        {
            return;
        }

        _selectedPiece.IsVisible = !_selectedPiece.IsVisible;
        UpdateSelectedPieceText();
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void CycleSelectedFaction()
    {
        if (_selectedPiece is null)
        {
            return;
        }

        var current = _selectedPiece.Metadata is not null &&
                      _selectedPiece.Metadata.TryGetValue("faction", out var value)
            ? value
            : "blue";
        var next = current.ToUpperInvariant() switch
        {
            "BLUE" => "red",
            "RED" => "gold",
            _ => "blue",
        };

        _selectedPiece.Metadata = new Dictionary<string, string> { ["faction"] = next };
        UpdateSelectedPieceText();
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void RemoveSelectedPiece()
    {
        if (_selectedPiece is null)
        {
            return;
        }

        _pieceItems.Remove(_selectedPiece);
        SelectPiece(null);
        UpdateDiagnostics();
    }

    [RelayCommand]
    private void RunInvalidDataDiagnostic()
    {
        try
        {
            var viewport = new global::IsoViewport.Controls.Controls.IsoViewport
            {
                PieceTypeDefinitions = SamplePieceTypes,
                TileMap = TileMapPresets.Flat(2, 2),
            };
            viewport.Pieces = new[]
            {
                new ObservableMapPiece("bad-piece", "missing-type", new TileCoordinate(0, 0)),
            };
        }
        catch (Exception ex)
        {
            RendererErrorText = $"Renderer/validation: {ex.GetType().Name}: {ex.Message}";
            UpdateDiagnostics();
            return;
        }

        RendererErrorText = "Renderer/validation: no error";
        UpdateDiagnostics();
    }

    partial void OnTileMapChanged(TileMap? value)
    {
        OnPropertyChanged(nameof(MapDimensions));
        HoveredTile = null;
        LastClickText = "Click: none";
        UpdateHoveredTileText();
        UpdateDiagnostics();
    }

    partial void OnHoveredTileChanged(TileCoordinate? value)
    {
        if (value is null && _hoverHighlight is not null)
        {
            _tileHighlightItems.Remove(_hoverHighlight);
            _hoverHighlight = null;
        }

        UpdateHoveredTileText();
        UpdateDiagnostics();
    }

    partial void OnIsHoverHighlightEnabledChanged(bool value)
    {
        if (!value && _hoverHighlight is not null)
        {
            _tileHighlightItems.Remove(_hoverHighlight);
            _hoverHighlight = null;
        }
    }

    partial void OnSelectedScenarioPresetChanged(ScenarioPreset value)
    {
        ScenarioName = value.DisplayName;
    }

    partial void OnIsSetupLockedChanged(bool value)
    {
        UpdateDiagnostics();
    }

    partial void OnRenderModeChanged(TerrainRenderMode value)
    {
        UpdateDiagnostics();
    }

    partial void OnViewProjectionModeChanged(ViewProjectionMode value)
    {
        OnPropertyChanged(nameof(IsTopDownView));
        UpdateDiagnostics();
    }

    partial void OnCameraRotationDegreesChanged(float value)
    {
        var normalized = IsoMath.NormalizeRotationDegrees(value);

        if (Math.Abs(normalized - value) > 0.0001f)
        {
            CameraRotationDegrees = normalized;
        }
    }

    private void ApplyScenario(ScenarioPreset scenario)
    {
        SelectedScenarioPreset = scenario;
        ScenarioName = scenario.DisplayName;
        RendererErrorText = "Renderer: none";
        ResetView();

        switch (scenario.Id)
        {
            case "empty":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                break;
            case "stacked":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddStackedBridgeUnit(new TileCoordinate(10, 10));
                AddMovementHighlights(new TileCoordinate(10, 10));
                SelectPiece(_pieceItems.OfType<ObservableMapPiece>().LastOrDefault());
                break;
            case "dense-units":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddDenseUnits(120);
                break;
            case "dense-highlights":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddDenseHighlights(180);
                break;
            case "water":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddWaterScenarioPieces();
                break;
            case "invalid":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                RunInvalidDataDiagnostic();
                break;
            case "perf-pieces":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddDenseUnits(1_000);
                break;
            case "perf-highlights":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddDenseHighlights(500);
                break;
            case "perf-mixed":
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddDenseUnits(1_000);
                AddDenseHighlights(500);
                break;
            default:
                ResetRuntimeCollections(clearItems: true, assignCollections: true);
                AddSamplePieces();
                AddSampleHighlights();
                break;
        }

        SetupStatusText = "Setup: type definitions assigned, map assigned once, runtime collections assigned";
        UpdateSelectedPieceText();
        UpdateDiagnostics();
    }

    private void UpdateHoveredTileText()
    {
        if (HoveredTile is not { } hovered || TileMap is not { } map)
        {
            HoveredTileText = "Hover: none";
            return;
        }

        if ((uint)hovered.Row >= (uint)map.Rows || (uint)hovered.Column >= (uint)map.Cols)
        {
            HoveredTileText = "Hover: none";
            return;
        }

        var elevation = map.Elevation[hovered.Row, hovered.Column];
        var typeName = GetSurfaceTypeName(map.TileType[hovered.Row, hovered.Column], elevation);
        HoveredTileText = $"Hover: ({hovered.Column}, {hovered.Row}) {typeName} elev {elevation}";
    }

    private void ToggleSelectionHighlight(TileCoordinate tile)
    {
        var existing = _tileHighlightItems
            .OfType<ObservableTileHighlight>()
            .FirstOrDefault(highlight => highlight.Tile == tile && !ReferenceEquals(highlight, _hoverHighlight));

        if (existing is not null)
        {
            _tileHighlightItems.Remove(existing);
            return;
        }

        _tileHighlightItems.Add(new ObservableTileHighlight(tile, Colors.Gold));
    }

    private void PlacePiece(TileCoordinate tile, string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return;
        }

        var orientation = typeId == "bridge" || typeId == "boat"
            ? PieceOrientation.Degrees90
            : PieceOrientation.Degrees0;
        var metadata = typeId == "unit"
            ? new Dictionary<string, string> { ["faction"] = "blue" }
            : null;
        var piece = CreatePiece(typeId, tile, orientation, metadata);
        _pieceItems.Add(piece);
        SelectPiece(piece);
    }

    private ObservableMapPiece CreatePiece(
        string typeId,
        TileCoordinate tile,
        PieceOrientation orientation = PieceOrientation.Degrees0,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ObservableMapPiece($"{typeId}-{_nextPieceIndex++}", typeId, tile)
        {
            Orientation = orientation,
            Metadata = metadata,
        };
    }

    private void SelectPiece(ObservableMapPiece? piece)
    {
        _selectedPiece = piece;
        UpdateSelectedPieceText();
    }

    private void UpdateSelectedPieceText()
    {
        if (_selectedPiece is null)
        {
            SelectedPieceText = "Selected piece: none";
            return;
        }

        var visible = _selectedPiece.IsVisible ? "visible" : "hidden";
        var faction = _selectedPiece.Metadata is not null && _selectedPiece.Metadata.TryGetValue("faction", out var value)
            ? $" {value}"
            : string.Empty;
        SelectedPieceText = $"Selected piece: {_selectedPiece.Id} ({_selectedPiece.Tile.Column}, {_selectedPiece.Tile.Row}) {visible}{faction}";
    }

    private void SetHoverHighlight(TileCoordinate tile)
    {
        if (_hoverHighlight is null)
        {
            _hoverHighlight = new ObservableTileHighlight(tile, Colors.DeepSkyBlue);
            _tileHighlightItems.Add(_hoverHighlight);
            return;
        }

        _hoverHighlight.Tile = tile;
        _hoverHighlight.Color = Colors.DeepSkyBlue;
    }

    private void ResetRuntimeCollections(bool clearItems, bool assignCollections)
    {
        if (clearItems)
        {
            _pieceItems.Clear();
            _tileHighlightItems.Clear();
            _hoverHighlight = null;
            _selectedPiece = null;
            _nextPieceIndex = 1;
        }

        if (assignCollections)
        {
            Pieces = _pieceItems;
            TileHighlights = _tileHighlightItems;
        }
    }

    private void EnsureRuntimeCollectionsAssigned()
    {
        if (Pieces is null || TileHighlights is null)
        {
            AssignRuntimeCollections();
        }
    }

    private void AddStackedBridgeUnit(TileCoordinate tile)
    {
        _pieceItems.Add(CreatePiece("bridge", tile, PieceOrientation.Degrees0));
        _pieceItems.Add(CreatePiece("unit", tile, PieceOrientation.Degrees0, new Dictionary<string, string> { ["faction"] = "blue" }));
    }

    private void AddMovementHighlights(TileCoordinate origin)
    {
        _tileHighlightItems.Add(new ObservableTileHighlight(origin, Colors.Gold));
        _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(origin.Row, origin.Column + 1), Colors.LimeGreen));
        _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(origin.Row + 1, origin.Column), Colors.LimeGreen));
        _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(origin.Row + 1, origin.Column + 1), Colors.OrangeRed));
        _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(origin.Row + 2, origin.Column + 1), Colors.DodgerBlue));
    }

    private void AddDenseUnits(int targetCount)
    {
        if (TileMap is not { } map)
        {
            return;
        }

        var added = 0;
        var attempts = 0;
        var maxAttempts = Math.Max(targetCount * 8, map.Rows * map.Cols);

        while (added < targetCount && attempts < maxAttempts)
        {
            var row = 2 + ((attempts * 7) % Math.Max(1, map.Rows - 4));
            var col = 2 + ((attempts * 11) % Math.Max(1, map.Cols - 4));
            attempts++;

            if (TileMap.IsWaterElevation(map.Elevation[row, col]))
            {
                continue;
            }

            var faction = added % 3 == 0 ? "red" : added % 3 == 1 ? "gold" : "blue";
            _pieceItems.Add(CreatePiece("unit", new TileCoordinate(row, col), metadata: new Dictionary<string, string> { ["faction"] = faction }));
            added++;
        }
    }

    private void AddDenseHighlights(int targetCount)
    {
        if (TileMap is not { } map)
        {
            return;
        }

        var added = 0;

        for (var row = 2; row < map.Rows - 2 && added < targetCount; row++)
        {
            for (var col = 2; col < map.Cols - 2 && added < targetCount; col++)
            {
                var color = (row + col) % 4 == 0
                    ? Colors.LimeGreen
                    : (row + col) % 4 == 1
                        ? Colors.DodgerBlue
                        : Colors.OrangeRed;
                _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(row, col), color));
                added++;
            }
        }
    }

    private void AddWaterScenarioPieces()
    {
        if (TileMap is not { } map)
        {
            return;
        }

        var addedBoats = 0;

        for (var row = 0; row < map.Rows && addedBoats < 5; row++)
        {
            for (var col = 0; col < map.Cols && addedBoats < 5; col++)
            {
                if (!TileMap.IsWaterElevation(map.Elevation[row, col]))
                {
                    continue;
                }

                _pieceItems.Add(CreatePiece("boat", new TileCoordinate(row, col), addedBoats % 2 == 0 ? PieceOrientation.Degrees0 : PieceOrientation.Degrees90));
                _tileHighlightItems.Add(new ObservableTileHighlight(new TileCoordinate(row, col), Colors.DeepSkyBlue));
                addedBoats++;
            }
        }
    }

    private void OnRuntimeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PieceCount));
        OnPropertyChanged(nameof(VisiblePieceCount));
        OnPropertyChanged(nameof(HighlightCount));
        UpdateDiagnostics();
    }

    private void UpdateDiagnostics()
    {
        OnPropertyChanged(nameof(PieceCount));
        OnPropertyChanged(nameof(VisiblePieceCount));
        OnPropertyChanged(nameof(HighlightCount));
        var pieceBinding = Pieces is null ? "null" : "assigned";
        var highlightBinding = TileHighlights is null ? "null" : "assigned";
        DiagnosticsText = $"Diagnostics: scenario {ScenarioName}; pieces {PieceCount} ({VisiblePieceCount} visible, binding {pieceBinding}); highlights {HighlightCount} (binding {highlightBinding}); setup locked {IsSetupLocked}; render {RenderMode}; projection {ViewProjectionMode}; fps {Fps:0}; visible tiles {VisibleTiles}";
    }

    private static TileMap BuildMixedMap(int rows, int cols)
    {
        var map = TileMapPresets.Island(rows, cols);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var elevationWave = (int)(Math.Sin(row * 0.18d) * 5d + Math.Cos(col * 0.12d) * 6d);
                var elevation = (byte)Math.Clamp(TileMap.LandMinElevation + 8 + elevationWave, TileMap.LandMinElevation, TileMap.MaxElevation);

                if (TileMap.IsWaterElevation(map.Elevation[row, col]))
                {
                    continue;
                }

                if ((row + col) % 19 == 0)
                {
                    map.SetTile(row, col, (byte)TileType.Forest, elevation);
                }
                else if ((row * 3 + col) % 37 == 0)
                {
                    map.SetTile(row, col, (byte)TileType.Stone, (byte)Math.Min(TileMap.MaxElevation, elevation + 10));
                }
                else if ((row * 5 + col * 2) % 71 == 0)
                {
                    map.SetTile(row, col, (byte)TileType.Iron, (byte)Math.Min(TileMap.MaxElevation, elevation + 24));
                }
            }
        }

        return map;
    }

    private static string GetSurfaceTypeName(byte tileType, byte elevation)
    {
        if (TileMap.IsWaterElevation(elevation))
        {
            return elevation <= TileMap.DeepWaterElevation ? "Deep Water" : "Shallow Water";
        }

        return Enum.IsDefined(typeof(TileType), tileType)
            ? ((TileType)tileType).ToString()
            : $"Type {tileType}";
    }
}

public sealed record ScenarioPreset(string Id, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}
