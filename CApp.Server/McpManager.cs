using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

namespace CApp.Server;

/// <summary>
/// MCP クライアントの管理
/// </summary>
public class McpManager : IDisposable
{
    readonly Dictionary<string, McpClient> _clients = [];
    ApiSettings _settings = new();
    static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_manager.log");

    static void Log(string message)
    {
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"{time} [INFO] {message}{Environment.NewLine}";
        File.AppendAllText(LogPath, line, Encoding.UTF8);
    }

    public Dictionary<string, McpClient> GetClients()
    {
        return _clients;
    }

    public async Task UpdateSettingsAsync(ApiSettings settings)
    {
        Log($"UpdateSettingsAsync called. McpEnabled={settings.McpEnabled}, ServerCount={settings.McpServers.Count}");
        _settings = settings;

        // 削除されたサーバーを停止
        foreach (string name in _clients.Keys.Except(settings.McpServers.Keys).ToList())
        {
            Log($"Stopping removed MCP server: {name}");
            await _clients[name].DisposeAsync();
            _clients.Remove(name);
        }

        if (settings.McpEnabled)
        {
            Log($"MCP is enabled. Starting {settings.McpServers.Count} server(s)...");
            foreach ((string? name, McpServerConfig? config) in settings.McpServers)
            {
                if (_clients.ContainsKey(name)) continue;

                try
                {
                    Log($"Starting MCP server: {name} (Command: {config.Command}, Args: {string.Join(" ", config.Args)})");

                    StdioClientTransport transport = new(new StdioClientTransportOptions
                    {
                        Name = name,
                        Command = config.Command,
                        Arguments = [.. config.Args],
                        EnvironmentVariables = config.Env?.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (string?)kvp.Value
                        )
                    });

                    McpClient client = await McpClient.CreateAsync(transport);
                    _clients[name] = client;

                    Log($"MCP server started: {name}");
                }
                catch (Exception ex)
                {
                    Log($"Failed to start MCP server {name}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        else
        {
            Log("MCP is disabled. Stopping all servers.");
            await StopAllAsync();
        }
    }

    public async Task StopAllAsync()
    {
        foreach (McpClient client in _clients.Values)
            await client.DisposeAsync();
        _clients.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
    }

    public void StopAll()
    {
        foreach (McpClient client in _clients.Values)
        {
            _ = client.DisposeAsync();
        }
        _clients.Clear();
    }

    public void Dispose()
    {
        StopAll();
    }

    public (bool enabled, int activeCount, int totalCount) GetStatus()
    {
        return (_settings.McpEnabled, _clients.Count, _settings.McpServers.Count);
    }
}
