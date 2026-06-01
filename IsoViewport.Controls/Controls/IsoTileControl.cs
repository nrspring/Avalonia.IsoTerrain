using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using IsoViewport.Controls.Rendering;
using Silk.NET.OpenGL;
using RectangleF = System.Drawing.RectangleF;

namespace IsoViewport.Controls.Controls;

public sealed class IsoTileControl : OpenGlControlBase
{
    private const float KeyboardRotationStepDegrees = 15f;

    private enum VboDirtyReason
    {
        None,
        PanChanged,
        TileEdited,
        ZoomChanged,
        MapReplaced,
    }

    public static readonly StyledProperty<TileMap?> TileMapProperty =
        AvaloniaProperty.Register<IsoTileControl, TileMap?>(nameof(TileMap));

    public static readonly StyledProperty<float> CameraZoomProperty =
        AvaloniaProperty.Register<IsoTileControl, float>(nameof(CameraZoom), 1.0f);

    public static readonly StyledProperty<float> CameraPanXProperty =
        AvaloniaProperty.Register<IsoTileControl, float>(nameof(CameraPanX), 0f);

    public static readonly StyledProperty<float> CameraPanYProperty =
        AvaloniaProperty.Register<IsoTileControl, float>(nameof(CameraPanY), 0f);

    public static readonly StyledProperty<float> CameraRotationDegreesProperty =
        AvaloniaProperty.Register<IsoTileControl, float>(nameof(CameraRotationDegrees), 0f);

    public static readonly StyledProperty<ViewProjectionMode> ViewProjectionModeProperty =
        AvaloniaProperty.Register<IsoTileControl, ViewProjectionMode>(nameof(ViewProjectionMode), ViewProjectionMode.ThreeD);

    public static readonly StyledProperty<TerrainRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<IsoTileControl, TerrainRenderMode>(nameof(RenderMode), TerrainRenderMode.Voxel);

    public static readonly StyledProperty<int> VisibleTilesProperty =
        AvaloniaProperty.Register<IsoTileControl, int>(nameof(VisibleTiles));

    public static readonly StyledProperty<int> VertexCountProperty =
        AvaloniaProperty.Register<IsoTileControl, int>(nameof(VertexCount));

    public static readonly StyledProperty<int> VisibleChunksProperty =
        AvaloniaProperty.Register<IsoTileControl, int>(nameof(VisibleChunks));

    public static readonly StyledProperty<int> RenderedTilesProperty =
        AvaloniaProperty.Register<IsoTileControl, int>(nameof(RenderedTiles));

    public static readonly StyledProperty<double> FpsProperty =
        AvaloniaProperty.Register<IsoTileControl, double>(nameof(Fps));

    public static readonly StyledProperty<(int Col, int Row)?> HoveredTileProperty =
        AvaloniaProperty.Register<IsoTileControl, (int Col, int Row)?>(nameof(HoveredTile));

    public static readonly StyledProperty<ICommand?> TileClickedCommandProperty =
        AvaloniaProperty.Register<IsoTileControl, ICommand?>(nameof(TileClickedCommand));

    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<IsoTileControl, bool>(nameof(AnimationsEnabled), true);

    public static readonly StyledProperty<ObjectLayer?> ObjectLayerProperty =
        AvaloniaProperty.Register<IsoTileControl, ObjectLayer?>(nameof(ObjectLayer));

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<IsoTileControl, double>(nameof(ViewportWidth), 0d);

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<IsoTileControl, double>(nameof(ViewportHeight), 0d);

    private const uint VertexStrideBytes = 24;
    private const float ChunkCullPaddingPixels = 8f;
    private const float FarZoomLodEnterZoom = 0.12f;
    private const float FarZoomLodExitZoom = 0.16f;
    private const float TerrainBorderFadeStartZoom = 0.26f;
    private const float TerrainBorderFadeEndZoom = 0.34f;
    private const float WaterAnimationFadeStartZoom = 0.25f;
    private const float WaterAnimationFadeEndZoom = 0.65f;
    private const float WaterAnimationMinStrength = 0.42f;
    private const float WaterGridFadeStartZoom = 0.32f;
    private const float WaterGridFadeEndZoom = 0.60f;
    private const float HoverOuterInset = 0.06f;
    private const float HoverInnerInset = 0.18f;
    private const float HoverDepthBias = 0.0015f;

    private GL _gl = null!;
    private uint _vao;
    private uint _hoverVbo;
    private uint _program;
    private uint _animProgram;
    private int _locViewport;
    private int _locPan;
    private int _locZoom;
    private int _locTerrainBorderVisibility;
    private int _locAnimViewport;
    private int _locAnimPan;
    private int _locAnimZoom;
    private int _locAnimTime;
    private int _locAnimStrength;
    private int _locAnimGridVisibility;
    private ChunkCache? _detailCache;
    private ChunkCache? _farLodCache;
    private TileMap? _observedTileMap;
    private ObjectLayer? _observedObjectLayer;
    private VboDirtyReason _dirtyReason = VboDirtyReason.None;
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _framesSinceFpsUpdate;
    private Point _lastMousePos;
    private bool _panning;
    private MouseButton? _panningButton;
    private double _panTravel;
    private bool _hasPointer;
    private Vector2 _inertiaDelta;
    private int _lodBlockSize = 1;
    private readonly HashSet<Key> _heldKeys = [];
    private readonly DispatcherTimer _keyTimer;

#if DEBUG
    private DebugProc? _debugProc;
    private int _debugUserParam;
#endif

    public IsoTileControl()
    {
        Focusable = true;
        ClipToBounds = true;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        AddHandler(PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerReleasedEvent, HandlePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerMovedEvent, HandlePointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerWheelChangedEvent, HandlePointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(PointerExitedEvent, HandlePointerExited, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        AddHandler(KeyUpEvent, HandleKeyUp, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        LostFocus += (_, _) =>
        {
            _heldKeys.Clear();
            _panning = false;
            _panningButton = null;
            _panTravel = 0d;
        };
        _keyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1d / 60d),
        };
        _keyTimer.Tick += OnKeyTimerTick;
        _keyTimer.Start();
    }

    public TileMap? TileMap
    {
        get => GetValue(TileMapProperty);
        set => SetValue(TileMapProperty, value);
    }

    public float CameraZoom
    {
        get => GetValue(CameraZoomProperty);
        set => SetValue(CameraZoomProperty, IsoCamera.ClampZoom(value));
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
        set => SetValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(value));
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

    public (int Col, int Row)? HoveredTile
    {
        get => GetValue(HoveredTileProperty);
        set => SetValue(HoveredTileProperty, value);
    }

    public ICommand? TileClickedCommand
    {
        get => GetValue(TileClickedCommandProperty);
        set => SetValue(TileClickedCommandProperty, value);
    }

    public bool AnimationsEnabled
    {
        get => GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    public ObjectLayer? ObjectLayer
    {
        get => GetValue(ObjectLayerProperty);
        set => SetValue(ObjectLayerProperty, value);
    }

    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double ViewportHeight
    {
        get => GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    public event EventHandler<TileClickedEventArgs>? TileClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TileMapProperty)
        {
            ObserveTileMap(TileMap);
            SetCurrentValue(HoveredTileProperty, null);
            SetDirty(VboDirtyReason.MapReplaced);
        }
        else if (change.Property == ObjectLayerProperty)
        {
            if (change.OldValue is ObjectLayer oldLayer && _vao != 0)
            {
                oldLayer.Delete(_gl);
            }

            ObserveObjectLayer(ObjectLayer);
            RequestNextFrameRendering();
        }
        else if (change.Property == CameraZoomProperty)
        {
            SetDirty(VboDirtyReason.PanChanged);
        }
        else if (change.Property == CameraRotationDegreesProperty)
        {
            if (change.NewValue is float rawValue)
            {
                var normalized = IsoMath.NormalizeRotationDegrees(rawValue);

                if (Math.Abs(normalized - rawValue) > 0.0001f)
                {
                    SetCurrentValue(CameraRotationDegreesProperty, normalized);
                    return;
                }
            }

            SetDirty(VboDirtyReason.ZoomChanged);
        }
        else if (change.Property == ViewProjectionModeProperty)
        {
            SetDirty(VboDirtyReason.ZoomChanged);
        }
        else if (change.Property == RenderModeProperty)
        {
            SetDirty(VboDirtyReason.ZoomChanged);
        }
        else if (change.Property == CameraPanXProperty || change.Property == CameraPanYProperty)
        {
            SetDirty(VboDirtyReason.PanChanged);
        }
        else if (change.Property == AnimationsEnabledProperty)
        {
            RequestNextFrameRendering();
        }
        else if (change.Property is { } property &&
                 (property == BoundsProperty ||
                  property == ViewportWidthProperty ||
                  property == ViewportHeightProperty))
        {
            RequestNextFrameRendering();
        }
    }

    private void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var position = e.GetPosition(this);
        _lastMousePos = position;
        _hasPointer = true;
        UpdateHoveredTile(position);

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _panningButton = point.Properties.IsMiddleButtonPressed ? MouseButton.Middle : MouseButton.Right;
            _panTravel = 0d;
            _inertiaDelta = Vector2.Zero;
            SetCurrentValue(HoveredTileProperty, null);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            RaiseTileClicked(position, MouseButton.Left);
            e.Handled = true;
        }
    }

    private void HandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastMousePos = e.GetPosition(this);

        if (_panning)
        {
            var shouldClick = _panningButton == MouseButton.Right && _panTravel < 4d;
            _panning = false;
            _panningButton = null;
            e.Pointer.Capture(null);

            if (shouldClick)
            {
                RaiseTileClicked(_lastMousePos, MouseButton.Right);
            }
            else
            {
                UpdateHoveredTile(_lastMousePos);
            }

            RequestNextFrameRendering();
            e.Handled = true;
        }
    }

    private void HandlePointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        _hasPointer = true;

        if (_panning)
        {
            var delta = position - _lastMousePos;
            _panTravel += Math.Abs(delta.X) + Math.Abs(delta.Y);
            ApplyPanDelta((float)delta.X, (float)delta.Y);
            _inertiaDelta = new Vector2((float)delta.X, (float)delta.Y);
            SetCurrentValue(HoveredTileProperty, null);
            _lastMousePos = position;
            return;
        }

        _lastMousePos = position;
        UpdateHoveredTile(position);
    }

    private void HandlePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var position = e.GetPosition(this);
        _lastMousePos = position;
        _hasPointer = true;
        ApplyZoomAtPoint(IsoCamera.GetWheelZoomFactor((float)e.Delta.Y), ToVector2(position));
        e.Handled = true;
    }

    private void HandlePointerExited(object? sender, PointerEventArgs e)
    {
        _hasPointer = false;
        SetCurrentValue(HoveredTileProperty, null);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            ResetCamera();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            SetCurrentValue(AnimationsEnabledProperty, !AnimationsEnabled);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Q)
        {
            RotateCameraFromKeyboard(-KeyboardRotationStepDegrees);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E)
        {
            RotateCameraFromKeyboard(KeyboardRotationStepDegrees);
            e.Handled = true;
            return;
        }

        if (IsMovementKey(e.Key) || IsZoomInKey(e.Key) || IsZoomOutKey(e.Key))
        {
            _heldKeys.Add(e.Key);
            RequestNextFrameRendering();
            e.Handled = true;
        }
    }

    private void HandleKeyUp(object? sender, KeyEventArgs e)
    {
        if (_heldKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    protected override void OnOpenGlInit(GlInterface glInterface)
    {
        _gl = GL.GetApi(proc =>
            glInterface.GetProcAddress(proc) is { } p
            && p != IntPtr.Zero ? (nint)p : IntPtr.Zero);

#if DEBUG
        _debugProc = OnGlDebug;
        _debugUserParam = 0;
        _gl.Enable(EnableCap.DebugOutput);
        _gl.DebugMessageCallback(_debugProc, ref _debugUserParam);
#endif

        _program = CreateCompatibleProgram(VertSrc, FragSrc, VertSrcGles, FragSrcGles);
        _animProgram = CreateCompatibleProgram(AnimVertSrc, FragSrc, AnimVertSrcGles, FragSrcGles);
        _locViewport = _gl.GetUniformLocation(_program, "uViewport");
        _locPan = _gl.GetUniformLocation(_program, "uPan");
        _locZoom = _gl.GetUniformLocation(_program, "uZoom");
        _locTerrainBorderVisibility = _gl.GetUniformLocation(_program, "uTerrainBorderVisibility");
        _locAnimViewport = _gl.GetUniformLocation(_animProgram, "uViewport");
        _locAnimPan = _gl.GetUniformLocation(_animProgram, "uPan");
        _locAnimZoom = _gl.GetUniformLocation(_animProgram, "uZoom");
        _locAnimTime = _gl.GetUniformLocation(_animProgram, "uTime");
        _locAnimStrength = _gl.GetUniformLocation(_animProgram, "uWaterAnimationStrength");
        _locAnimGridVisibility = _gl.GetUniformLocation(_animProgram, "uWaterGridVisibility");
        _vao = _gl.GenVertexArray();
        _hoverVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        SetDirty(VboDirtyReason.MapReplaced);
        Debug.WriteLine("IsoTileControl GL ready");
    }

    protected override void OnOpenGlRender(GlInterface _, int framebuffer)
    {
        var logicalWidth = Math.Max(1f, ViewportWidth > 0d ? (float)ViewportWidth : (float)Bounds.Width);
        var logicalHeight = Math.Max(1f, ViewportHeight > 0d ? (float)ViewportHeight : (float)Bounds.Height);
        var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var framebufferWidth = Math.Max(1, (int)Math.Ceiling(logicalWidth * renderScaling));
        var framebufferHeight = Math.Max(1, (int)Math.Ceiling(logicalHeight * renderScaling));
        var rebuildObjects = _dirtyReason is VboDirtyReason.MapReplaced or VboDirtyReason.ZoomChanged;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ClearColor(0.18f, 0.20f, 0.25f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);

        if (TileMap is null)
        {
            ClearMetrics();
            return;
        }

        var zoom = Math.Max(CameraZoom, IsoCamera.MinZoom);
        var nextLodBlockSize = GetFarZoomLodBlockSize(zoom, _lodBlockSize);

        _lodBlockSize = nextLodBlockSize;

        UpdateChunkCache();
        var activeCache = GetActiveChunkCache();

        var worldViewport = new RectangleF(
            -CameraPanX / zoom,
            -CameraPanY / zoom,
            logicalWidth / zoom,
            logicalHeight / zoom);
        worldViewport.Inflate(ChunkCullPaddingPixels / zoom, ChunkCullPaddingPixels / zoom);
        var visibleGround = IsoMath.GetVisibleTileBounds(
            CameraPanX,
            CameraPanY,
            zoom,
            CameraRotationDegrees,
            logicalWidth,
            logicalHeight,
            ViewProjectionMode);
        var stats = activeCache?.UpdateVisibleChunks(worldViewport, visibleGround) ?? default;

        _gl.UseProgram(_program);
        _gl.Uniform2(_locViewport, new Vector2(logicalWidth, logicalHeight));
        _gl.Uniform2(_locPan, new Vector2(CameraPanX, CameraPanY));
        _gl.Uniform1(_locZoom, zoom);
        _gl.Uniform1(_locTerrainBorderVisibility, GetTerrainBorderVisibility(zoom, RenderMode));
        activeCache?.DrawVisibleStaticChunks(_gl, _vao, SetAttribPointers);

        _gl.UseProgram(_animProgram);
        _gl.Uniform2(_locAnimViewport, new Vector2(logicalWidth, logicalHeight));
        _gl.Uniform2(_locAnimPan, new Vector2(CameraPanX, CameraPanY));
        _gl.Uniform1(_locAnimZoom, zoom);
        _gl.Uniform1(_locAnimTime, AnimationsEnabled ? (float)_clock.Elapsed.TotalSeconds : 0f);
        _gl.Uniform1(_locAnimStrength, GetWaterAnimationStrength(zoom));
        _gl.Uniform1(_locAnimGridVisibility, GetWaterGridVisibility(zoom));
        activeCache?.DrawVisibleAnimatedChunks(_gl, _vao, SetAttribPointers);

        DrawHoveredTileHighlight(logicalWidth, logicalHeight);

        if (ObjectLayer is { } objectLayer)
        {
            if (rebuildObjects || objectLayer.Dirty)
            {
                objectLayer.RebuildVbo(_gl, TileMap, CameraRotationDegrees, ViewProjectionMode, RenderMode);
            }

            _gl.BindVertexArray(_vao);
            objectLayer.Draw(
                _gl,
                _program,
                _locViewport,
                _locPan,
                _locZoom,
                logicalWidth,
                logicalHeight,
                zoom,
                CameraPanX,
                CameraPanY);
        }

        SetCurrentValue(VisibleChunksProperty, stats.VisibleChunks);
        SetCurrentValue(RenderedTilesProperty, stats.RenderedTiles);
        SetCurrentValue(VisibleTilesProperty, stats.RenderedTiles);
        SetCurrentValue(VertexCountProperty, stats.VertexCount);
        UpdateFps();

        if (_dirtyReason == VboDirtyReason.PanChanged)
        {
            _dirtyReason = VboDirtyReason.None;
        }

        if (ShouldKeepRendering())
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface _)
    {
        if (_program != 0)
        {
            _gl.DeleteProgram(_program);
            _program = 0;
        }

        if (_animProgram != 0)
        {
            _gl.DeleteProgram(_animProgram);
            _animProgram = 0;
        }

        DeleteChunkCaches();

        if (ObjectLayer is not null)
        {
            ObjectLayer.Delete(_gl);
        }

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
            _vao = 0;
        }

        if (_hoverVbo != 0)
        {
            _gl.DeleteBuffer(_hoverVbo);
            _hoverVbo = 0;
        }
    }

    private void SetAttribPointers()
    {
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStrideBytes, IntPtr.Zero);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, VertexStrideBytes, (IntPtr)8);

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, VertexStrideBytes, (IntPtr)12);
    }

    private void UpdateChunkCache()
    {
        if (TileMap is not { } map)
        {
            return;
        }

        var expectedChunkRows = (map.Rows + TileBatcher.ChunkSize - 1) / TileBatcher.ChunkSize;
        var expectedChunkCols = (map.Cols + TileBatcher.ChunkSize - 1) / TileBatcher.ChunkSize;
        var shouldCreateFarLodCache = map.Rows > TileBatcher.ChunkSize || map.Cols > TileBatcher.ChunkSize;
        var cachesMissing =
            _detailCache is null ||
            _detailCache.ChunkRows != expectedChunkRows ||
            _detailCache.ChunkCols != expectedChunkCols ||
            (shouldCreateFarLodCache && (_farLodCache is null ||
                                         _farLodCache.ChunkRows != expectedChunkRows ||
                                         _farLodCache.ChunkCols != expectedChunkCols));

        if (cachesMissing ||
            _dirtyReason == VboDirtyReason.MapReplaced ||
            (!shouldCreateFarLodCache && _farLodCache is not null))
        {
            DeleteChunkCaches();
            _detailCache = ChunkCache.Create(_gl, map);
            _detailCache.RebuildAll(_gl, map, CameraRotationDegrees, RenderMode, ViewProjectionMode);

            if (shouldCreateFarLodCache)
            {
                _farLodCache = ChunkCache.Create(_gl, map);
                _farLodCache.RebuildAll(
                    _gl,
                    map,
                    CameraRotationDegrees,
                    RenderMode,
                    ViewProjectionMode,
                    lodBlockSize: TileBatcher.FarZoomLodBlockSize);
            }

            _dirtyReason = VboDirtyReason.None;
            return;
        }

        if (_dirtyReason == VboDirtyReason.ZoomChanged)
        {
            _detailCache?.RebuildAll(_gl, map, CameraRotationDegrees, RenderMode, ViewProjectionMode);
            _farLodCache?.RebuildAll(
                _gl,
                map,
                CameraRotationDegrees,
                RenderMode,
                ViewProjectionMode,
                lodBlockSize: TileBatcher.FarZoomLodBlockSize);
            _dirtyReason = VboDirtyReason.None;
            return;
        }

        if (_dirtyReason == VboDirtyReason.TileEdited)
        {
            _detailCache?.UploadDirtyChunks(_gl, map, CameraRotationDegrees, RenderMode, ViewProjectionMode);
            _farLodCache?.UploadDirtyChunks(
                _gl,
                map,
                CameraRotationDegrees,
                RenderMode,
                ViewProjectionMode,
                lodBlockSize: TileBatcher.FarZoomLodBlockSize);
            _dirtyReason = VboDirtyReason.None;
        }
    }

    private ChunkCache? GetActiveChunkCache()
    {
        return _lodBlockSize > 1 && _farLodCache is not null
            ? _farLodCache
            : _detailCache;
    }

    private unsafe void DrawHoveredTileHighlight(float logicalWidth, float logicalHeight)
    {
        if (TileMap is not { } map || HoveredTile is not { } hovered)
        {
            return;
        }

        if ((uint)hovered.Row >= (uint)map.Rows || (uint)hovered.Col >= (uint)map.Cols)
        {
            return;
        }

        var elev = map.Elevation[hovered.Row, hovered.Col];
        var depth = Math.Clamp(
            IsoMath.TileDepth(hovered.Col, hovered.Row, elev, Math.Max(map.Rows, map.Cols), CameraRotationDegrees) - HoverDepthBias,
            0f,
            1f);
        var topCorners = RenderMode == TerrainRenderMode.Voxel
            ? IsoMath.TopFaceCorners(hovered.Col, hovered.Row, elev, 1f, CameraRotationDegrees, ViewProjectionMode)
            : IsoMath.SmoothedTopFaceCorners(map, hovered.Col, hovered.Row, 1f, CameraRotationDegrees, ViewProjectionMode);
        var centre = (topCorners[0] + topCorners[1] + topCorners[2] + topCorners[3]) * 0.25f;
        var baseColour = TileColours.GetFaceColours(map.TileType[hovered.Row, hovered.Col], elev).top;
        var ringColour = Vector3.Min(new Vector3(1f, 1f, 1f), baseColour * 1.55f + new Vector3(0.12f, 0.10f, 0.02f));
        var fillColour = Vector3.Min(new Vector3(1f, 1f, 1f), baseColour * 1.18f + new Vector3(0.05f, 0.05f, 0.02f));
        var outer = InsetCorners(topCorners, centre, HoverOuterInset);
        var inner = InsetCorners(topCorners, centre, HoverInnerInset);
        var data = BuildHoverHighlightVertices(outer, inner, depth, ringColour, fillColour);

        fixed (float* dataPtr = data)
        {
            _gl.UseProgram(_program);
            _gl.Uniform2(_locViewport, new Vector2(logicalWidth, logicalHeight));
            _gl.Uniform2(_locPan, new Vector2(CameraPanX, CameraPanY));
            _gl.Uniform1(_locZoom, CameraZoom);
            _gl.Uniform1(_locTerrainBorderVisibility, GetTerrainBorderVisibility(CameraZoom, RenderMode));
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _hoverVbo);
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(data.Length * sizeof(float)),
                dataPtr,
                BufferUsageARB.DynamicDraw);
            SetAttribPointers();
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(data.Length / 6));
        }
    }

    private void UpdateFps()
    {
        _framesSinceFpsUpdate++;
        var elapsed = _fpsClock.Elapsed;

        if (elapsed.TotalSeconds < 0.5)
        {
            return;
        }

        SetCurrentValue(FpsProperty, _framesSinceFpsUpdate / elapsed.TotalSeconds);
        _framesSinceFpsUpdate = 0;
        _fpsClock.Restart();
    }

    private void ObserveTileMap(TileMap? map)
    {
        if (ReferenceEquals(_observedTileMap, map))
        {
            return;
        }

        if (_observedTileMap is not null)
        {
            _observedTileMap.TileChanged -= OnTileMapTileChanged;
        }

        _observedTileMap = map;

        if (_observedTileMap is not null)
        {
            _observedTileMap.TileChanged += OnTileMapTileChanged;
        }
    }

    private void ObserveObjectLayer(ObjectLayer? layer)
    {
        if (ReferenceEquals(_observedObjectLayer, layer))
        {
            return;
        }

        if (_observedObjectLayer is not null)
        {
            _observedObjectLayer.Changed -= OnObjectLayerChanged;
        }

        _observedObjectLayer = layer;

        if (_observedObjectLayer is not null)
        {
            _observedObjectLayer.Changed += OnObjectLayerChanged;
        }
    }

    private void OnTileMapTileChanged(int row, int col)
    {
        _detailCache?.MarkTileDirty(row, col);
        _farLodCache?.MarkTileDirty(row, col);
        SetDirty(VboDirtyReason.TileEdited);
    }

    private void DeleteChunkCaches()
    {
        if (_detailCache is not null)
        {
            _detailCache.Delete(_gl);
            _detailCache = null;
        }

        if (_farLodCache is not null)
        {
            _farLodCache.Delete(_gl);
            _farLodCache = null;
        }
    }

    private void OnObjectLayerChanged()
    {
        RequestNextFrameRendering();
    }

    private void SetDirty(VboDirtyReason reason)
    {
        if ((int)reason > (int)_dirtyReason)
        {
            _dirtyReason = reason;
        }

        RequestNextFrameRendering();
    }

    private void ClearMetrics()
    {
        SetCurrentValue(VisibleTilesProperty, 0);
        SetCurrentValue(VertexCountProperty, 0);
        SetCurrentValue(VisibleChunksProperty, 0);
        SetCurrentValue(RenderedTilesProperty, 0);
    }

    private void OnKeyTimerTick(object? sender, EventArgs e)
    {
        var changed = false;

        if (!_panning && _inertiaDelta.Length() > 0.5f)
        {
            ApplyPanDelta(_inertiaDelta.X, _inertiaDelta.Y);
            _inertiaDelta *= 0.88f;
            changed = true;
        }
        else if (!_panning && _inertiaDelta != Vector2.Zero)
        {
            _inertiaDelta = Vector2.Zero;
        }

        var speed = 6f / Math.Max(CameraZoom, 0.10f);
        var panX = 0f;
        var panY = 0f;

        if (IsHeld(Key.W) || IsHeld(Key.Up))
        {
            panY += speed;
        }

        if (IsHeld(Key.S) || IsHeld(Key.Down))
        {
            panY -= speed;
        }

        if (IsHeld(Key.A) || IsHeld(Key.Left))
        {
            panX += speed;
        }

        if (IsHeld(Key.D) || IsHeld(Key.Right))
        {
            panX -= speed;
        }

        if (panX != 0f || panY != 0f)
        {
            ApplyPanDelta(panX, panY);
            changed = true;
        }

        if (IsAnyZoomInHeld())
        {
            ApplyZoomAtPoint(IsoCamera.GetKeyboardZoomInFactor(), GetViewportCentre());
            changed = true;
        }

        if (IsAnyZoomOutHeld())
        {
            ApplyZoomAtPoint(IsoCamera.GetKeyboardZoomOutFactor(), GetViewportCentre());
            changed = true;
        }

        if (changed)
        {
            RequestNextFrameRendering();
        }
    }

    private bool ShouldKeepRendering()
    {
        return AnimationsEnabled ||
               ObjectLayer?.Dirty == true ||
               _panning ||
               _heldKeys.Count > 0 ||
               _inertiaDelta.Length() > 0.5f ||
               _dirtyReason != VboDirtyReason.None;
    }

    private void ApplyPanDelta(float deltaX, float deltaY)
    {
        if (deltaX == 0f && deltaY == 0f)
        {
            return;
        }

        SetCurrentValue(CameraPanXProperty, CameraPanX + deltaX);
        SetCurrentValue(CameraPanYProperty, CameraPanY + deltaY);

        if (!_panning)
        {
            UpdateHoveredTileFromLastPointer();
        }

        RequestNextFrameRendering();
    }

    private void ApplyZoomAtPoint(float factor, Vector2 screenPoint)
    {
        if (factor <= 0f)
        {
            return;
        }

        var currentZoom = CameraZoom;
        var nextZoom = IsoCamera.ClampZoom(currentZoom * factor);

        if (Math.Abs(nextZoom - currentZoom) < 0.0001f)
        {
            return;
        }

        var pan = new Vector2(CameraPanX, CameraPanY);
        var worldBefore = (screenPoint - pan) / currentZoom;

        SetCurrentValue(CameraZoomProperty, nextZoom);
        SetCurrentValue(CameraPanXProperty, screenPoint.X - (worldBefore.X * nextZoom));
        SetCurrentValue(CameraPanYProperty, screenPoint.Y - (worldBefore.Y * nextZoom));
        UpdateHoveredTileFromLastPointer();
        RequestNextFrameRendering();
    }

    private void ResetCamera()
    {
        _inertiaDelta = Vector2.Zero;
        SetCurrentValue(CameraRotationDegreesProperty, 0f);
        RequestNextFrameRendering();
    }

    private void RotateCameraFromKeyboard(float deltaDegrees)
    {
        if (ViewProjectionMode == ViewProjectionMode.ThreeD)
        {
            var stableRotation = IsoMath.SnapToStableObliqueRotationDegrees(CameraRotationDegrees);
            var stableDelta = deltaDegrees < 0f ? -90f : 90f;
            SetCurrentValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(stableRotation + stableDelta));
            return;
        }

        SetCurrentValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(CameraRotationDegrees + deltaDegrees));
    }

    private void RaiseTileClicked(Point position, MouseButton button)
    {
        if (!TryPickTile(position, out var col, out var row))
        {
            return;
        }

        var hovered = (col, row);
        SetCurrentValue(HoveredTileProperty, hovered);

        var args = new TileClickedEventArgs(col, row, button);
        TileClicked?.Invoke(this, args);

        if (TileClickedCommand is { } command && command.CanExecute(args))
        {
            command.Execute(args);
        }

        RequestNextFrameRendering();
    }

    private void UpdateHoveredTile(Point position)
    {
        if (TryPickTile(position, out var col, out var row))
        {
            SetCurrentValue(HoveredTileProperty, (col, row));
            return;
        }

        SetCurrentValue(HoveredTileProperty, null);
    }

    private void UpdateHoveredTileFromLastPointer()
    {
        if (!_hasPointer)
        {
            return;
        }

        UpdateHoveredTile(_lastMousePos);
    }

    private bool TryPickTile(Point position, out int col, out int row)
    {
        col = -1;
        row = -1;

        if (TileMap is not { } map)
        {
            return false;
        }

        var screenRelative = new Vector2(
            (float)position.X - CameraPanX,
            (float)position.Y - CameraPanY);
        return IsoMath.TryPickTile(map, screenRelative, CameraZoom, out col, out row, CameraRotationDegrees, ViewProjectionMode);
    }

    private Vector2 GetViewportCentre()
    {
        return new Vector2((float)Bounds.Width / 2f, (float)Bounds.Height / 2f);
    }

    private static Vector2 ToVector2(Point point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2[] InsetCorners(Vector2[] corners, Vector2 centre, float inset)
    {
        return
        [
            Vector2.Lerp(corners[0], centre, inset),
            Vector2.Lerp(corners[1], centre, inset),
            Vector2.Lerp(corners[2], centre, inset),
            Vector2.Lerp(corners[3], centre, inset),
        ];
    }

    private static float[] BuildHoverHighlightVertices(
        Vector2[] outer,
        Vector2[] inner,
        float depth,
        Vector3 ringColour,
        Vector3 fillColour)
    {
        var vertices = new List<float>(8 * 6 * 6);
        EmitQuad(vertices, outer[0], outer[1], inner[1], inner[0], depth, ringColour);
        EmitQuad(vertices, outer[1], outer[2], inner[2], inner[1], depth, ringColour);
        EmitQuad(vertices, outer[2], outer[3], inner[3], inner[2], depth, ringColour);
        EmitQuad(vertices, outer[3], outer[0], inner[0], inner[3], depth, ringColour);
        EmitQuad(vertices, inner[0], inner[1], inner[2], inner[3], depth, fillColour);
        return vertices.ToArray();
    }

    private static void EmitQuad(
        List<float> vertices,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float depth,
        Vector3 colour)
    {
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, b, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, d, depth, colour);
    }

    private static void EmitVertex(List<float> vertices, Vector2 point, float depth, Vector3 colour)
    {
        vertices.Add(point.X);
        vertices.Add(point.Y);
        vertices.Add(depth);
        vertices.Add(colour.X);
        vertices.Add(colour.Y);
        vertices.Add(colour.Z);
    }

    private bool IsHeld(Key key)
    {
        return _heldKeys.Contains(key);
    }

    private bool IsAnyZoomInHeld()
    {
        return _heldKeys.Any(IsZoomInKey);
    }

    private bool IsAnyZoomOutHeld()
    {
        return _heldKeys.Any(IsZoomOutKey);
    }

    private static bool IsMovementKey(Key key)
    {
        return key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right;
    }

    private static bool IsZoomInKey(Key key)
    {
        return key is Key.Add or Key.OemPlus;
    }

    private static bool IsZoomOutKey(Key key)
    {
        return key is Key.Subtract or Key.OemMinus;
    }

    internal static float GetTerrainBorderVisibility(float zoom, TerrainRenderMode renderMode)
    {
        if (renderMode != TerrainRenderMode.Voxel)
        {
            return 1f;
        }

        var normalized = Math.Clamp(
            (zoom - TerrainBorderFadeStartZoom) / (TerrainBorderFadeEndZoom - TerrainBorderFadeStartZoom),
            0f,
            1f);
        return normalized * normalized * (3f - (2f * normalized));
    }

    internal static int GetFarZoomLodBlockSize(float zoom, int currentLodBlockSize = 1)
    {
        if (currentLodBlockSize > 1)
        {
            return zoom <= FarZoomLodExitZoom ? currentLodBlockSize : 1;
        }

        return zoom < FarZoomLodEnterZoom ? TileBatcher.FarZoomLodBlockSize : 1;
    }

    internal static float GetWaterAnimationStrength(float zoom)
    {
        var normalized = Math.Clamp(
            (zoom - WaterAnimationFadeStartZoom) / (WaterAnimationFadeEndZoom - WaterAnimationFadeStartZoom),
            0f,
            1f);
        var eased = normalized * normalized * (3f - (2f * normalized));
        return WaterAnimationMinStrength + ((1f - WaterAnimationMinStrength) * eased);
    }

    internal static float GetWaterGridVisibility(float zoom)
    {
        var normalized = Math.Clamp(
            (zoom - WaterGridFadeStartZoom) / (WaterGridFadeEndZoom - WaterGridFadeStartZoom),
            0f,
            1f);
        return normalized * normalized * (3f - (2f * normalized));
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);
        var program = _gl.CreateProgram();

        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        if (_gl.GetProgram(program, ProgramPropertyARB.LinkStatus) == 0)
        {
            var infoLog = _gl.GetProgramInfoLog(program);
            _gl.DeleteProgram(program);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"OpenGL program link failed: {infoLog}");
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
        return program;
    }

    private uint CreateCompatibleProgram(
        string vertexSource,
        string fragmentSource,
        string vertexSourceGles,
        string fragmentSourceGles)
    {
        try
        {
            return CreateProgram(vertexSource, fragmentSource);
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Desktop GLSL shader path failed, trying OpenGL ES shader path: {ex.Message}");
            return CreateProgram(vertexSourceGles, fragmentSourceGles);
        }
    }

    private uint CompileShader(ShaderType shaderType, string source)
    {
        var shader = _gl.CreateShader(shaderType);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        if (_gl.GetShader(shader, ShaderParameterName.CompileStatus) == 0)
        {
            var infoLog = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"{shaderType} compilation failed: {infoLog}");
        }

        return shader;
    }

#if DEBUG
    private static void OnGlDebug(
        GLEnum source,
        GLEnum type,
        int id,
        GLEnum severity,
        int length,
        IntPtr message,
        IntPtr userParam)
    {
        var text = Marshal.PtrToStringAnsi(message, length) ?? string.Empty;
        Debug.WriteLine($"GL {source}/{type}/{severity} #{id}: {text}");
    }
#endif

    private const string VertSrc = """
        #version 330 core
        layout(location=0) in vec2  aScreen;
        layout(location=1) in float aDepth;
        layout(location=2) in vec3  aColour;
        uniform vec2  uViewport;
        uniform vec2  uPan;
        uniform float uZoom;
        uniform float uTerrainBorderVisibility;
        out vec3 vColour;
        void main() {
          vec2 pos = aScreen * uZoom + uPan;
          vec2 ndc = (pos / uViewport) * 2.0 - 1.0;
          ndc.y = -ndc.y;
          gl_Position = vec4(ndc, aDepth, 1.0);
          vec3 colour = aColour;
          if (aColour.x < 0.0) {
            vec3 fillColour = vec3(-aColour.x, aColour.y, aColour.z);
            vec3 borderColour = fillColour * vec3(0.62, 0.62, 0.62);
            colour = mix(fillColour, borderColour, uTerrainBorderVisibility);
          }
          vColour = colour;
        }
        """;

    private const string FragSrc = """
        #version 330 core
        in  vec3 vColour;
        out vec4 fragColour;
        void main() { fragColour = vec4(vColour, 1.0); }
        """;

    private const string AnimVertSrc = """
        #version 330 core
        layout(location=0) in vec2  aScreen;
        layout(location=1) in float aDepth;
        layout(location=2) in vec3  aColour;
        uniform vec2  uViewport;
        uniform vec2  uPan;
        uniform float uZoom;
        uniform float uTime;
        uniform float uWaterAnimationStrength;
        uniform float uWaterGridVisibility;
        out vec3 vColour;
        void main() {
          vec2 scaledScreen = aScreen * uZoom;
          vec2 shoreDir = aColour.xy;
          float rawBand = aColour.z;
          float marker = abs(rawBand);
          float shoreStrength = 0.0;
          float outerBand = 0.0;
          float innerBand = 0.0;
          float deepWaterMix = 0.0;

          if (rawBand < 0.0) {
            if (marker >= 10.0) {
              outerBand = 1.0;
              deepWaterMix = 1.0;
              shoreStrength = marker - 10.0;
            } else if (marker >= 8.0) {
              outerBand = 1.0;
              shoreStrength = marker - 8.0;
            } else if (marker >= 6.0) {
              innerBand = 1.0;
              deepWaterMix = 1.0;
              shoreStrength = marker - 6.0;
            } else if (marker >= 4.0) {
              innerBand = 1.0;
              shoreStrength = marker - 4.0;
            }
          } else if (marker >= 2.0) {
            deepWaterMix = 1.0;
            shoreStrength = marker - 2.0;
          } else {
            shoreStrength = marker;
          }

          shoreStrength = clamp(shoreStrength, 0.0, 1.0);
          innerBand *= uWaterGridVisibility;
          outerBand *= uWaterGridVisibility;

          if (dot(shoreDir, shoreDir) < 0.0001) {
            shoreDir = normalize(vec2(0.78, 0.62));
          } else {
            shoreDir = normalize(shoreDir);
          }

          float openRipple =
              sin(scaledScreen.x * 0.045 + uTime * 1.55) * 1.25 +
              sin(scaledScreen.y * 0.062 - uTime * 1.10) * 0.95 +
              sin((scaledScreen.x + scaledScreen.y) * 0.032 + uTime * 0.72) * 0.65;
          float chop =
              sin(scaledScreen.x * 0.11 - uTime * 2.6) *
              sin(scaledScreen.y * 0.09 + uTime * 1.9) * 0.35;
          float shorePhase = dot(scaledScreen, shoreDir * 0.075) - uTime * (3.0 + shoreStrength * 1.8);
          float shorePulse = sin(shorePhase) * 0.5 + 0.5;
          float shoreRipple = shoreStrength * (shorePulse * 1.8 + sin(shorePhase * 1.9 + 0.8) * 0.45);
          float ripple = (openRipple + chop + shoreRipple) * uWaterAnimationStrength;

          vec2 pos = scaledScreen + uPan + vec2(0.0, ripple);
          vec2 ndc = (pos / uViewport) * 2.0 - 1.0;
          ndc.y = -ndc.y;
          gl_Position = vec4(ndc, aDepth, 1.0);

          vec3 deepWater = vec3(0.10, 0.30, 0.54);
          vec3 shallowWater = vec3(0.30, 0.64, 0.84);
          vec3 foamColour = vec3(0.86, 0.93, 0.96);
          vec3 base = mix(shallowWater, deepWater, deepWaterMix);

          float shimmer = 0.04 * sin(scaledScreen.x * 0.035 + uTime * 2.4)
                        + 0.03 * sin((scaledScreen.x - scaledScreen.y) * 0.028 - uTime * 1.7);
          shimmer *= uWaterAnimationStrength;
          float highlight = 0.05 * max(0.0, sin((scaledScreen.x + scaledScreen.y) * 0.05 + uTime * 2.0))
                          * uWaterAnimationStrength;
          float foam = shoreStrength
                     * smoothstep(0.58, 0.92, shorePulse)
                     * (0.65 + 0.35 * sin(shorePhase * 0.7 + 1.4))
                     * uWaterAnimationStrength;

          vec3 colour = base
                      + vec3(-shimmer * 0.35, shimmer * 0.45, shimmer)
                      + vec3(highlight * 0.4, highlight * 0.6, highlight);
          colour = mix(colour, foamColour, foam * 0.75);
          vec3 outerBorderColour = mix(base, vec3(0.02, 0.11, 0.20), 0.78);
          vec3 innerBorderColour = mix(base, vec3(0.38, 0.72, 0.86), 0.62);
          innerBorderColour = mix(innerBorderColour, foamColour, shoreStrength * 0.18);
          outerBorderColour += vec3(-shimmer * 0.08, shimmer * 0.08, shimmer * 0.12);
          innerBorderColour += vec3(highlight * 0.18, highlight * 0.24, highlight * 0.28);
          colour = mix(colour, innerBorderColour, innerBand);
          colour = mix(colour, outerBorderColour, outerBand);
          vColour = clamp(colour, 0.0, 1.0);
        }
        """;

    private const string VertSrcGles = """
        #version 300 es
        precision highp float;
        layout(location=0) in vec2  aScreen;
        layout(location=1) in float aDepth;
        layout(location=2) in vec3  aColour;
        uniform vec2  uViewport;
        uniform vec2  uPan;
        uniform float uZoom;
        uniform float uTerrainBorderVisibility;
        out vec3 vColour;
        void main() {
          vec2 pos = aScreen * uZoom + uPan;
          vec2 ndc = (pos / uViewport) * 2.0 - 1.0;
          ndc.y = -ndc.y;
          gl_Position = vec4(ndc, aDepth, 1.0);
          vec3 colour = aColour;
          if (aColour.x < 0.0) {
            vec3 fillColour = vec3(-aColour.x, aColour.y, aColour.z);
            vec3 borderColour = fillColour * vec3(0.62, 0.62, 0.62);
            colour = mix(fillColour, borderColour, uTerrainBorderVisibility);
          }
          vColour = colour;
        }
        """;

    private const string AnimVertSrcGles = """
        #version 300 es
        precision highp float;
        layout(location=0) in vec2  aScreen;
        layout(location=1) in float aDepth;
        layout(location=2) in vec3  aColour;
        uniform vec2  uViewport;
        uniform vec2  uPan;
        uniform float uZoom;
        uniform float uTime;
        uniform float uWaterAnimationStrength;
        uniform float uWaterGridVisibility;
        out vec3 vColour;
        void main() {
          vec2 scaledScreen = aScreen * uZoom;
          vec2 shoreDir = aColour.xy;
          float rawBand = aColour.z;
          float marker = abs(rawBand);
          float shoreStrength = 0.0;
          float outerBand = 0.0;
          float innerBand = 0.0;
          float deepWaterMix = 0.0;

          if (rawBand < 0.0) {
            if (marker >= 10.0) {
              outerBand = 1.0;
              deepWaterMix = 1.0;
              shoreStrength = marker - 10.0;
            } else if (marker >= 8.0) {
              outerBand = 1.0;
              shoreStrength = marker - 8.0;
            } else if (marker >= 6.0) {
              innerBand = 1.0;
              deepWaterMix = 1.0;
              shoreStrength = marker - 6.0;
            } else if (marker >= 4.0) {
              innerBand = 1.0;
              shoreStrength = marker - 4.0;
            }
          } else if (marker >= 2.0) {
            deepWaterMix = 1.0;
            shoreStrength = marker - 2.0;
          } else {
            shoreStrength = marker;
          }

          shoreStrength = clamp(shoreStrength, 0.0, 1.0);
          innerBand *= uWaterGridVisibility;
          outerBand *= uWaterGridVisibility;

          if (dot(shoreDir, shoreDir) < 0.0001) {
            shoreDir = normalize(vec2(0.78, 0.62));
          } else {
            shoreDir = normalize(shoreDir);
          }

          float openRipple =
              sin(scaledScreen.x * 0.045 + uTime * 1.55) * 1.25 +
              sin(scaledScreen.y * 0.062 - uTime * 1.10) * 0.95 +
              sin((scaledScreen.x + scaledScreen.y) * 0.032 + uTime * 0.72) * 0.65;
          float chop =
              sin(scaledScreen.x * 0.11 - uTime * 2.6) *
              sin(scaledScreen.y * 0.09 + uTime * 1.9) * 0.35;
          float shorePhase = dot(scaledScreen, shoreDir * 0.075) - uTime * (3.0 + shoreStrength * 1.8);
          float shorePulse = sin(shorePhase) * 0.5 + 0.5;
          float shoreRipple = shoreStrength * (shorePulse * 1.8 + sin(shorePhase * 1.9 + 0.8) * 0.45);
          float ripple = (openRipple + chop + shoreRipple) * uWaterAnimationStrength;

          vec2 pos = scaledScreen + uPan + vec2(0.0, ripple);
          vec2 ndc = (pos / uViewport) * 2.0 - 1.0;
          ndc.y = -ndc.y;
          gl_Position = vec4(ndc, aDepth, 1.0);

          vec3 deepWater = vec3(0.10, 0.30, 0.54);
          vec3 shallowWater = vec3(0.30, 0.64, 0.84);
          vec3 foamColour = vec3(0.86, 0.93, 0.96);
          vec3 base = mix(shallowWater, deepWater, deepWaterMix);

          float shimmer = 0.04 * sin(scaledScreen.x * 0.035 + uTime * 2.4)
                        + 0.03 * sin((scaledScreen.x - scaledScreen.y) * 0.028 - uTime * 1.7);
          shimmer *= uWaterAnimationStrength;
          float highlight = 0.05 * max(0.0, sin((scaledScreen.x + scaledScreen.y) * 0.05 + uTime * 2.0))
                          * uWaterAnimationStrength;
          float foam = shoreStrength
                     * smoothstep(0.58, 0.92, shorePulse)
                     * (0.65 + 0.35 * sin(shorePhase * 0.7 + 1.4))
                     * uWaterAnimationStrength;

          vec3 colour = base
                      + vec3(-shimmer * 0.35, shimmer * 0.45, shimmer)
                      + vec3(highlight * 0.4, highlight * 0.6, highlight);
          colour = mix(colour, foamColour, foam * 0.75);
          vec3 outerBorderColour = mix(base, vec3(0.02, 0.11, 0.20), 0.78);
          vec3 innerBorderColour = mix(base, vec3(0.38, 0.72, 0.86), 0.62);
          innerBorderColour = mix(innerBorderColour, foamColour, shoreStrength * 0.18);
          outerBorderColour += vec3(-shimmer * 0.08, shimmer * 0.08, shimmer * 0.12);
          innerBorderColour += vec3(highlight * 0.18, highlight * 0.24, highlight * 0.28);
          colour = mix(colour, innerBorderColour, innerBand);
          colour = mix(colour, outerBorderColour, outerBand);
          vColour = clamp(colour, 0.0, 1.0);
        }
        """;

    private const string FragSrcGles = """
        #version 300 es
        precision highp float;
        in  vec3 vColour;
        out vec4 fragColour;
        void main() { fragColour = vec4(vColour, 1.0); }
        """;
}
