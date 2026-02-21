using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WinRT.Interop;

namespace CApp;

public sealed partial class SettingsWindow : Window
{
    OverlappedPresenter? presenter;
    MainWindow? mainWindow;

    public string SettingsUri { get; set; } = "";

    public SettingsWindow(MainWindow? mainWindow = null)
    {
        InitializeComponent();

        this.mainWindow = mainWindow;

        // 親ウィンドウが閉じられたら設定ウィンドウも閉じる
        if (this.mainWindow != null)
        {
            this.mainWindow.Closed += MainWindow_Closed;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // ウィンドウサイズを設定（DPI スケーリング対応）
        nint hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        // 現在のスケールファクターを取得
        double scale = GetDpiScale();
        int width = (int)(920 * scale);
        int height = (int)(920 * scale);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        InitializeSettingsWebView();
    }

    void MainWindow_Closed(object? sender, Microsoft.UI.Xaml.WindowEventArgs e)
    {
        // 親ウィンドウが閉じられたら設定ウィンドウも閉じる
        if (mainWindow != null)
        {
            mainWindow.Closed -= MainWindow_Closed;
        }
        Close();
    }

    double GetDpiScale()
    {
        // XamlRoot から現在のスケールファクターを取得
        if (Content is FrameworkElement fe && fe.XamlRoot != null)
        {
            return fe.XamlRoot.RasterizationScale;
        }
        // デフォルトは 1.0（96 DPI）
        return 1.0;
    }

    async void InitializeSettingsWebView()
    {
        WebView2 settingsWebView = SettingsWebView;
        if (settingsWebView.CoreWebView2 == null)
            await settingsWebView.EnsureCoreWebView2Async();

        InitializeWindowPresenter();

        if (settingsWebView.CoreWebView2 != null)
        {
            settingsWebView.CoreWebView2.WebMessageReceived += SettingsWebView_WebMessageReceived;
            settingsWebView.CoreWebView2.NavigationCompleted += SettingsWebView_NavigationCompleted;

            if (SettingsUri != "")
                settingsWebView.Source = new Uri(SettingsUri);
        }
    }

    /// <summary>
    /// WebView2 のナビゲーション完了時に Ollama 情報を送信
    /// </summary>
    void SettingsWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            SendOllamaInfo();
        }
    }

    void SettingsWebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json))
            return;

        _ = ProcessWebMessageAsync(json);
    }

    async Task ProcessWebMessageAsync(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("method", out JsonElement methodKey))
            {
                if (methodKey.GetString() is string methodName && methodName == "tools/call")
                {
                    if (doc.RootElement.TryGetProperty("params", out JsonElement paramsKey))
                    {
                        ToolParams tp = ToolParams.Parse(paramsKey);
                        if (tp.Name == "closeSettings")
                        {
                            Close();
                        }
                        else if (tp.Name == "settingsUpdated")
                        {
                            // メインウィンドウに設定更新を通知
                            NotifyMainwindowSettingsUpdated();
                            // MCP クライアントの再初期化は SaveMcpSettingsToJsonAsync で行う
                        }
                        else if (tp.Name == "getOllamaInfo")
                        {
                            // Ollama 情報を再送信
                            SendOllamaInfo();
                        }
                        else if (tp.Name == "saveMcpSettings")
                        {
                            // MCP 設定を保存
                            string? mcpJson = tp.GetArgumentValue("mcpJson");
                            if (!string.IsNullOrEmpty(mcpJson))
                            {
                                await SaveMcpSettingsToJsonAsync(mcpJson);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogInfo(ex.Message);
        }
    }

    async Task SaveMcpSettingsToJsonAsync(string mcpJson)
    {
        try
        {
            LogInfo($"SaveMcpSettingsToJsonAsync called with: {mcpJson}");
            
            // MCP 設定をパース
            var mcpSettings = JsonSerializer.Deserialize<McpSettings>(mcpJson);
            LogInfo($"Parsed MCP settings: enabled={mcpSettings?.Enabled}, servers={mcpSettings?.McpServers?.Count ?? 0}");
            
            // 既存の設定をロードして MCP 設定のみ更新
            var settings = await ApiSettingsManager.LoadAsync();
            LogInfo($"Loaded existing settings: MCP enabled={settings.Mcp?.Enabled}");
            
            settings.Mcp = mcpSettings ?? new McpSettings();
            
            // ファイルに保存
            await ApiSettingsManager.SaveAsync(settings);
            LogInfo($"MCP settings saved to file");
            
            // 保存直後にファイルから再読み込みして MCP クライアントを初期化
            var verifySettings = await ApiSettingsManager.LoadAsync();
            LogInfo($"Verified settings: enabled={verifySettings.Mcp?.Enabled}, servers={verifySettings.Mcp?.McpServers?.Count ?? 0}");
            
            // MCP クライアントを再初期化
            var app = Application.Current as App;
            if (app != null && verifySettings.Mcp != null && verifySettings.Mcp.Enabled)
            {
                LogInfo("Re-initializing MCP client...");
                await app.InitializeMcpClientAsync(verifySettings.Mcp);
                LogInfo("MCP client re-initialized");
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Failed to save MCP settings: {ex.Message}");
            LogInfo($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Ollama 情報を JavaScript に送信
    /// </summary>
    public void SendOllamaInfo()
    {
        if (mainWindow != null)
        {
            LogInfo($"SendOllamaInfo: IsOllamaAvailable={mainWindow.IsOllamaAvailable}, Models={mainWindow.OllamaModels.Count}");

            var ollamaInfo = new
            {
                method = "ollamaInfo",
                isAvailable = mainWindow.IsOllamaAvailable,
                models = mainWindow.OllamaModels
            };
            var json = JsonSerializer.Serialize(ollamaInfo);
            SettingsWebView.CoreWebView2?.PostWebMessageAsString(json);
        }
    }

    void NotifyMainwindowSettingsUpdated()
    {
        if (mainWindow != null)
        {
            mainWindow.NotifySettingsUpdated();
        }
    }

    async Task<McpSettings> LoadMcpSettingsAsync()
    {
        // ファイルから設定をロード
        try
        {
            var apiSettings = await ApiSettingsManager.LoadAsync();
            return apiSettings.Mcp;
        }
        catch (Exception ex)
        {
            LogInfo($"Failed to load MCP settings: {ex.Message}");
            return new McpSettings();
        }
    }

    void InitializeWindowPresenter()
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            this.presenter = presenter;
        }
    }

    void LogInfo(string message, string memberName = "", string sourceFilePath = "", int sourceLineNumber = 0)
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
        string fileName = string.IsNullOrEmpty(sourceFilePath) ? "" : Path.GetFileName(sourceFilePath);
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"{time} [INFO] {fileName}:{sourceLineNumber} {memberName} - {message}{Environment.NewLine}";
        File.AppendAllText(logPath, line, Encoding.UTF8);
    }
}
