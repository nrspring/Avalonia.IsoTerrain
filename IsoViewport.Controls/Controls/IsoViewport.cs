using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Controls;

public sealed class IsoViewport : Grid
{
    public static readonly StyledProperty<TileMap?> TileMapProperty =
        AvaloniaProperty.Register<IsoViewport, TileMap?>(nameof(TileMap));

    public static readonly StyledProperty<IEnumerable?> PieceTypeDefinitionsProperty =
        AvaloniaProperty.Register<IsoViewport, IEnumerable?>(nameof(PieceTypeDefinitions));

    public static readonly StyledProperty<IEnumerable?> PiecesProperty =
        AvaloniaProperty.Register<IsoViewport, IEnumerable?>(nameof(Pieces));

    public static readonly StyledProperty<IEnumerable?> TileHighlightsProperty =
        AvaloniaProperty.Register<IsoViewport, IEnumerable?>(nameof(TileHighlights));

    public static readonly StyledProperty<ObjectLayer?> ObjectLayerProperty =
        AvaloniaProperty.Register<IsoViewport, ObjectLayer?>(nameof(ObjectLayer));

    public static readonly StyledProperty<float> CameraZoomProperty =
        AvaloniaProperty.Register<IsoViewport, float>(nameof(CameraZoom), 1f);

    public static readonly StyledProperty<float> CameraPanXProperty =
        AvaloniaProperty.Register<IsoViewport, float>(nameof(CameraPanX), 0f);

    public static readonly StyledProperty<float> CameraPanYProperty =
        AvaloniaProperty.Register<IsoViewport, float>(nameof(CameraPanY), 0f);

    public static readonly StyledProperty<float> CameraRotationDegreesProperty =
        AvaloniaProperty.Register<IsoViewport, float>(nameof(CameraRotationDegrees), 0f);

    public static readonly StyledProperty<ViewProjectionMode> ViewProjectionModeProperty =
        AvaloniaProperty.Register<IsoViewport, ViewProjectionMode>(nameof(ViewProjectionMode), ViewProjectionMode.ThreeD);

    public static readonly StyledProperty<TerrainRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<IsoViewport, TerrainRenderMode>(nameof(RenderMode), TerrainRenderMode.Voxel);

    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<IsoViewport, bool>(nameof(AnimationsEnabled), true);

    public static readonly StyledProperty<bool> IsMiniMapVisibleProperty =
        AvaloniaProperty.Register<IsoViewport, bool>(nameof(IsMiniMapVisible), true);

    public static readonly StyledProperty<MiniMapLocation> MiniMapLocationProperty =
        AvaloniaProperty.Register<IsoViewport, MiniMapLocation>(nameof(MiniMapLocation), MiniMapLocation.BottomRight);

    public static readonly DirectProperty<IsoViewport, TileCoordinate?> HoveredTileProperty =
        AvaloniaProperty.RegisterDirect<IsoViewport, TileCoordinate?>(
            nameof(HoveredTile),
            viewport => viewport.HoveredTile);

    public static readonly StyledProperty<ICommand?> TileHoverCommandProperty =
        AvaloniaProperty.Register<IsoViewport, ICommand?>(nameof(TileHoverCommand));

    public static readonly StyledProperty<ICommand?> TileClickCommandProperty =
        AvaloniaProperty.Register<IsoViewport, ICommand?>(nameof(TileClickCommand));

    public static readonly StyledProperty<int> VisibleTilesProperty =
        AvaloniaProperty.Register<IsoViewport, int>(nameof(VisibleTiles));

    public static readonly StyledProperty<int> VertexCountProperty =
        AvaloniaProperty.Register<IsoViewport, int>(nameof(VertexCount));

    public static readonly StyledProperty<int> VisibleChunksProperty =
        AvaloniaProperty.Register<IsoViewport, int>(nameof(VisibleChunks));

    public static readonly StyledProperty<int> RenderedTilesProperty =
        AvaloniaProperty.Register<IsoViewport, int>(nameof(RenderedTiles));

    public static readonly StyledProperty<double> FpsProperty =
        AvaloniaProperty.Register<IsoViewport, double>(nameof(Fps));

    public static readonly DirectProperty<IsoViewport, bool> IsSetupLockedProperty =
        AvaloniaProperty.RegisterDirect<IsoViewport, bool>(
            nameof(IsSetupLocked),
            viewport => viewport.IsSetupLocked);

    private readonly IsoTileControl _terrain;
    private readonly MapPieceOverlay _pieceOverlay;
    private readonly IsoInputOverlay _input;
    private readonly TopoLabelOverlay _topoLabels;
    private readonly MiniMapControl _miniMap;
    private readonly Dictionary<string, IMapPieceTypeDefinition> _pieceTypeCatalog = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMapPiece> _runtimePieceCatalog = new(StringComparer.Ordinal);
    private readonly Dictionary<TileCoordinate, ITileHighlight> _runtimeHighlightCatalog = [];
    private readonly List<INotifyPropertyChanged> _observedTypeDefinitionItems = [];
    private readonly List<INotifyPropertyChanged> _observedPieceItems = [];
    private readonly List<INotifyPropertyChanged> _observedHighlightItems = [];
    private INotifyCollectionChanged? _observedTypeDefinitions;
    private INotifyCollectionChanged? _observedPieces;
    private INotifyCollectionChanged? _observedHighlights;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private TileMap? _lockedTileMap;
    private TileCoordinate? _hoveredTile;
    private TileCoordinate? _lastHoverCommandTile;
    private KeyModifiers _lastHoverCommandModifiers;
    private bool _syncingFromChild;
    private bool _isSetupLocked;

    public IsoViewport()
    {
        ClipToBounds = true;

        _terrain = new IsoTileControl
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _pieceOverlay = new MapPieceOverlay
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _input = new IsoInputOverlay
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _topoLabels = new TopoLabelOverlay
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _miniMap = new MiniMapControl();

        Children.Add(_terrain);
        Children.Add(_pieceOverlay);
        Children.Add(_input);
        Children.Add(_topoLabels);
        Children.Add(_miniMap);

        _input.TileHovered += OnInputTileHovered;
        _input.TileClicked += OnInputTileClicked;

        SubscribeToChildChanges();
        SyncAllToChildren();
        UpdateViewportSize();
    }

    public TileMap? TileMap
    {
        get => GetValue(TileMapProperty);
        set
        {
            EnsureOwnerThread(nameof(TileMap));
            ValidateTileMapAssignment(value);
            SetValue(TileMapProperty, value);
        }
    }

    public IEnumerable? PieceTypeDefinitions
    {
        get => GetValue(PieceTypeDefinitionsProperty);
        set
        {
            EnsureOwnerThread(nameof(PieceTypeDefinitions));
            ValidatePieceTypeDefinitionsAssignment(value);
            SetValue(PieceTypeDefinitionsProperty, value);
        }
    }

    public IEnumerable? Pieces
    {
        get => GetValue(PiecesProperty);
        set
        {
            EnsureOwnerThread(nameof(Pieces));
            EnsureDynamicCollectionChangeAllowed(value, nameof(Pieces));
            SetValue(PiecesProperty, value);
        }
    }

    public IEnumerable? TileHighlights
    {
        get => GetValue(TileHighlightsProperty);
        set
        {
            EnsureOwnerThread(nameof(TileHighlights));
            EnsureDynamicCollectionChangeAllowed(value, nameof(TileHighlights));
            SetValue(TileHighlightsProperty, value);
        }
    }

    public ObjectLayer? ObjectLayer
    {
        get => GetValue(ObjectLayerProperty);
        set => SetValue(ObjectLayerProperty, value);
    }

    public float CameraZoom
    {
        get => GetValue(CameraZoomProperty);
        set => SetValue(CameraZoomProperty, value);
    }

    public float CameraPanX
    {
        get => GetValue(CameraPanXProperty);
        set => SetValue(CameraPanXProperty, value);
    }

    public float CameraPanY
    {
        get => GetValue(CameraPanYProperty);
        set => SetValue(CameraPanYProperty, value);
    }

    public float CameraRotationDegrees
    {
        get => GetValue(CameraRotationDegreesProperty);
        set => SetValue(CameraRotationDegreesProperty, value);
    }

    public ViewProjectionMode ViewProjectionMode
    {
        get => GetValue(ViewProjectionModeProperty);
        set => SetValue(ViewProjectionModeProperty, value);
    }

    public TerrainRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public bool AnimationsEnabled
    {
        get => GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    public bool IsMiniMapVisible
    {
        get => GetValue(IsMiniMapVisibleProperty);
        set => SetValue(IsMiniMapVisibleProperty, value);
    }

    public MiniMapLocation MiniMapLocation
    {
        get => GetValue(MiniMapLocationProperty);
        set => SetValue(MiniMapLocationProperty, value);
    }

    public TileCoordinate? HoveredTile => _hoveredTile;

    public ICommand? TileHoverCommand
    {
        get => GetValue(TileHoverCommandProperty);
        set => SetValue(TileHoverCommandProperty, value);
    }

    public ICommand? TileClickCommand
    {
        get => GetValue(TileClickCommandProperty);
        set => SetValue(TileClickCommandProperty, value);
    }

    public int VisibleTiles
    {
        get => GetValue(VisibleTilesProperty);
        set => SetValue(VisibleTilesProperty, value);
    }

    public int VertexCount
    {
        get => GetValue(VertexCountProperty);
        set => SetValue(VertexCountProperty, value);
    }

    public int VisibleChunks
    {
        get => GetValue(VisibleChunksProperty);
        set => SetValue(VisibleChunksProperty, value);
    }

    public int RenderedTiles
    {
        get => GetValue(RenderedTilesProperty);
        set => SetValue(RenderedTilesProperty, value);
    }

    public double Fps
    {
        get => GetValue(FpsProperty);
        set => SetValue(FpsProperty, value);
    }

    public bool IsSetupLocked => _isSetupLocked;

    internal IReadOnlyDictionary<string, IMapPiece> RuntimePieceCatalog => _runtimePieceCatalog;

    internal IReadOnlyDictionary<TileCoordinate, ITileHighlight> RuntimeHighlightCatalog => _runtimeHighlightCatalog;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            UpdateViewportSize();
            return;
        }

        if (_syncingFromChild)
        {
            return;
        }

        if (change.Property == TileMapProperty)
        {
            HandleTileMapChanged(change.GetOldValue<TileMap?>(), change.GetNewValue<TileMap?>());
            _terrain.TileMap = TileMap;
            _pieceOverlay.TileMap = TileMap;
            _input.TileMap = TileMap;
            _topoLabels.TileMap = TileMap;
            _miniMap.TileMap = TileMap;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == PieceTypeDefinitionsProperty)
        {
            HandlePieceTypeDefinitionsChanged(change.GetOldValue<IEnumerable?>(), change.GetNewValue<IEnumerable?>());
            UpdatePieceOverlay();
        }
        else if (change.Property == PiecesProperty)
        {
            HandlePiecesChanged(change.GetOldValue<IEnumerable?>(), change.GetNewValue<IEnumerable?>());
            UpdatePieceOverlay();
        }
        else if (change.Property == TileHighlightsProperty)
        {
            HandleTileHighlightsChanged(change.GetOldValue<IEnumerable?>(), change.GetNewValue<IEnumerable?>());
        }
        else if (change.Property == ObjectLayerProperty)
        {
            _terrain.ObjectLayer = ObjectLayer;
        }
        else if (change.Property == CameraZoomProperty)
        {
            _terrain.CameraZoom = CameraZoom;
            _pieceOverlay.CameraZoom = CameraZoom;
            _input.CameraZoom = CameraZoom;
            _topoLabels.CameraZoom = CameraZoom;
            _miniMap.CameraZoom = CameraZoom;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == CameraPanXProperty)
        {
            _terrain.CameraPanX = CameraPanX;
            _pieceOverlay.CameraPanX = CameraPanX;
            _input.CameraPanX = CameraPanX;
            _topoLabels.CameraPanX = CameraPanX;
            _miniMap.CameraPanX = CameraPanX;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == CameraPanYProperty)
        {
            _terrain.CameraPanY = CameraPanY;
            _pieceOverlay.CameraPanY = CameraPanY;
            _input.CameraPanY = CameraPanY;
            _topoLabels.CameraPanY = CameraPanY;
            _miniMap.CameraPanY = CameraPanY;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == CameraRotationDegreesProperty)
        {
            _terrain.CameraRotationDegrees = CameraRotationDegrees;
            _pieceOverlay.CameraRotationDegrees = CameraRotationDegrees;
            _input.CameraRotationDegrees = CameraRotationDegrees;
            _topoLabels.CameraRotationDegrees = CameraRotationDegrees;
            _miniMap.CameraRotationDegrees = CameraRotationDegrees;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == ViewProjectionModeProperty)
        {
            _terrain.ViewProjectionMode = ViewProjectionMode;
            _pieceOverlay.ViewProjectionMode = ViewProjectionMode;
            _input.ViewProjectionMode = ViewProjectionMode;
            _topoLabels.ViewProjectionMode = ViewProjectionMode;
            _miniMap.ViewProjectionMode = ViewProjectionMode;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == RenderModeProperty)
        {
            _terrain.RenderMode = RenderMode;
            _pieceOverlay.RenderMode = RenderMode;
            _topoLabels.RenderMode = RenderMode;
            _pieceOverlay.InvalidateVisual();
        }
        else if (change.Property == AnimationsEnabledProperty)
        {
            _terrain.AnimationsEnabled = AnimationsEnabled;
            _input.AnimationsEnabled = AnimationsEnabled;
        }
        else if (change.Property == IsMiniMapVisibleProperty)
        {
            _miniMap.IsVisible = IsMiniMapVisible;
        }
        else if (change.Property == MiniMapLocationProperty)
        {
            _miniMap.Location = MiniMapLocation;
        }
        else if (change.Property == HoveredTileProperty)
        {
            _terrain.HoveredTile = ToTuple(HoveredTile);
        }
        else if (change.Property == TileHoverCommandProperty || change.Property == TileClickCommandProperty)
        {
        }
    }

    private void SubscribeToChildChanges()
    {
        _input.GetObservable(IsoInputOverlay.CameraZoomProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraZoomProperty, value)));
        _input.GetObservable(IsoInputOverlay.CameraPanXProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraPanXProperty, value)));
        _input.GetObservable(IsoInputOverlay.CameraPanYProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraPanYProperty, value)));
        _input.GetObservable(IsoInputOverlay.CameraRotationDegreesProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraRotationDegreesProperty, value)));
        _input.GetObservable(IsoInputOverlay.ViewProjectionModeProperty)
            .Subscribe(new ActionObserver<ViewProjectionMode>(value => SetFromChild(ViewProjectionModeProperty, value)));
        _input.GetObservable(IsoInputOverlay.AnimationsEnabledProperty)
            .Subscribe(new ActionObserver<bool>(value => SetFromChild(AnimationsEnabledProperty, value)));
        _miniMap.GetObservable(MiniMapControl.CameraPanXProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraPanXProperty, value)));
        _miniMap.GetObservable(MiniMapControl.CameraPanYProperty)
            .Subscribe(new ActionObserver<float>(value => SetFromChild(CameraPanYProperty, value)));

        _terrain.GetObservable(IsoTileControl.VisibleTilesProperty)
            .Subscribe(new ActionObserver<int>(value => SetFromChild(VisibleTilesProperty, value)));
        _terrain.GetObservable(IsoTileControl.VertexCountProperty)
            .Subscribe(new ActionObserver<int>(value => SetFromChild(VertexCountProperty, value)));
        _terrain.GetObservable(IsoTileControl.VisibleChunksProperty)
            .Subscribe(new ActionObserver<int>(value => SetFromChild(VisibleChunksProperty, value)));
        _terrain.GetObservable(IsoTileControl.RenderedTilesProperty)
            .Subscribe(new ActionObserver<int>(value => SetFromChild(RenderedTilesProperty, value)));
        _terrain.GetObservable(IsoTileControl.FpsProperty)
            .Subscribe(new ActionObserver<double>(value => SetFromChild(FpsProperty, value)));
    }

    private void SetFromChild<T>(StyledProperty<T> property, T value)
    {
        _syncingFromChild = true;
        try
        {
            SetCurrentValue(property, value);
        }
        finally
        {
            _syncingFromChild = false;
        }

        SyncChildOriginatedValue(property);
    }

    private void SyncChildOriginatedValue<T>(StyledProperty<T> property)
    {
        if (property == CameraZoomProperty)
        {
            _terrain.CameraZoom = CameraZoom;
            _pieceOverlay.CameraZoom = CameraZoom;
            _input.CameraZoom = CameraZoom;
            _topoLabels.CameraZoom = CameraZoom;
            _miniMap.CameraZoom = CameraZoom;
            _pieceOverlay.InvalidateVisual();
        }
        else if (property == CameraPanXProperty)
        {
            _terrain.CameraPanX = CameraPanX;
            _pieceOverlay.CameraPanX = CameraPanX;
            _input.CameraPanX = CameraPanX;
            _topoLabels.CameraPanX = CameraPanX;
            _miniMap.CameraPanX = CameraPanX;
            _pieceOverlay.InvalidateVisual();
        }
        else if (property == CameraPanYProperty)
        {
            _terrain.CameraPanY = CameraPanY;
            _pieceOverlay.CameraPanY = CameraPanY;
            _input.CameraPanY = CameraPanY;
            _topoLabels.CameraPanY = CameraPanY;
            _miniMap.CameraPanY = CameraPanY;
            _pieceOverlay.InvalidateVisual();
        }
        else if (property == CameraRotationDegreesProperty)
        {
            _terrain.CameraRotationDegrees = CameraRotationDegrees;
            _pieceOverlay.CameraRotationDegrees = CameraRotationDegrees;
            _input.CameraRotationDegrees = CameraRotationDegrees;
            _topoLabels.CameraRotationDegrees = CameraRotationDegrees;
            _miniMap.CameraRotationDegrees = CameraRotationDegrees;
            _pieceOverlay.InvalidateVisual();
        }
        else if (property == ViewProjectionModeProperty)
        {
            _terrain.ViewProjectionMode = ViewProjectionMode;
            _pieceOverlay.ViewProjectionMode = ViewProjectionMode;
            _input.ViewProjectionMode = ViewProjectionMode;
            _topoLabels.ViewProjectionMode = ViewProjectionMode;
            _miniMap.ViewProjectionMode = ViewProjectionMode;
            _pieceOverlay.InvalidateVisual();
        }
        else if (property == AnimationsEnabledProperty)
        {
            _terrain.AnimationsEnabled = AnimationsEnabled;
            _input.AnimationsEnabled = AnimationsEnabled;
        }
    }

    private void SyncAllToChildren()
    {
        _terrain.TileMap = TileMap;
        _pieceOverlay.TileMap = TileMap;
        _input.TileMap = TileMap;
        _topoLabels.TileMap = TileMap;
        _miniMap.TileMap = TileMap;
        _terrain.ObjectLayer = ObjectLayer;

        _terrain.CameraZoom = CameraZoom;
        _pieceOverlay.CameraZoom = CameraZoom;
        _input.CameraZoom = CameraZoom;
        _topoLabels.CameraZoom = CameraZoom;
        _miniMap.CameraZoom = CameraZoom;

        _terrain.CameraPanX = CameraPanX;
        _pieceOverlay.CameraPanX = CameraPanX;
        _input.CameraPanX = CameraPanX;
        _topoLabels.CameraPanX = CameraPanX;
        _miniMap.CameraPanX = CameraPanX;

        _terrain.CameraPanY = CameraPanY;
        _pieceOverlay.CameraPanY = CameraPanY;
        _input.CameraPanY = CameraPanY;
        _topoLabels.CameraPanY = CameraPanY;
        _miniMap.CameraPanY = CameraPanY;

        _terrain.CameraRotationDegrees = CameraRotationDegrees;
        _pieceOverlay.CameraRotationDegrees = CameraRotationDegrees;
        _input.CameraRotationDegrees = CameraRotationDegrees;
        _topoLabels.CameraRotationDegrees = CameraRotationDegrees;
        _miniMap.CameraRotationDegrees = CameraRotationDegrees;

        _terrain.ViewProjectionMode = ViewProjectionMode;
        _pieceOverlay.ViewProjectionMode = ViewProjectionMode;
        _input.ViewProjectionMode = ViewProjectionMode;
        _topoLabels.ViewProjectionMode = ViewProjectionMode;
        _miniMap.ViewProjectionMode = ViewProjectionMode;

        _terrain.RenderMode = RenderMode;
        _pieceOverlay.RenderMode = RenderMode;
        _topoLabels.RenderMode = RenderMode;
        _terrain.AnimationsEnabled = AnimationsEnabled;
        _input.AnimationsEnabled = AnimationsEnabled;
        _miniMap.IsVisible = IsMiniMapVisible;
        _miniMap.Location = MiniMapLocation;
        _terrain.HoveredTile = ToTuple(HoveredTile);
        UpdateTerrainHighlights();
        UpdatePieceOverlay();
    }

    private void OnInputTileHovered(object? sender, TileHoverChangedEventArgs e)
    {
        HandleInputTileHovered(e.Tile, e.KeyModifiers);
    }

    private void OnInputTileClicked(object? sender, TileClickedEventArgs e)
    {
        HandleInputTileClicked(new TileCoordinate(e.Row, e.Col), e.Button, e.KeyModifiers);
    }

    internal void HandleInputTileHovered((int Col, int Row)? tile, KeyModifiers keyModifiers)
    {
        if (tile is not { } value)
        {
            SetHoveredTile(null);
            _lastHoverCommandTile = null;
            _lastHoverCommandModifiers = KeyModifiers.None;
            return;
        }

        var coordinate = new TileCoordinate(value.Row, value.Col);
        SetHoveredTile(coordinate);

        if (_lastHoverCommandTile == coordinate && _lastHoverCommandModifiers == keyModifiers)
        {
            return;
        }

        _lastHoverCommandTile = coordinate;
        _lastHoverCommandModifiers = keyModifiers;
        var parameter = new TileHoverCommandParameter(coordinate, keyModifiers);

        if (TileHoverCommand is { } command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    internal void HandleInputTileClicked(TileCoordinate tile, MouseButton button, KeyModifiers keyModifiers)
    {
        SetHoveredTile(tile);
        var parameter = new TileClickCommandParameter(tile, button, keyModifiers);

        if (TileClickCommand is { } command && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private void SetHoveredTile(TileCoordinate? tile)
    {
        SetAndRaise(HoveredTileProperty, ref _hoveredTile, tile);
        _terrain.HoveredTile = ToTuple(tile);
    }

    private static (int Col, int Row)? ToTuple(TileCoordinate? tile)
    {
        return tile is { } value
            ? (value.Column, value.Row)
            : null;
    }

    private void HandleTileMapChanged(TileMap? oldMap, TileMap? newMap)
    {
        ValidateTileMapAssignment(newMap);

        if (newMap is null)
        {
            return;
        }

        _lockedTileMap = newMap;
        _lockedTileMap.TileChanged += OnLockedTileMapChanged;
        SetAndRaise(IsSetupLockedProperty, ref _isSetupLocked, true);
    }

    private void HandlePieceTypeDefinitionsChanged(IEnumerable? oldDefinitions, IEnumerable? newDefinitions)
    {
        ValidatePieceTypeDefinitionsAssignment(newDefinitions);

        DetachTypeDefinitionObservers(oldDefinitions);
        AttachTypeDefinitionObservers(newDefinitions);
        RebuildPieceTypeCatalog(validate: false);
    }

    private void HandlePiecesChanged(IEnumerable? oldPieces, IEnumerable? newPieces)
    {
        EnsureDynamicCollectionChangeAllowed(newPieces, nameof(Pieces));

        DetachRuntimeObservers(ref _observedPieces, _observedPieceItems, OnPiecesCollectionChanged, OnPiecePropertyChanged);
        AttachRuntimeObservers(newPieces, ref _observedPieces, _observedPieceItems, OnPiecesCollectionChanged, OnPiecePropertyChanged);
        RebuildRuntimePieceCatalog();
        InvalidateRuntimeVisuals();
    }

    private void HandleTileHighlightsChanged(IEnumerable? oldHighlights, IEnumerable? newHighlights)
    {
        EnsureDynamicCollectionChangeAllowed(newHighlights, nameof(TileHighlights));

        DetachRuntimeObservers(ref _observedHighlights, _observedHighlightItems, OnHighlightsCollectionChanged, OnHighlightPropertyChanged);
        AttachRuntimeObservers(newHighlights, ref _observedHighlights, _observedHighlightItems, OnHighlightsCollectionChanged, OnHighlightPropertyChanged);
        RebuildRuntimeHighlightCatalog();
        InvalidateRuntimeVisuals();
    }

    private void ValidateTileMapAssignment(TileMap? newMap)
    {
        if (IsSetupLocked)
        {
            throw new IsoViewportSetupException("TileMap cannot be cleared or replaced after setup is locked.");
        }

        if (newMap is null)
        {
            return;
        }

        if (PieceTypeDefinitions is null)
        {
            throw new IsoViewportSetupException("PieceTypeDefinitions must be assigned before TileMap is assigned.");
        }

        RebuildPieceTypeCatalog(validate: true);
    }

    private void ValidatePieceTypeDefinitionsAssignment(IEnumerable? newDefinitions)
    {
        if (IsSetupLocked && !ReferenceEquals(newDefinitions, PieceTypeDefinitions))
        {
            throw new IsoViewportSetupException("PieceTypeDefinitions cannot be replaced after setup is locked.");
        }
    }

    private void EnsureDynamicCollectionChangeAllowed(IEnumerable? collection, string propertyName)
    {
        if (!IsSetupLocked && collection is not null)
        {
            throw new IsoViewportSetupException($"{propertyName} cannot be assigned until setup is locked by assigning TileMap.");
        }
    }

    private void AttachTypeDefinitionObservers(IEnumerable? definitions)
    {
        if (definitions is INotifyCollectionChanged observableCollection)
        {
            _observedTypeDefinitions = observableCollection;
            _observedTypeDefinitions.CollectionChanged += OnTypeDefinitionsCollectionChanged;
        }

        if (definitions is null)
        {
            return;
        }

        foreach (var item in definitions)
        {
            if (item is INotifyPropertyChanged observableItem)
            {
                _observedTypeDefinitionItems.Add(observableItem);
                observableItem.PropertyChanged += OnTypeDefinitionPropertyChanged;
            }
        }
    }

    private void DetachTypeDefinitionObservers(IEnumerable? definitions)
    {
        if (_observedTypeDefinitions is not null)
        {
            _observedTypeDefinitions.CollectionChanged -= OnTypeDefinitionsCollectionChanged;
            _observedTypeDefinitions = null;
        }

        foreach (var item in _observedTypeDefinitionItems)
        {
            item.PropertyChanged -= OnTypeDefinitionPropertyChanged;
        }

        _observedTypeDefinitionItems.Clear();
    }

    private void OnTypeDefinitionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(PieceTypeDefinitions));

        if (IsSetupLocked)
        {
            throw new IsoViewportSetupException("PieceTypeDefinitions cannot be modified after setup is locked.");
        }

        DetachTypeDefinitionObservers(PieceTypeDefinitions);
        AttachTypeDefinitionObservers(PieceTypeDefinitions);
        RebuildPieceTypeCatalog(validate: false);
    }

    private void OnTypeDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(PieceTypeDefinitions));

        if (IsSetupLocked)
        {
            throw new IsoViewportSetupException("PieceTypeDefinitions items cannot be modified after setup is locked.");
        }

        RebuildPieceTypeCatalog(validate: false);
    }

    private void OnLockedTileMapChanged(int row, int col)
    {
        EnsureOwnerThread(nameof(TileMap));
        throw new IsoViewportSetupException("TileMap cannot be modified after setup is locked.");
    }

    private void OnPiecesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(Pieces));
        DetachRuntimeObservers(ref _observedPieces, _observedPieceItems, OnPiecesCollectionChanged, OnPiecePropertyChanged);
        AttachRuntimeObservers(Pieces, ref _observedPieces, _observedPieceItems, OnPiecesCollectionChanged, OnPiecePropertyChanged);
        RebuildRuntimePieceCatalog();
        InvalidateRuntimeVisuals();
    }

    private void OnHighlightsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(TileHighlights));
        DetachRuntimeObservers(ref _observedHighlights, _observedHighlightItems, OnHighlightsCollectionChanged, OnHighlightPropertyChanged);
        AttachRuntimeObservers(TileHighlights, ref _observedHighlights, _observedHighlightItems, OnHighlightsCollectionChanged, OnHighlightPropertyChanged);
        RebuildRuntimeHighlightCatalog();
        InvalidateRuntimeVisuals();
    }

    private void OnPiecePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(Pieces));
        RebuildRuntimePieceCatalog();
        InvalidateRuntimeVisuals();
    }

    private void OnHighlightPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        EnsureOwnerThread(nameof(TileHighlights));
        RebuildRuntimeHighlightCatalog();
        InvalidateRuntimeVisuals();
    }

    private void RebuildPieceTypeCatalog(bool validate)
    {
        _pieceTypeCatalog.Clear();

        if (PieceTypeDefinitions is null)
        {
            return;
        }

        foreach (var item in PieceTypeDefinitions)
        {
            if (item is not IMapPieceTypeDefinition definition)
            {
                if (validate)
                {
                    throw new IsoViewportValidationException("Every item in PieceTypeDefinitions must implement IMapPieceTypeDefinition.");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.TypeId))
            {
                if (validate)
                {
                    throw new IsoViewportValidationException($"PieceTypeDefinitions entry '{definition.DisplayName}' must provide a non-empty TypeId.");
                }

                continue;
            }

            if (definition.Renderer is null)
            {
                if (validate)
                {
                    throw new IsoViewportValidationException($"PieceTypeDefinitions entry '{definition.TypeId}' ('{definition.DisplayName}') must provide a renderer.");
                }

                continue;
            }

            _pieceTypeCatalog[definition.TypeId] = definition;
        }

        UpdatePieceOverlay();
    }

    private void RebuildRuntimePieceCatalog()
    {
        _runtimePieceCatalog.Clear();

        if (Pieces is null)
        {
            return;
        }

        var map = RequireLockedMapForRuntimeData();

        foreach (var item in Pieces)
        {
            if (item is not IMapPiece piece)
            {
                throw new IsoViewportValidationException("Every item in Pieces must implement IMapPiece.");
            }

            ValidateRuntimePiece(piece, map);
            _runtimePieceCatalog[piece.Id] = piece;
        }

        UpdatePieceOverlay();
    }

    private void RebuildRuntimeHighlightCatalog()
    {
        _runtimeHighlightCatalog.Clear();

        if (TileHighlights is null)
        {
            UpdateTerrainHighlights();
            return;
        }

        var map = RequireLockedMapForRuntimeData();

        foreach (var item in TileHighlights)
        {
            if (item is not ITileHighlight highlight)
            {
                throw new IsoViewportValidationException("Every item in TileHighlights must implement ITileHighlight.");
            }

            ValidateTileCoordinate(highlight.Tile, map, "TileHighlights entry");
            _runtimeHighlightCatalog[highlight.Tile] = highlight;
        }

        UpdateTerrainHighlights();
    }

    private void UpdateTerrainHighlights()
    {
        _terrain.TileHighlights = _runtimeHighlightCatalog.Count == 0
            ? null
            : _runtimeHighlightCatalog.Values.ToArray();
    }

    private void UpdatePieceOverlay()
    {
        _pieceOverlay.PieceTypeDefinitions = new Dictionary<string, IMapPieceTypeDefinition>(_pieceTypeCatalog, StringComparer.Ordinal);
        _pieceOverlay.Pieces = new Dictionary<string, IMapPiece>(_runtimePieceCatalog, StringComparer.Ordinal);
    }

    private void ValidateRuntimePiece(IMapPiece piece, TileMap map)
    {
        if (string.IsNullOrWhiteSpace(piece.Id))
        {
            throw new IsoViewportValidationException("Pieces entry must provide a non-empty Id.");
        }

        if (string.IsNullOrWhiteSpace(piece.TypeId))
        {
            throw new IsoViewportValidationException($"Pieces entry '{piece.Id}' must provide a non-empty TypeId.");
        }

        if (!_pieceTypeCatalog.ContainsKey(piece.TypeId))
        {
            throw new IsoViewportValidationException($"Pieces entry '{piece.Id}' references unknown piece type '{piece.TypeId}'.");
        }

        ValidateTileCoordinate(piece.Tile, map, $"Pieces entry '{piece.Id}'");
    }

    private static void ValidateTileCoordinate(TileCoordinate tile, TileMap map, string source)
    {
        if ((uint)tile.Row >= (uint)map.Rows || (uint)tile.Column >= (uint)map.Cols)
        {
            throw new IsoViewportValidationException($"{source} tile coordinate '{tile}' is outside the loaded map.");
        }
    }

    private TileMap RequireLockedMapForRuntimeData()
    {
        return _lockedTileMap ?? throw new IsoViewportSetupException("Runtime collections cannot be processed before setup is locked.");
    }

    private void EnsureOwnerThread(string operation)
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new IsoViewportSetupException($"{operation} changes must occur on the IsoViewport owner UI thread.");
        }
    }

    private static void AttachRuntimeObservers(
        IEnumerable? source,
        ref INotifyCollectionChanged? observedCollection,
        List<INotifyPropertyChanged> observedItems,
        NotifyCollectionChangedEventHandler collectionChanged,
        PropertyChangedEventHandler itemChanged)
    {
        if (source is INotifyCollectionChanged observableCollection)
        {
            observedCollection = observableCollection;
            observedCollection.CollectionChanged += collectionChanged;
        }

        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is INotifyPropertyChanged observableItem)
            {
                observedItems.Add(observableItem);
                observableItem.PropertyChanged += itemChanged;
            }
        }
    }

    private static void DetachRuntimeObservers(
        ref INotifyCollectionChanged? observedCollection,
        List<INotifyPropertyChanged> observedItems,
        NotifyCollectionChangedEventHandler collectionChanged,
        PropertyChangedEventHandler itemChanged)
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= collectionChanged;
            observedCollection = null;
        }

        foreach (var item in observedItems)
        {
            item.PropertyChanged -= itemChanged;
        }

        observedItems.Clear();
    }

    private void InvalidateRuntimeVisuals()
    {
        _terrain.RequestNextFrameRendering();
        _pieceOverlay.InvalidateVisual();
        InvalidateVisual();
    }

    private void UpdateViewportSize()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        _terrain.ViewportWidth = width;
        _input.ViewportWidth = width;
        _topoLabels.ViewportWidth = width;
        _miniMap.ViewportWidth = width;

        _terrain.ViewportHeight = height;
        _input.ViewportHeight = height;
        _topoLabels.ViewportHeight = height;
        _miniMap.ViewportHeight = height;
    }

    private sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;

        public ActionObserver(Action<T> onNext)
        {
            _onNext = onNext;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value)
        {
            _onNext(value);
        }
    }
}
