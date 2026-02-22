using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    public MainWindow? MainWindow => _mainWindow;
}
