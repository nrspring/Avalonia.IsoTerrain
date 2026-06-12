namespace IsoViewport.Controls.Contracts;

public class ObservableMapPieceTypeDefinition : ObservableViewportObject, IMapPieceTypeDefinition
{
    private string _typeId = string.Empty;
    private string _displayName = string.Empty;
    private int _defaultZLayer;
    private IMapPieceRenderer _renderer = NullMapPieceRenderer.Instance;

    public ObservableMapPieceTypeDefinition()
    {
    }

    public ObservableMapPieceTypeDefinition(
        string typeId,
        string displayName,
        int defaultZLayer,
        IMapPieceRenderer renderer)
    {
        _typeId = typeId;
        _displayName = displayName;
        _defaultZLayer = defaultZLayer;
        _renderer = renderer;
    }

    public string TypeId
    {
        get => _typeId;
        set => SetProperty(ref _typeId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public int DefaultZLayer
    {
        get => _defaultZLayer;
        set => SetProperty(ref _defaultZLayer, value);
    }

    public IMapPieceRenderer Renderer
    {
        get => _renderer;
        set => SetProperty(ref _renderer, value);
    }
}
