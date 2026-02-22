using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private Server.McpManager? _mcpManager;
    private Server.SimpleApiServer? _apiServer;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mcpManager = new Server.McpManager();
        
        // 設定を読み込んで MCP サーバーを起動
        var settings = await Server.ApiSettingsManager.LoadAsync();
        LogInfo($"OnLaunched: Loading settings. McpEnabled={settings.McpEnabled}, McpServers.Count={settings.McpServers.Count}");
        await _mcpManager.UpdateSettingsAsync(settings);
        LogInfo($"OnLaunched: MCP Manager updated. Status={_mcpManager.GetStatus()}");
        
        // API サーバーを起動（McpManager を共有）
        _apiServer = new Server.SimpleApiServer("http://localhost:51234/", _mcpManager);
        _apiServer.ExecuteScriptAsync = (script) => _mainWindow?.ExecuteScriptAsync(script);
        _apiServer.GetChatHistoryAsync = () => _mainWindow?.GetChatHistoryAsync()!;
        _apiServer.Start();
        LogInfo($"OnLaunched: API Server started");
        
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        LogInfo($"OnLaunched: MainWindow activated");
    }

    public MainWindow? MainWindow => _mainWindow;

    public (bool enabled, int activeCount, int totalCount) GetMcpStatus()
    {
        return _mcpManager?.GetStatus() ?? (false, 0, 0);
    }

    public async Task UpdateMcpSettingsAsync(Server.ApiSettings settings)
    {
        if (_mcpManager != null)
        {
            await _mcpManager.UpdateSettingsAsync(settings);
        }
    }

    private void LogInfo(string message)
    {
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"{time} [INFO] {message}{Environment.NewLine}";
        System.IO.File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
    }
}
