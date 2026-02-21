using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    OllamaClient? _ollamaClient;

    public string ServerUri { get; set; } = "";

    /// <summary>
    /// Ollama が利用可能か
    /// </summary>
    public bool IsOllamaAvailable { get; private set; } = false;

    /// <summary>
    /// Ollama のモデル一覧
    /// </summary>
    public List<string> OllamaModels { get; private set; } = new();

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        InitializePreview();
        InitializeOllamaAsync();
    }

    /// <summary>
    /// バックグラウンドで Ollama の状態を確認
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

            // 設定画面が開いている場合は Ollama 情報を通知
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
    /// デバッグ用：Ollama 利用可能フラグを手動設定
    /// </summary>
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
        // メッセージを文字列として受け取る
        string json = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json))
        {
            LogInfo("メッセージがありません");
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
                            // デバッグ用：Ollama 利用可能フラグを手動設定
                            string? available = tp.GetArgumentValue("available");
                            SetOllamaAvailable(available == "true");
                        }
                    }
                }
            }

            // UI スレッドで実行
            // if (DispatcherQueue.HasThreadAccess)
            //     HandleWindowCommand(cmd);
            // else
            //     DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => HandleWindowCommand(cmd));
        }
        catch(Exception ex)
        {
            LogInfo(ex.Message);
            // 不正なメッセージは無視またはログ
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

    public void NotifySettingsUpdated()
    {
        // メインウィンドウの WebView2 に設定更新を通知
        Preview.CoreWebView2?.PostWebMessageAsString("settingsUpdated");
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
