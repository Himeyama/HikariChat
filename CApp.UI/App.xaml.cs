using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace CApp;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private McpManager? _mcpManager;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        _mcpManager = new McpManager();
    }

    public MainWindow? MainWindow => _mainWindow;

    public (bool enabled, int activeCount, int totalCount) GetMcpStatus()
    {
        return _mcpManager?.GetStatus() ?? (false, 0, 0);
    }
}
