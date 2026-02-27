using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HikariChat.Server;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WinRT.Interop;

namespace HikariChat;

public sealed partial class MainWindow : Window
{
    OverlappedPresenter? presenter;
    OllamaClient? _ollamaClient;

    public string ServerUri { get; set; } = "";

    public ApiSettings CurrentApiSettings { get; private set; }

    /// <summary>
    /// Ollama が利用可能ぁE
    /// </summary>
    public bool IsOllamaAvailable { get; private set; } = false;

    /// <summary>
    /// Ollama のモデル一覧
    /// </summary>
    public List<string> OllamaModels { get; private set; } = new();

    public MainWindow()
    {
        InitializeComponent();

        CurrentApiSettings = new ApiSettings(); // Initialize to prevent CS8618 warning

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        InitializePreview();
        InitializeOllamaAsync();
        InitializeCurrentApiSettings();
    }

    async void InitializeCurrentApiSettings()
    {
        ApiSettings loadedSettings = await HikariChat.Server.ApiSettingsManager.LoadAsync();
        if (loadedSettings != null)
        {
            CurrentApiSettings = loadedSettings; // Update with loaded settings
        }
        LogInfo($"Initialized CurrentApiSettings. Model: {CurrentApiSettings.Model}");
        // Initial send of settings to WebView2
        SendCurrentSettingsToWebView();
    }

    /// <summary>
    /// バックグラウンドで Ollama の状態を確誁E
    /// </summary>
    async void InitializeOllamaAsync()

    {
        try
        {
            _ollamaClient = new OllamaClient();
            IsOllamaAvailable = await _ollamaClient.IsAvailableAsync();
            if (IsOllamaAvailable)
            {
                OllamaModels = await _ollamaClient.GetModelsAsync();
            }
            LogInfo($"Ollama available: {IsOllamaAvailable}, Models={OllamaModels.Count}");

            // 設定画面が開ぁE��ぁE��場合�E Ollama 惁E��を通知
            if (settingsWindow != null)
            {
                settingsWindow.SendOllamaInfo();
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Ollama initialization failed: {ex.Message}");
        }
    }

    public void SetOllamaAvailable(bool available)
    {
        IsOllamaAvailable = available;
        LogInfo($"Ollama available set manually: {available}");
        
        // 設定画面に通知
        if (settingsWindow != null)
        {
            settingsWindow.SendOllamaInfo();
        }
    }

    async void InitializePreview()
    {
        WebView2 preview = Preview;

        // フロントエンドのパスを取得
        string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI");
        string indexPath = Path.Combine(assetsPath, "index.html");

        // CoreWebView2 を初期化
        if (preview.CoreWebView2 == null)
        {
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync();
            await preview.EnsureCoreWebView2Async(env);
        }

        InitializeWindowPresenter();
#pragma warning disable CS8602
        preview.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
#pragma warning restore CS8602
        
        // フロントエンドを読み込む
        if (File.Exists(indexPath))
        {
            // HTTP サーバーを使用して提供（CORS 回避のため）
            preview.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.assets", assetsPath, CoreWebView2HostResourceAccessKind.Allow);
            preview.Source = new Uri("https://app.assets/index.html");
            LogInfo($"Loaded frontend: https://app.assets/index.html");
        }
        else if (ServerUri != "")
        {
            preview.Source = new Uri(ServerUri);
        }
        else
        {
            LogInfo("Frontend not found and ServerUri is empty");
        }
    }

    void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json))
        {
            LogInfo("メチE��ージがありません");
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if(doc.RootElement.TryGetProperty("method", out JsonElement methodKey))
            {
                if (methodKey.GetString() is string methodName && methodName == "tools/call")
                {
                    if(doc.RootElement.TryGetProperty("params", out JsonElement paramsKey))
                    {
                        ToolParams tp = ToolParams.Parse(paramsKey);
                        if(tp.Name == "control"){
                            string? argValue = tp.GetArgumentValue("command");
                            if(argValue != null)
                            {
                                if (DispatcherQueue.HasThreadAccess)
                                    HandleWindowCommand(argValue);
                                else
                                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => HandleWindowCommand(argValue));
                            }
                        }
                        else if (tp.Name == "openSettings")
                        {
                            if (DispatcherQueue.HasThreadAccess)
                                OpenSettingsWindow();
                            else
                                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => OpenSettingsWindow());
                        }
                        else if (tp.Name == "setOllamaAvailable")
                        {
                            // チE��チE��用�E�Ollama 利用可能フラグを手動設宁E
                            string? available = tp.GetArgumentValue("available");
                            SetOllamaAvailable(available == "true");
                        }
                        else if (tp.Name == "getMcpInfo")
                        {
                            SendMcpStatus();
                        }
                        else
                        {
                            // MCP ツール呼び出しとして処理
                            LogInfo($"MCP Tool call received: {tp.Name}, Args: {tp.Arguments}");
                            _ = ExecuteMcpToolAsync(tp.Name, tp.Arguments);
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

    public void SendMcpStatus()
    {
        if (Application.Current is App app)
        {
            (bool enabled, int activeCount, int totalCount) = app.GetMcpStatus();
            var status = new
            {
                method = "mcpStatus",
                enabled = enabled,
                activeCount = activeCount,
                totalCount = totalCount
            };
            string json = JsonSerializer.Serialize(status);
            Preview.CoreWebView2?.PostWebMessageAsString(json);
        }
    }

    /// <summary>
    /// MCP ツールを実行し、結果を WebView2 に通知する
    /// </summary>
    private async Task ExecuteMcpToolAsync(string toolName, JsonElement arguments)
    {
        // toolCallId を生成（フロントエンドと同じ形式）
        string toolCallId = $"tool-{DateTimeOffset.Now.ToUnixTimeMilliseconds()}-{new Random().Next(100000000, 999999999)}";
        
        try
        {
            LogInfo($"Executing MCP tool: {toolName}, toolCallId: {toolCallId}");

            // API サーバーにツール実行を依頼
            using HttpClient httpClient = new();
            string payload = JsonSerializer.Serialize(new { name = toolName, arguments });
            StringContent content = new(payload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync("http://localhost:29000/api/mcp/execute", content);

            if (response.IsSuccessStatusCode)
            {
                string responseJson = await response.Content.ReadAsStringAsync();
                using JsonDocument resultDoc = JsonDocument.Parse(responseJson);
                JsonElement result = resultDoc.RootElement;
                LogInfo($"MCP tool execution completed: {toolName}, Success: {result.GetProperty("success").GetBoolean()}");

                // ツール実行結果を WebView に返す
                var toolResult = new
                {
                    toolCallId = toolCallId,
                    method = "toolResult",
                    name = toolName,
                    result = result
                };
                string resultJson = JsonSerializer.Serialize(toolResult);
                Preview.CoreWebView2?.PostWebMessageAsString(resultJson);
            }
            else
            {
                string error = await response.Content.ReadAsStringAsync();
                LogInfo($"MCP tool execution failed: {error}");
                
                // エラー結果を WebView に返す
                var errorResult = new
                {
                    toolCallId = toolCallId,
                    method = "toolResult",
                    name = toolName,
                    result = new { error = error }
                };
                string errorJson = JsonSerializer.Serialize(errorResult);
                Preview.CoreWebView2?.PostWebMessageAsString(errorJson);
            }
        }
        catch (Exception ex)
        {
            LogInfo($"MCP tool execution error: {ex.Message}");
            
            // 例外結果を WebView に返す
            var exceptionResult = new
            {
                toolCallId = toolCallId,
                method = "toolResult",
                name = toolName,
                result = new { error = ex.Message }
            };
            string exceptionJson = JsonSerializer.Serialize(exceptionResult);
            Preview.CoreWebView2?.PostWebMessageAsString(exceptionJson);
        }
    }

    /// <summary>
    /// WebView2 で JavaScript を実行する
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        try
        {
            if (Preview.CoreWebView2 == null)
            {
                LogInfo("ExecuteScriptAsync: CoreWebView2 is null");
                return string.Empty;
            }
            
            var result = await Preview.CoreWebView2.ExecuteScriptAsync(script);
            LogInfo($"ExecuteScriptAsync result: {result ?? "(null)"}");
            return result ?? string.Empty;
        }
        catch (Exception ex)
        {
            LogInfo($"ExecuteScriptAsync error: {ex.Message}");
            return string.Empty;
        }
    }

    public async Task<string?> GetChatHistoryAsync()
    {
        string result = await ExecuteScriptAsync("JSON.stringify(window.chrome.webview.targetEnvironment?.tabs || {})");
        return string.IsNullOrEmpty(result) ? null : result;
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

    SettingsWindow? settingsWindow;

    void HandleWindowCommand(string cmd)
    {
        switch (cmd)
        {
            case "minimize":
                presenter?.Minimize();
                break;
            case "toggleMaximize":
                if (presenter?.State == OverlappedPresenterState.Maximized)
                {
                    presenter?.Restore();
                }
                else if (presenter?.State == OverlappedPresenterState.Restored)
                {
                    presenter?.Maximize();
                }
                break;
            case "restore":
                presenter?.Restore();
                break;
            case "maximize":
                presenter?.Maximize();
                break;
            case "close":
                Close();
                break;
            case "openSettings":
                OpenSettingsWindow();
                break;
        }
    }

    void OpenSettingsWindow()
    {
        LogInfo($"OpenSettingsWindow called. ServerUri={ServerUri}");

        if (settingsWindow == null)
        {
            // 仮想ホスト名を使用
            string settingsUri = "https://app.assets/settings.html";

            LogInfo($"Creating SettingsWindow with Uri={settingsUri}");

            settingsWindow = new SettingsWindow(this)
            {
                SettingsUri = settingsUri
            };
            settingsWindow.Closed += (s, e) => settingsWindow = null;
            settingsWindow.Activate();
        }
        else
        {
            settingsWindow.Activate();
        }
    }

    public async void NotifySettingsUpdated()
    {
        LogInfo("NotifySettingsUpdated called. Reloading settings and updating WebView2.");
        CurrentApiSettings = await HikariChat.Server.ApiSettingsManager.LoadAsync();
        SendCurrentSettingsToWebView();
    }

    private void SendCurrentSettingsToWebView()
    {
        var settingsMessage = new
        {
            method = "settingsUpdated",
            settings = CurrentApiSettings
        };
        string json = JsonSerializer.Serialize(settingsMessage);
        Preview.CoreWebView2?.PostWebMessageAsString(json);
        LogInfo($"Sent updated settings to WebView2: {json}");
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

class ToolParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }

    public static ToolParams Parse(JsonElement json)
    {
        ToolParams tp = new();
        if(json.TryGetProperty("name", out JsonElement name))
        {
            if (name.GetString() is string nameValue)
                tp.Name = nameValue;
        }

        if(json.TryGetProperty("arguments", out JsonElement args))
        {
            tp.Arguments = args;
        }
        return tp;
    }

    public string? GetArgumentValue(string argumentKey)
    {
        if(Arguments.TryGetProperty(argumentKey, out JsonElement jsonElement))
        {
            if (jsonElement.GetString() is string argValue)
                return argValue;
        }
        return null;
    }
}
