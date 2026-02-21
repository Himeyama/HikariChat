using System;
using System.IO;
using System.Text;
using System.Text.Json;
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
                        }
                        else if (tp.Name == "getOllamaInfo")
                        {
                            // Ollama 情報を再送信
                            SendOllamaInfo();
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
