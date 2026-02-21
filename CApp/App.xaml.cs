using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;
public partial class App : Application
{
    private SimpleApiServer? _server;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = new();
        window.Activate();

        string serverUri = "http://localhost:30078/";
        window.ServerUri = serverUri;
        _server = new SimpleApiServer(serverUri);
        
        var settings = await ApiSettingsManager.LoadAsync();
        await _server.InitializeSettingsAsync(settings);
        
        _server.Start();

        window.Closed += MainWindow_Closed;
    }

    public SimpleApiServer? Server => _server;

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        _server?.Dispose();
        _server = null;
    }
}
