using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Demo.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TileType[] ClickCycleTileTypes = Enum.GetValues<TileType>();
    private const int RealisticWorldRows = 2500;
    private const int RealisticWorldCols = 2500;

    [ObservableProperty]
    private TileMap? _tileMap;

    [ObservableProperty]
    private ObjectLayer? _objectLayer;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private float _cameraZoom = 1f;

    [ObservableProperty]
    private float _cameraPanX = 0f;

    [ObservableProperty]
    private float _cameraPanY = 0f;

    [ObservableProperty]
    private float _cameraRotationDegrees = 0f;

    [ObservableProperty]
    private ViewProjectionMode _viewProjectionMode = ViewProjectionMode.ThreeD;

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
    private bool _animationsEnabled = true;

    [ObservableProperty]
    private int _objectCount;

    [ObservableProperty]
    private MiniMapLocation _miniMapLocation = MiniMapLocation.BottomRight;

    [ObservableProperty]
    private TerrainRenderMode _renderMode = TerrainRenderMode.Voxel;

    public IReadOnlyList<TerrainRenderMode> RenderModes { get; } = Enum.GetValues<TerrainRenderMode>();

    private (int Col, int Row)? _hoveredTile;
    private string _hoveredTileText = "Hover: none";

    public string MapDimensions => TileMap is { } map ? $"{map.Rows}x{map.Cols}" : "No map";

    public bool IsTopDownView
    {
        get => ViewProjectionMode == IsoViewport.Controls.Rendering.ViewProjectionMode.TopDown;
        set
        {
            var next = value
                ? IsoViewport.Controls.Rendering.ViewProjectionMode.TopDown
                : IsoViewport.Controls.Rendering.ViewProjectionMode.ThreeD;

            if (ViewProjectionMode != next)
            {
                ViewProjectionMode = next;
            }
        }
    }

    public (int Col, int Row)? HoveredTile
    {
        get => _hoveredTile;
        set
        {
            if (SetProperty(ref _hoveredTile, value))
            {
                UpdateHoveredTileText();
            }
        }
    }

    public string HoveredTileText
    {
        get => _hoveredTileText;
        private set => SetProperty(ref _hoveredTileText, value);
    }

    public MainViewModel()
    {
        var initialMap = TileMapPresets.RealisticWorld(RealisticWorldRows, RealisticWorldCols);
        TileMap = initialMap;
        ObjectLayer = new ObjectLayer();
    }

    [RelayCommand]
    private Task LoadRealisticWorld()
    {
        return LoadPresetAsync(() => CreateScene(TileMapPresets.RealisticWorld(RealisticWorldRows, RealisticWorldCols)));
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

    partial void OnTileMapChanged(TileMap? value)
    {
        OnPropertyChanged(nameof(MapDimensions));
        HoveredTile = null;
        UpdateHoveredTileText();
    }

    partial void OnCameraRotationDegreesChanged(float value)
    {
        var normalized = IsoMath.NormalizeRotationDegrees(value);

        if (Math.Abs(normalized - value) > 0.0001f)
        {
            CameraRotationDegrees = normalized;
        }
    }

    partial void OnViewProjectionModeChanged(ViewProjectionMode value)
    {
        OnPropertyChanged(nameof(IsTopDownView));
    }

    partial void OnObjectLayerChanged(ObjectLayer? value)
    {
        ObjectCount = value?.Count ?? 0;
    }

    [RelayCommand]
    private void HandleTileClick(TileClickedEventArgs? args)
    {
        if (args is null || TileMap is not { } map)
        {
            return;
        }

        if ((uint)args.Row >= (uint)map.Rows || (uint)args.Col >= (uint)map.Cols)
        {
            return;
        }

        switch (args.Button)
        {
            case Avalonia.Input.MouseButton.Left:
            {
                var currentType = map.TileType[args.Row, args.Col];
                var currentIndex = Array.IndexOf(ClickCycleTileTypes, (TileType)currentType);
                var nextIndex = currentIndex >= 0
                    ? (currentIndex + 1) % ClickCycleTileTypes.Length
                    : 0;
                var nextType = (byte)ClickCycleTileTypes[nextIndex];
                var elevation = map.Elevation[args.Row, args.Col];
                map.SetTile(args.Row, args.Col, nextType, elevation);
                break;
            }
            case Avalonia.Input.MouseButton.Right:
            {
                ObjectLayer ??= new ObjectLayer();

                if (ObjectLayer.Contains(args.Col, args.Row, (byte)ObjectType.Unit))
                {
                    ObjectLayer.Remove(args.Col, args.Row, (byte)ObjectType.Unit);
                }
                else
                {
                    ObjectLayer.Add(new TileObject
                    {
                        Col = args.Col,
                        Row = args.Row,
                        Type = (byte)ObjectType.Unit,
                    });
                }

                ObjectCount = ObjectLayer.Count;
                break;
            }
        }

        if (HoveredTile is { } hovered &&
            hovered.Col == args.Col &&
            hovered.Row == args.Row)
        {
            UpdateHoveredTileText();
        }
    }

    private async Task LoadPresetAsync(Func<(TileMap Map, ObjectLayer ObjectLayer)> build)
    {
        IsLoading = true;

        try
        {
            var scene = await Task.Run(build);
            TileMap = scene.Map;
            ObjectLayer = scene.ObjectLayer;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static (TileMap Map, ObjectLayer ObjectLayer) CreateScene(TileMap map)
    {
        return (map, new ObjectLayer());
    }

    private void UpdateHoveredTileText()
    {
        if (HoveredTile is not { } hovered || TileMap is not { } map)
        {
            HoveredTileText = "Hover: none";
            return;
        }

        if ((uint)hovered.Row >= (uint)map.Rows || (uint)hovered.Col >= (uint)map.Cols)
        {
            HoveredTileText = "Hover: none";
            return;
        }

        var elevation = map.Elevation[hovered.Row, hovered.Col];
        var typeName = GetSurfaceTypeName(map.TileType[hovered.Row, hovered.Col], elevation);
        HoveredTileText = $"Hover: ({hovered.Col}, {hovered.Row}) {typeName} elev {elevation}";
    }

    private static string GetSurfaceTypeName(byte tileType, byte elevation)
    {
        if (TileMap.IsWaterElevation(elevation))
        {
            return elevation <= TileMap.DeepWaterElevation ? "Deep Water" : "Shallow Water";
        }

        return Enum.IsDefined(typeof(TileType), tileType)
            ? ((TileType)tileType) switch
            {
                TileType.RareMetals => "Rare Metals",
                _ => ((TileType)tileType).ToString(),
            }
            : $"Type {tileType}";
    }
}
