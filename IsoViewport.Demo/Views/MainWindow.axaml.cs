using Avalonia.Controls;
using IsoViewport.Demo.ViewModels;

namespace IsoViewport.Demo.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
