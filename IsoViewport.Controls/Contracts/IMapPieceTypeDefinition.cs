using System.ComponentModel;

namespace IsoViewport.Controls.Contracts;

public interface IMapPieceTypeDefinition : INotifyPropertyChanged
{
    string TypeId { get; }

    string DisplayName { get; }

    int DefaultZLayer { get; }

    IMapPieceRenderer Renderer { get; }
}
