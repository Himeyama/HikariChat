using System;
using Microsoft.UI.Xaml;

namespace CApp;
public partial class App : Application
{
    private SimpleApiServer? _server;
    
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = new();
        window.Activate();

        string serverUri = "http://localhost:30078/";
        window.ServerUri = serverUri;
        _server = new SimpleApiServer(serverUri);
        _server.Start();

        window.Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        _server?.Dispose();
        _server = null;
    }
}