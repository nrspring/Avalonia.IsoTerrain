namespace IsoViewport.Controls.Rendering;

public sealed class IsoCamera
{
    public const float MinZoom = 0.01f;
    public const float MaxZoom = 4.0f;
    public const float MouseWheelZoomBase = 1.08f;
    public const float KeyboardZoomBase = 1.02f;

    private float _zoom = 1f;

    public float PanX { get; set; }

    public float PanY { get; set; }

    public float Zoom
    {
        get => _zoom;
        set => _zoom = ClampZoom(value);
    }

    public static float ClampZoom(float zoom)
    {
        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public static float GetWheelZoomFactor(float delta)
    {
        return MathF.Pow(MouseWheelZoomBase, delta);
    }

    public static float GetKeyboardZoomInFactor()
    {
        return KeyboardZoomBase;
    }

    public static float GetKeyboardZoomOutFactor()
    {
        return 1f / KeyboardZoomBase;
    }
}
