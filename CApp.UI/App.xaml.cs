using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;

public partial class App : Application
{
    private CApp.Server.SimpleApiServer? _server;
    private MainWindow? _mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        string serverUri = "http://localhost:30078/";
        _mainWindow.ServerUri = serverUri;
        _server = new CApp.Server.SimpleApiServer(serverUri);

        // デリゲートを設定
        _server.ExecuteScriptAsync = async (script) => await _mainWindow.ExecuteScriptAsync(script);
        _server.GetChatHistoryAsync = async () => await _mainWindow.GetChatHistoryAsync();

        CApp.Server.DebugLogger.Mcp("App.OnLaunched: Loading settings and initializing server...");
        var settings = await CApp.Server.ApiSettingsManager.LoadAsync();
        CApp.Server.DebugLogger.Mcp($"App.OnLaunched: Settings loaded. McpEnabled={settings.McpEnabled}, McpServers.Count={settings.McpServers.Count}");
        
        await _server.InitializeSettingsAsync(settings);
        CApp.Server.DebugLogger.Mcp("App.OnLaunched: Server initialized.");

        _server.Start();

        _mainWindow.Closed += MainWindow_Closed;
    }

    public CApp.Server.SimpleApiServer? Server => _server;

    public MainWindow? MainWindow => _mainWindow;

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        _server?.Dispose();
        _server = null;
        _mainWindow = null;
    }
}
