using System.Numerics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Controls;

public sealed class IsoInputOverlay : Border
{
    private const float KeyboardRotationStepDegrees = 45f;
    private const double ClickTravelThreshold = 4d;

    public static readonly StyledProperty<TileMap?> TileMapProperty =
        AvaloniaProperty.Register<IsoInputOverlay, TileMap?>(nameof(TileMap));

    public static readonly StyledProperty<float> CameraZoomProperty =
        AvaloniaProperty.Register<IsoInputOverlay, float>(nameof(CameraZoom), 1.0f);

    public static readonly StyledProperty<float> CameraPanXProperty =
        AvaloniaProperty.Register<IsoInputOverlay, float>(nameof(CameraPanX), 0f);

    public static readonly StyledProperty<float> CameraPanYProperty =
        AvaloniaProperty.Register<IsoInputOverlay, float>(nameof(CameraPanY), 0f);

    public static readonly StyledProperty<float> CameraRotationDegreesProperty =
        AvaloniaProperty.Register<IsoInputOverlay, float>(nameof(CameraRotationDegrees), 0f);

    public static readonly StyledProperty<ViewProjectionMode> ViewProjectionModeProperty =
        AvaloniaProperty.Register<IsoInputOverlay, ViewProjectionMode>(nameof(ViewProjectionMode), ViewProjectionMode.ThreeD);

    public static readonly StyledProperty<(int Col, int Row)?> HoveredTileProperty =
        AvaloniaProperty.Register<IsoInputOverlay, (int Col, int Row)?>(nameof(HoveredTile));

    public static readonly StyledProperty<ICommand?> TileClickedCommandProperty =
        AvaloniaProperty.Register<IsoInputOverlay, ICommand?>(nameof(TileClickedCommand));

    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<IsoInputOverlay, bool>(nameof(AnimationsEnabled), true);

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<IsoInputOverlay, double>(nameof(ViewportWidth), 0d);

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<IsoInputOverlay, double>(nameof(ViewportHeight), 0d);

    private Point _lastMousePos;
    private bool _panning;
    private MouseButton? _panningButton;
    private double _panTravel;
    private double _pressTravel;
    private (int Col, int Row)? _pressTile;
    private MouseButton? _pressButton;
    private KeyModifiers _pressModifiers;
    private KeyModifiers _lastHoverModifiers;
    private bool _hasPointer;
    private Vector2 _inertiaDelta;
    private bool _fitOnNextLayout;
    private bool _autoFitCamera = true;
    private double _lastFittedViewportWidth = double.NaN;
    private double _lastFittedViewportHeight = double.NaN;
    private readonly HashSet<Key> _heldKeys = [];
    private readonly DispatcherTimer _keyTimer;

    public IsoInputOverlay()
    {
        Background = Brushes.Transparent;
        Focusable = true;
        LayoutUpdated += (_, _) =>
        {
            if (!_autoFitCamera)
            {
                return;
            }

            var width = ViewportWidth > 0d ? ViewportWidth : Bounds.Width;
            var height = ViewportHeight > 0d ? ViewportHeight : Bounds.Height;

            if (width <= 0d || height <= 0d)
            {
                return;
            }

            if (Math.Abs(width - _lastFittedViewportWidth) > 0.5d ||
                Math.Abs(height - _lastFittedViewportHeight) > 0.5d)
            {
                TryFitViewToMap();
            }
        };
        LostFocus += (_, _) =>
        {
            _heldKeys.Clear();
            _panning = false;
            _panningButton = null;
            _panTravel = 0d;
            ClearPressState();
            SetHoveredTile(null, KeyModifiers.None);
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

    internal event EventHandler<TileHoverChangedEventArgs>? TileHovered;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TileMapProperty)
        {
            _autoFitCamera = true;
            _lastFittedViewportWidth = double.NaN;
            _lastFittedViewportHeight = double.NaN;
            SetHoveredTile(null, KeyModifiers.None);
            TryFitViewToMap();
        }
        else if (change.Property == CameraRotationDegreesProperty)
        {
            var oldRotation = change.OldValue is float oldValue ? oldValue : 0f;
            var newRotation = change.NewValue is float newValue ? IsoMath.NormalizeRotationDegrees(newValue) : 0f;

            if (change.NewValue is float rawValue && Math.Abs(rawValue - newRotation) > 0.0001f)
            {
                SetCurrentValue(CameraRotationDegreesProperty, newRotation);
                return;
            }

            if (Math.Abs(newRotation - oldRotation) < 0.0001f)
            {
                return;
            }

            if (_autoFitCamera || _fitOnNextLayout)
            {
                _lastFittedViewportWidth = double.NaN;
                _lastFittedViewportHeight = double.NaN;
                TryFitViewToMap();
            }
            else
            {
                AnchorViewTransformAtViewportCentre(oldRotation, newRotation, ViewProjectionMode, ViewProjectionMode);
            }

            UpdateHoveredTileFromLastPointer();
        }
        else if (change.Property == ViewProjectionModeProperty)
        {
            var oldMode = change.OldValue is ViewProjectionMode oldValue ? oldValue : global::IsoViewport.Controls.Rendering.ViewProjectionMode.ThreeD;
            var newMode = change.NewValue is ViewProjectionMode newValue ? newValue : global::IsoViewport.Controls.Rendering.ViewProjectionMode.ThreeD;

            if (oldMode == newMode)
            {
                return;
            }

            if (_autoFitCamera || _fitOnNextLayout)
            {
                _lastFittedViewportWidth = double.NaN;
                _lastFittedViewportHeight = double.NaN;
                TryFitViewToMap();
            }
            else
            {
                AnchorViewTransformAtViewportCentre(CameraRotationDegrees, CameraRotationDegrees, oldMode, newMode);
            }

            UpdateHoveredTileFromLastPointer();
        }
        else if ((change.Property == ViewportWidthProperty || change.Property == ViewportHeightProperty) &&
                 (_fitOnNextLayout || _autoFitCamera))
        {
            TryFitViewToMap();
        }

        if (change.Property == TileMapProperty ||
            change.Property == CameraZoomProperty ||
            change.Property == CameraPanXProperty ||
            change.Property == CameraPanYProperty ||
            change.Property == CameraRotationDegreesProperty ||
            change.Property == ViewProjectionModeProperty ||
            change.Property == ViewportWidthProperty ||
            change.Property == ViewportHeightProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var position = e.GetPosition(this);
        _lastMousePos = position;
        _hasPointer = true;
        var currentTile = UpdateHoveredTile(position, e.KeyModifiers);

        var point = e.GetCurrentPoint(this);
        var button = GetPressedButton(point);
        _pressTile = currentTile;
        _pressButton = button;
        _pressModifiers = e.KeyModifiers;
        _pressTravel = 0d;

        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _panningButton = point.Properties.IsMiddleButtonPressed ? MouseButton.Middle : MouseButton.Right;
            _panTravel = 0d;
            _inertiaDelta = Vector2.Zero;
            SetHoveredTile(null, e.KeyModifiers);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _lastMousePos = e.GetPosition(this);
        var releaseTile = TryGetTile(_lastMousePos);

        if (_panning)
        {
            var panningButton = _panningButton;
            var shouldClick = panningButton.HasValue &&
                _pressButton == panningButton.Value &&
                _panTravel < ClickTravelThreshold &&
                _pressTile.HasValue &&
                _pressTile == releaseTile;
            _panning = false;
            _panningButton = null;
            e.Pointer.Capture(null);

            if (shouldClick)
            {
                RaiseTileClicked(releaseTile!.Value, panningButton!.Value, e.KeyModifiers);
            }
            else
            {
                UpdateHoveredTile(_lastMousePos, e.KeyModifiers);
            }

            ClearPressState();
            e.Handled = true;
            return;
        }

        if (_pressButton == MouseButton.Left)
        {
            var shouldClick = _pressTravel < ClickTravelThreshold &&
                _pressTile.HasValue &&
                _pressTile == releaseTile;

            if (shouldClick)
            {
                RaiseTileClicked(releaseTile!.Value, MouseButton.Left, e.KeyModifiers);
            }
            else
            {
                UpdateHoveredTile(_lastMousePos, e.KeyModifiers);
            }

            ClearPressState();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        _hasPointer = true;

        if (_panning)
        {
            var delta = position - _lastMousePos;
            _panTravel += Math.Abs(delta.X) + Math.Abs(delta.Y);
            ApplyPanDelta((float)delta.X, (float)delta.Y);
            _inertiaDelta = new Vector2((float)delta.X, (float)delta.Y);
            SetHoveredTile(null, e.KeyModifiers);
            _lastMousePos = position;
            return;
        }

        if (_pressButton == MouseButton.Left)
        {
            var delta = position - _lastMousePos;
            _pressTravel += Math.Abs(delta.X) + Math.Abs(delta.Y);
        }

        _lastMousePos = position;
        UpdateHoveredTile(position, e.KeyModifiers);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var position = e.GetPosition(this);
        _lastMousePos = position;
        _hasPointer = true;
        ApplyZoomAtPoint(IsoCamera.GetWheelZoomFactor((float)e.Delta.Y), ToVector2(position));
        UpdateHoveredTile(position, e.KeyModifiers);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hasPointer = false;
        SetHoveredTile(null, e.KeyModifiers);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

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
            RotateCamera(-KeyboardRotationStepDegrees);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E)
        {
            RotateCamera(KeyboardRotationStepDegrees);
            e.Handled = true;
            return;
        }

        if (IsMovementKey(e.Key) || IsZoomInKey(e.Key) || IsZoomOutKey(e.Key))
        {
            _heldKeys.Add(e.Key);
            e.Handled = true;
        }

        UpdateHoverModifiers(e.KeyModifiers);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_heldKeys.Remove(e.Key))
        {
            e.Handled = true;
        }

        UpdateHoverModifiers(e.KeyModifiers);
    }

    private void OnKeyTimerTick(object? sender, EventArgs e)
    {
        if (!_hasPointer && _heldKeys.Count == 0 && (!_panning && _inertiaDelta.Length() <= 0.5f))
        {
            return;
        }

        if (!_panning && _inertiaDelta.Length() > 0.5f)
        {
            ApplyPanDelta(_inertiaDelta.X, _inertiaDelta.Y);
            _inertiaDelta *= 0.88f;
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
        }

        if (IsAnyZoomInHeld())
        {
            ApplyZoomAtPoint(IsoCamera.GetKeyboardZoomInFactor(), GetViewportCentre());
        }

        if (IsAnyZoomOutHeld())
        {
            ApplyZoomAtPoint(IsoCamera.GetKeyboardZoomOutFactor(), GetViewportCentre());
        }
    }

    private void ApplyPanDelta(float deltaX, float deltaY)
    {
        if (deltaX == 0f && deltaY == 0f)
        {
            return;
        }

        _autoFitCamera = false;
        _lastFittedViewportWidth = double.NaN;
        _lastFittedViewportHeight = double.NaN;
        SetCurrentValue(CameraPanXProperty, CameraPanX + deltaX);
        SetCurrentValue(CameraPanYProperty, CameraPanY + deltaY);

        if (!_panning)
        {
            UpdateHoveredTileFromLastPointer();
        }
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

        _autoFitCamera = false;
        _lastFittedViewportWidth = double.NaN;
        _lastFittedViewportHeight = double.NaN;
        var pan = new Vector2(CameraPanX, CameraPanY);
        var worldBefore = (screenPoint - pan) / currentZoom;

        SetCurrentValue(CameraZoomProperty, nextZoom);
        SetCurrentValue(CameraPanXProperty, screenPoint.X - (worldBefore.X * nextZoom));
        SetCurrentValue(CameraPanYProperty, screenPoint.Y - (worldBefore.Y * nextZoom));
        UpdateHoveredTileFromLastPointer();
    }

    private void ResetCamera()
    {
        _inertiaDelta = Vector2.Zero;
        _autoFitCamera = true;
        _lastFittedViewportWidth = double.NaN;
        _lastFittedViewportHeight = double.NaN;
        SetCurrentValue(CameraRotationDegreesProperty, 0f);
        TryFitViewToMap();
    }

    private void TryFitViewToMap()
    {
        if (TileMap is not { } map)
        {
            _fitOnNextLayout = false;
            return;
        }

        var viewportWidth = ViewportWidth > 0d ? (float)ViewportWidth : (float)Bounds.Width;
        var viewportHeight = ViewportHeight > 0d ? (float)ViewportHeight : (float)Bounds.Height;

        if (viewportWidth <= 0f || viewportHeight <= 0f)
        {
            _fitOnNextLayout = true;
            return;
        }

        var fitted = IsoMath.FitMapToViewport(map, viewportWidth, viewportHeight, 0f, CameraRotationDegrees, ViewProjectionMode);
        SetCurrentValue(CameraZoomProperty, fitted.Zoom);
        SetCurrentValue(CameraPanXProperty, fitted.PanX);
        SetCurrentValue(CameraPanYProperty, fitted.PanY);
        _lastFittedViewportWidth = viewportWidth;
        _lastFittedViewportHeight = viewportHeight;
        _fitOnNextLayout = false;
        UpdateHoveredTileFromLastPointer();
    }

    private void RaiseTileClicked((int Col, int Row) tile, MouseButton button, KeyModifiers keyModifiers)
    {
        SetHoveredTile(tile, keyModifiers);
        var args = new TileClickedEventArgs(tile.Col, tile.Row, button, keyModifiers);
        TileClicked?.Invoke(this, args);

        if (TileClickedCommand is { } command && command.CanExecute(args))
        {
            command.Execute(args);
        }
    }

    private (int Col, int Row)? UpdateHoveredTile(Point position, KeyModifiers keyModifiers)
    {
        if (TryPickTile(position, out var col, out var row))
        {
            var tile = (col, row);
            SetHoveredTile(tile, keyModifiers);
            return tile;
        }

        SetHoveredTile(null, keyModifiers);
        return null;
    }

    private void UpdateHoveredTileFromLastPointer()
    {
        if (_hasPointer)
        {
            UpdateHoveredTile(_lastMousePos, _lastHoverModifiers);
        }
    }

    private void SetHoveredTile((int Col, int Row)? tile, KeyModifiers keyModifiers)
    {
        if (HoveredTile == tile && _lastHoverModifiers == keyModifiers)
        {
            return;
        }

        _lastHoverModifiers = keyModifiers;
        SetCurrentValue(HoveredTileProperty, tile);
        TileHovered?.Invoke(this, new TileHoverChangedEventArgs(tile, keyModifiers));
    }

    private void UpdateHoverModifiers(KeyModifiers keyModifiers)
    {
        if (HoveredTile is null || _lastHoverModifiers == keyModifiers)
        {
            return;
        }

        SetHoveredTile(HoveredTile, keyModifiers);
    }

    private (int Col, int Row)? TryGetTile(Point position)
    {
        return TryPickTile(position, out var col, out var row)
            ? (col, row)
            : null;
    }

    private void ClearPressState()
    {
        _pressTile = null;
        _pressButton = null;
        _pressModifiers = KeyModifiers.None;
        _pressTravel = 0d;
    }

    private static MouseButton? GetPressedButton(PointerPoint point)
    {
        if (point.Properties.IsLeftButtonPressed)
        {
            return MouseButton.Left;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            return MouseButton.Right;
        }

        if (point.Properties.IsMiddleButtonPressed)
        {
            return MouseButton.Middle;
        }

        return null;
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

    private void RotateCamera(float deltaDegrees)
    {
        _autoFitCamera = false;
        _lastFittedViewportWidth = double.NaN;
        _lastFittedViewportHeight = double.NaN;

        if (ViewProjectionMode == ViewProjectionMode.ThreeD)
        {
            var stableRotation = IsoMath.SnapToStableObliqueRotationDegrees(CameraRotationDegrees);
            var stableDelta = deltaDegrees < 0f ? -90f : 90f;
            SetCurrentValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(stableRotation + stableDelta));
            return;
        }

        SetCurrentValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(CameraRotationDegrees + deltaDegrees));
    }

    private void AnchorViewTransformAtViewportCentre(
        float oldRotation,
        float newRotation,
        ViewProjectionMode oldProjectionMode,
        ViewProjectionMode newProjectionMode)
    {
        if (TileMap is null)
        {
            return;
        }

        var screenPoint = GetViewportCentre();
        var pan = new Vector2(CameraPanX, CameraPanY);
        var groundBefore = IsoMath.ScreenToTile(screenPoint - pan, CameraZoom, oldRotation, oldProjectionMode);
        var projectedAfter = IsoMath.TileToScreen(groundBefore.X, groundBefore.Y, 0f, newRotation, newProjectionMode) * CameraZoom;
        SetCurrentValue(CameraPanXProperty, screenPoint.X - projectedAfter.X);
        SetCurrentValue(CameraPanYProperty, screenPoint.Y - projectedAfter.Y);
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

}
