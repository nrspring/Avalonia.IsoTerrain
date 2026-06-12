using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Harness.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int SmallRows = 32;
    private const int SmallCols = 48;
    private const int MediumRows = 120;
    private const int MediumCols = 160;

    [ObservableProperty]
    private TileMap? _tileMap;

    [ObservableProperty]
    private IReadOnlyList<IMapPieceTypeDefinition> _pieceTypeDefinitions = Array.Empty<IMapPieceTypeDefinition>();

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
    private string _scenarioName = "Small mixed map";

    [ObservableProperty]
    private bool _isHoverHighlightEnabled = true;

    private ObservableTileHighlight? _hoverHighlight;

    public MainViewModel()
    {
        LoadSmallMap();
    }

    public ObservableCollection<ITileHighlight> TileHighlights { get; } = [];

    public IReadOnlyList<TerrainRenderMode> RenderModes { get; } = Enum.GetValues<TerrainRenderMode>();

    public IReadOnlyList<MiniMapLocation> MiniMapLocations { get; } = Enum.GetValues<MiniMapLocation>();

    public string MapDimensions => TileMap is { } map ? $"{map.Rows}x{map.Cols}" : "No map";

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
    private void LoadSmallMap()
    {
        ScenarioName = "Small mixed map";
        TileMap = BuildMixedMap(SmallRows, SmallCols);
    }

    [RelayCommand]
    private void LoadMediumMap()
    {
        ScenarioName = "Medium mixed map";
        TileMap = BuildMixedMap(MediumRows, MediumCols);
    }

    [RelayCommand]
    private void LoadFlatMap()
    {
        ScenarioName = "Flat baseline map";
        TileMap = TileMapPresets.Flat(SmallRows, SmallCols, (byte)TileType.Grass);
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
            ToggleSelectionHighlight(tile);
        }
    }

    [RelayCommand]
    private void HandleTileHover(TileHoverCommandParameter? args)
    {
        if (args is null || !IsHoverHighlightEnabled)
        {
            return;
        }

        SetHoverHighlight(args.Tile);
    }

    [RelayCommand]
    private void AddSampleHighlights()
    {
        TileHighlights.Add(new ObservableTileHighlight(new TileCoordinate(4, 4), Colors.LimeGreen));
        TileHighlights.Add(new ObservableTileHighlight(new TileCoordinate(4, 5), Colors.LimeGreen));
        TileHighlights.Add(new ObservableTileHighlight(new TileCoordinate(5, 6), Colors.OrangeRed));
        TileHighlights.Add(new ObservableTileHighlight(new TileCoordinate(6, 7), Colors.DodgerBlue));
    }

    [RelayCommand]
    private void ClearHighlights()
    {
        TileHighlights.Clear();
        _hoverHighlight = null;
    }

    partial void OnTileMapChanged(TileMap? value)
    {
        OnPropertyChanged(nameof(MapDimensions));
        HoveredTile = null;
        LastClickText = "Click: none";
        ClearHighlights();
        UpdateHoveredTileText();
    }

    partial void OnHoveredTileChanged(TileCoordinate? value)
    {
        if (value is null && _hoverHighlight is not null)
        {
            TileHighlights.Remove(_hoverHighlight);
            _hoverHighlight = null;
        }

        UpdateHoveredTileText();
    }

    partial void OnIsHoverHighlightEnabledChanged(bool value)
    {
        if (!value && _hoverHighlight is not null)
        {
            TileHighlights.Remove(_hoverHighlight);
            _hoverHighlight = null;
        }
    }

    partial void OnViewProjectionModeChanged(ViewProjectionMode value)
    {
        OnPropertyChanged(nameof(IsTopDownView));
    }

    partial void OnCameraRotationDegreesChanged(float value)
    {
        var normalized = IsoMath.NormalizeRotationDegrees(value);

        if (Math.Abs(normalized - value) > 0.0001f)
        {
            CameraRotationDegrees = normalized;
        }
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
        var existing = TileHighlights
            .OfType<ObservableTileHighlight>()
            .FirstOrDefault(highlight => highlight.Tile == tile && !ReferenceEquals(highlight, _hoverHighlight));

        if (existing is not null)
        {
            TileHighlights.Remove(existing);
            return;
        }

        TileHighlights.Add(new ObservableTileHighlight(tile, Colors.Gold));
    }

    private void SetHoverHighlight(TileCoordinate tile)
    {
        if (_hoverHighlight is null)
        {
            _hoverHighlight = new ObservableTileHighlight(tile, Colors.DeepSkyBlue);
            TileHighlights.Add(_hoverHighlight);
            return;
        }

        _hoverHighlight.Tile = tile;
        _hoverHighlight.Color = Colors.DeepSkyBlue;
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
