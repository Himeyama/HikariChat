using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WinRT.Interop;

namespace CApp;

public sealed partial class MainWindow : Window
{
    OverlappedPresenter? presenter;
    CApp.OllamaClient? _ollamaClient;

    public string ServerUri { get; set; } = "";

    public CApp.ApiSettings CurrentApiSettings { get; private set; }

    /// <summary>
    /// Ollama 縺悟茜逕ｨ蜿ｯ閭ｽ縺・
    /// </summary>
    public bool IsOllamaAvailable { get; private set; } = false;

    /// <summary>
    /// Ollama 縺ｮ繝｢繝・Ν荳隕ｧ
    /// </summary>
    public List<string> OllamaModels { get; private set; } = new();

    public MainWindow()
    {
        InitializeComponent();

        CurrentApiSettings = new CApp.ApiSettings(); // Initialize to prevent CS8618 warning

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        InitializePreview();
        InitializeOllamaAsync();
        InitializeCurrentApiSettings();
    }

    async void InitializeCurrentApiSettings()
    {
        var loadedSettings = await CApp.ApiSettingsManager.LoadAsync();
        if (loadedSettings != null)
        {
            CurrentApiSettings = loadedSettings; // Update with loaded settings
        }
        LogInfo($"Initialized CurrentApiSettings. Model: {CurrentApiSettings.Model}");
        // Initial send of settings to WebView2
        SendCurrentSettingsToWebView();
    }

    /// <summary>
    /// 繝舌ャ繧ｯ繧ｰ繝ｩ繧ｦ繝ｳ繝峨〒 Ollama 縺ｮ迥ｶ諷九ｒ遒ｺ隱・
    /// </summary>
    async void InitializeOllamaAsync()

    {
        try
        {
            _ollamaClient = new CApp.OllamaClient();
            IsOllamaAvailable = await _ollamaClient.IsAvailableAsync();
            if (IsOllamaAvailable)
            {
                OllamaModels = await _ollamaClient.GetModelsAsync();
            }
            LogInfo($"Ollama available: {IsOllamaAvailable}, Models={OllamaModels.Count}");

            // 險ｭ螳夂判髱｢縺碁幕縺・※縺・ｋ蝣ｴ蜷医・ Ollama 諠・ｱ繧帝夂衍
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

    /// <summary>
    /// 繝・ヰ繝・げ逕ｨ・唹llama 蛻ｩ逕ｨ蜿ｯ閭ｽ繝輔Λ繧ｰ繧呈焔蜍戊ｨｭ螳・
    /// </summary>
    public void SetOllamaAvailable(bool available)
    {
        IsOllamaAvailable = available;
        LogInfo($"Ollama available set manually: {available}");
        
        // 險ｭ螳夂判髱｢縺ｫ騾夂衍
        if (settingsWindow != null)
        {
            settingsWindow.SendOllamaInfo();
        }
    }

    async void InitializePreview()
    {
        WebView2 preview = Preview;
        if (preview.CoreWebView2 == null)
            await preview.EnsureCoreWebView2Async();
        InitializeWindowPresenter();
        if (preview.CoreWebView2 != null)
            preview.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        if (ServerUri != "")
            preview.Source = new Uri(ServerUri);
    }

    void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 繝｡繝・そ繝ｼ繧ｸ繧呈枚蟄怜・縺ｨ縺励※蜿励￠蜿悶ｋ
        string json = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json))
        {
            LogInfo("繝｡繝・そ繝ｼ繧ｸ縺後≠繧翫∪縺帙ｓ");
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
                            // 繝・ヰ繝・げ逕ｨ・唹llama 蛻ｩ逕ｨ蜿ｯ閭ｽ繝輔Λ繧ｰ繧呈焔蜍戊ｨｭ螳・
                            string? available = tp.GetArgumentValue("available");
                            SetOllamaAvailable(available == "true");
                        }
                        else if (tp.Name == "getMcpInfo")
                        {
                            SendMcpStatus();
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
        if (Application.Current is App app && app.Server != null)
        {
            var (enabled, activeCount, totalCount) = app.Server.GetMcpStatus();
            var status = new
            {
                method = "mcpStatus",
                enabled = enabled,
                activeCount = activeCount,
                totalCount = totalCount
            };
            var json = JsonSerializer.Serialize(status);
            Preview.CoreWebView2?.PostWebMessageAsString(json);
        }
    }

    /// <summary>
    /// WebView2 縺ｧ JavaScript 繧貞ｮ溯｡後☆繧具ｼ医ユ繧ｹ繝郁・蜍募喧逕ｨ・・
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

    /// <summary>
    /// 繝・せ繝郁・蜍募喧・夂樟蝨ｨ縺ｮ繝√Ε繝・ヨ螻･豁ｴ繧貞叙蠕・
    /// </summary>
    public async Task<string?> GetChatHistoryAsync()
    {
        return await ExecuteScriptAsync("JSON.stringify(window.chrome.webview.targetEnvironment?.tabs || {})");
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
            string settingsUri = ServerUri;
            if (ServerUri.Contains("index.html"))
                settingsUri = ServerUri.Replace("index.html", "settings.html");
            else if (ServerUri.EndsWith("/"))
                settingsUri = ServerUri + "settings.html";
            else
                settingsUri = ServerUri + "/settings.html";

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
        CurrentApiSettings = await CApp.ApiSettingsManager.LoadAsync();
        SendCurrentSettingsToWebView();
    }

    private void SendCurrentSettingsToWebView()
    {
        var settingsMessage = new
        {
            method = "settingsUpdated",
            settings = CurrentApiSettings
        };
        var json = JsonSerializer.Serialize(settingsMessage);
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
