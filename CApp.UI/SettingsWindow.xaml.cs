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

        if (this.mainWindow != null)
        {
            this.mainWindow.Closed += MainWindow_Closed;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        nint hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        double scale = GetDpiScale();
        int width = (int)(920 * scale);
        int height = (int)(1080 * scale);
        appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

        InitializeSettingsWebView();
    }

    void MainWindow_Closed(object? sender, Microsoft.UI.Xaml.WindowEventArgs e)
    {
        if (mainWindow != null)
        {
            mainWindow.Closed -= MainWindow_Closed;
        }
        Close();
    }

    double GetDpiScale()
    {
        if (Content is FrameworkElement fe && fe.XamlRoot != null)
        {
            return fe.XamlRoot.RasterizationScale;
        }
        return 1.0;
    }

    async void InitializeSettingsWebView()
    {
        WebView2 settingsWebView = SettingsWebView;
        
        // フロントエンドのパスを取得
        string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI");
        
        // CoreWebView2 を初期化
        if (settingsWebView.CoreWebView2 == null)
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await settingsWebView.EnsureCoreWebView2Async(env);
        }

        InitializeWindowPresenter();

        if (settingsWebView.CoreWebView2 != null)
        {
            settingsWebView.CoreWebView2.WebMessageReceived += SettingsWebView_WebMessageReceived;
            settingsWebView.CoreWebView2.NavigationCompleted += SettingsWebView_NavigationCompleted;
            
            // 仮想ホスト名マッピングを設定
            settingsWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.assets", assetsPath, CoreWebView2HostResourceAccessKind.Allow);

            if (SettingsUri != "")
                settingsWebView.Source = new Uri(SettingsUri);
        }
    }

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
                            NotifyMainwindowSettingsUpdated();
                        }
                        else if (tp.Name == "getOllamaInfo")
                        {
                            SendOllamaInfo();
                        }
                        else if (tp.Name == "saveSettings")
                        {
                            string? settingsJson = tp.GetArgumentValue("settingsJson");
                            if (!string.IsNullOrEmpty(settingsJson))
                            {
                                await SaveSettingsToJsonAsync(settingsJson);
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

    async Task SaveSettingsToJsonAsync(string settingsJson)
    {
        try
        {
            LogInfo($"SaveSettingsToJsonAsync called");

            var settings = JsonSerializer.Deserialize<CApp.Server.ApiSettings>(settingsJson);

            if (settings != null)
            {
                await CApp.Server.ApiSettingsManager.SaveAsync(settings);
                LogInfo($"Settings saved to file");

                // アプリに MCP サーバーの更新を通知
                if (Application.Current is App app)
                {
                    await app.UpdateMcpSettingsAsync(settings);
                    LogInfo($"MCP settings updated in App");
                    
                    // MCP ステータスをフロントエンドに送信
                    app.MainWindow?.SendMcpStatus();
                    LogInfo($"MCP status sent to frontend");
                }
            }
        }
        catch (Exception ex)
        {
            LogInfo($"Failed to save settings: {ex.Message}");
        }
    }

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
