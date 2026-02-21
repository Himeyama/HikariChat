using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;
public partial class App : Application
{
    private SimpleApiServer? _server;
    private McpClient? _mcpClient;
    private McpSettings? _mcpSettings;

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
        _server.Start();

        // 設定をロードして MCP クライアントを初期化
        // UI スレッドをブロックしないように Task.Run で実行
        _ = LoadSettingsAndInitializeMcpAsync();

        window.Closed += MainWindow_Closed;
    }

    private async Task LoadSettingsAndInitializeMcpAsync()
    {
        try
        {
            DebugLogger.Info("Loading settings...");
            var settings = await ApiSettingsManager.LoadAsync();
            _mcpSettings = settings.Mcp;

            DebugLogger.Info($"MCP settings: enabled={_mcpSettings?.Enabled}, servers={_mcpSettings?.McpServers?.Count ?? 0}");

            if (_mcpSettings != null && _mcpSettings.Enabled && _mcpSettings.McpServers.Count > 0)
            {
                DebugLogger.Info("Initializing MCP client...");
                await InitializeMcpClientAsync(_mcpSettings);
                DebugLogger.Info("MCP client initialized");
            }
            else
            {
                if (_mcpSettings == null)
                {
                    DebugLogger.Info("MCP settings is null");
                }
                else if (!_mcpSettings.Enabled)
                {
                    DebugLogger.Info("MCP is disabled");
                }
                else if (_mcpSettings.McpServers.Count == 0)
                {
                    DebugLogger.Info("No MCP servers configured");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error($"Failed to load settings: {ex.Message}", ex);
        }
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        _server?.Dispose();
        _server = null;
        _mcpClient?.Dispose();
        _mcpClient = null;
    }

    /// <summary>
    /// MCP クライアントを初期化
    /// </summary>
    public async Task InitializeMcpClientAsync(McpSettings settings)
    {
        if (_mcpClient != null)
        {
            _mcpClient.Dispose();
        }

        _mcpClient = new McpClient();
        await _mcpClient.InitializeAsync(settings);
    }

    /// <summary>
    /// MCP クライアントを取得
    /// </summary>
    public McpClient? GetMcpClient()
    {
        if (_mcpClient == null)
        {
            DebugLogger.Info("GetMcpClient called but MCP client is null");
            DebugLogger.Info($"MCP settings: enabled={_mcpSettings?.Enabled}, servers={_mcpSettings?.McpServers?.Count ?? 0}");
        }
        return _mcpClient;
    }

    /// <summary>
    /// MCP 設定を取得
    /// </summary>
    public McpSettings? GetMcpSettings()
    {
        return _mcpSettings;
    }
}