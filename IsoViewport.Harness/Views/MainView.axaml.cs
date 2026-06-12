using Avalonia.Controls;
using IsoViewport.Harness.ViewModels;

namespace IsoViewport.Harness.Views;

public sealed partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
