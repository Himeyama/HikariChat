using System;
using System.Net;
using System.Threading.Tasks;
using HikariChat.Server;
using Microsoft.UI.Xaml;

namespace HikariChat;

#pragma warning disable CS8603, CS8619

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private McpManager? _mcpManager;
    private SimpleApiServer? _apiServer;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mcpManager = new McpManager();

        // 設定を読み込んで MCP サーバーを起動
        ApiSettings settings = await ApiSettingsManager.LoadAsync();
        LogInfo($"OnLaunched: Loading settings. McpEnabled={settings.McpEnabled}, McpServers.Count={settings.McpServers.Count}");
        await _mcpManager.UpdateSettingsAsync(settings);
        LogInfo($"OnLaunched: MCP Manager updated. Status={_mcpManager.GetStatus()}");

        // MainWindow を先に作成・表示
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        LogInfo($"OnLaunched: MainWindow activated");

        // API サーバーを起動（McpManager を共有）
        _apiServer = new SimpleApiServer("http://localhost:29000/", _mcpManager)
        {
            GetChatHistoryAsync = () =>
            {
                if (_mainWindow == null) return Task.FromResult<string?>(null);
                return _mainWindow.GetChatHistoryAsync();
            }
        };
        _apiServer.ExecuteScriptAsync = (script) => _mainWindow?.ExecuteScriptAsync(script);

        try
        {
            _apiServer.Start();
            LogInfo($"OnLaunched: API Server started");
        }
        catch (HttpListenerException ex)
        {
            LogInfo($"OnLaunched: API Server failed to start: {ex.Message} (Code={ex.ErrorCode})");
            await _mainWindow.ShowStartupError(
                "APIサーバーを起動できませんでした",
                $"ポート 29000 が使用できません。\n" +
                $"Windows によってポートが予約されている可能性があります。\n\n" +
                $"MCP 機能は利用できません。\n\n" +
                $"【確認方法】 コマンドプロンプト（管理者）で:\n" +
                $"netsh int ipv4 show excludedportrange protocol=tcp\n\n" +
                $"エラーコード: {ex.ErrorCode}"
            );
            MainWindow?.Close();
        }
    }

    public MainWindow? MainWindow => _mainWindow;

    public (bool enabled, int activeCount, int totalCount) GetMcpStatus()
    {
        return _mcpManager?.GetStatus() ?? (false, 0, 0);
    }

    public async Task UpdateMcpSettingsAsync(ApiSettings settings)
    {
        if (_mcpManager != null)
        {
            await _mcpManager.UpdateSettingsAsync(settings);
        }
    }

    private void LogInfo(string message)
    {
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"{time} [INFO] {message}{Environment.NewLine}";
        System.IO.File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
    }
}

#pragma warning restore CS8603, CS8619
