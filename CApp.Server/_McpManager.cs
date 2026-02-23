using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CApp.Server;

/// <summary>
/// MCP クライアントの管理
/// </summary>
public class McpManager : IDisposable
{
    private readonly Dictionary<string, McpClientWrapper> _clients = new();
    private ApiSettings _settings = new();
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_manager.log");

    private static void Log(string message)
    {
        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"{time} [INFO] {message}{Environment.NewLine}";
        File.AppendAllText(LogPath, line, Encoding.UTF8);
    }

    public bool IsEnabled => _settings.McpEnabled;

    public async Task UpdateSettingsAsync(ApiSettings settings)
    {
        Log($"UpdateSettingsAsync called. McpEnabled={settings.McpEnabled}, ServerCount={settings.McpServers.Count}");
        Console.WriteLine($"[McpManager] UpdateSettingsAsync called. McpEnabled={settings.McpEnabled}, ServerCount={settings.McpServers.Count}");
        _settings = settings;

        // 削除されたサーバーを停止
        List<string> removed = _clients.Keys.Except(settings.McpServers.Keys).ToList();
        foreach (string name in removed)
        {
            Log($"Stopping removed MCP server: {name}");
            Console.WriteLine($"[McpManager] Stopping removed MCP server: {name}");
            _clients[name].Dispose();
            _clients.Remove(name);
        }

        // 有効なサーバーのみ起動
        if (settings.McpEnabled)
        {
            Log($"MCP is enabled. Starting {settings.McpServers.Count} server(s)...");
            Console.WriteLine($"[McpManager] MCP is enabled. Starting {settings.McpServers.Count} server(s)...");
            foreach (KeyValuePair<string, McpServerConfig> kv in settings.McpServers)
            {
                if (!_clients.ContainsKey(kv.Key))
                {
                    try
                    {
                        Log($"Starting MCP server: {kv.Key} (Command: {kv.Value.Command}, Args: {string.Join(" ", kv.Value.Args)})");
                        Console.WriteLine($"[McpManager] Starting MCP server: {kv.Key} (Command: {kv.Value.Command}, Args: {string.Join(" ", kv.Value.Args)})");
                        McpClientWrapper client = new McpClientWrapper(kv.Key, kv.Value);
                        await client.ConnectAsync();
                        _clients[kv.Key] = client;
                        Log($"MCP server started: {kv.Key}");
                        Console.WriteLine($"[McpManager] MCP server started: {kv.Key}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to start MCP server {kv.Key}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                        Console.WriteLine($"[McpManager] Failed to start MCP server {kv.Key}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    }
                }
            }
        }
        else
        {
            Log("MCP is disabled. Stopping all servers.");
            Console.WriteLine("[McpManager] MCP is disabled. Stopping all servers.");
            StopAll();
        }
    }

    public void StopAll()
    {
        foreach (McpClientWrapper client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    public async Task<List<object>> GetOpenAiToolsAsync()
    {
        List<object> allTools = new List<object>();
        if (!_settings.McpEnabled) return allTools;

        foreach (McpClientWrapper client in _clients.Values)
        {
            try
            {
                List<McpToolDefinition> tools = await client.ListToolsAsync();
                foreach (McpToolDefinition tool in tools)
                {
                    string namespacedName = $"{client.Name}_{tool.Name}";

                    allTools.Add(new
                    {
                        type = "function",
                        function = new
                        {
                            name = namespacedName,
                            description = tool.Description,
                            parameters = tool.InputSchema
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McpManager] Failed to list tools for {client.Name}: {ex.Message}");
            }
        }
        return allTools;
    }

    public async Task<McpCallToolResult> CallToolAsync(string namespacedName, JsonElement arguments)
    {
        if (!_settings.McpEnabled) throw new Exception("MCP is not enabled");

        int separatorIndex = namespacedName.IndexOf('_');
        if (separatorIndex == -1) throw new Exception("Invalid tool name format");

        string serverName = namespacedName.Substring(0, separatorIndex);
        string originalName = namespacedName.Substring(separatorIndex + 1);

        if (_clients.TryGetValue(serverName, out McpClientWrapper? client))
        {
            return await client.CallToolAsync(originalName, arguments);
        }
        throw new Exception($"MCP server not found: {serverName}");
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
