using System;
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

    public string ServerUri { get; set; } = "";

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        InitializePreview();
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
