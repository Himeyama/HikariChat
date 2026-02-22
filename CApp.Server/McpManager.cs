using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CApp;

public class McpManager : IDisposable
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private ApiSettings _settings = new();

    public bool IsEnabled => _settings.McpEnabled;

    public async Task UpdateSettingsAsync(ApiSettings settings)
    {
        DebugLogger.Mcp($"UpdateSettingsAsync called. McpEnabled={settings.McpEnabled}, ServerCount={settings.McpServers.Count}");
        _settings = settings;

        // 削除されたサーバ�Eを停止
        var removed = _clients.Keys.Except(settings.McpServers.Keys).ToList();
        foreach (var name in removed)
        {
            DebugLogger.Mcp($"Stopping removed MCP server: {name}");
            _clients[name].Dispose();
            _clients.Remove(name);
        }

        // 有効なサーバ�Eのみ起勁E
        if (settings.McpEnabled)
        {
            DebugLogger.Mcp($"MCP is enabled. Starting {settings.McpServers.Count} server(s)...");
            foreach (var kv in settings.McpServers)
            {
                if (!_clients.ContainsKey(kv.Key))
                {
                    try
                    {
                        DebugLogger.Mcp($"Starting MCP server: {kv.Key} (Command: {kv.Value.Command}, Args: {string.Join(" ", kv.Value.Args)})");
                        var client = new McpClient(kv.Key, kv.Value);
                        await client.StartAsync();
                        _clients[kv.Key] = client;
                        DebugLogger.Mcp($"MCP server started: {kv.Key}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Error($"Failed to start MCP server {kv.Key}: {ex.Message}\nStackTrace: {ex.StackTrace}");
                        Console.WriteLine($"[McpManager] Failed to start server {kv.Key}: {ex.Message}");
                    }
                }
            }
        }
        else
        {
            DebugLogger.Mcp("MCP is disabled. Stopping all servers.");
            // MCP 無効時�E全サーバ�E停止
            StopAll();
        }
    }

    public void StopAll()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    public async Task<List<object>> GetOpenAiToolsAsync()
    {
        var allTools = new List<object>();
        if (!_settings.McpEnabled) return allTools;

        foreach (var client in _clients.Values)
        {
            try
            {
                var result = await client.ListToolsAsync();
                if (result.TryGetProperty("tools", out var toolsArray))
                {
                    foreach (var tool in toolsArray.EnumerateArray())
                    {
                        string name = tool.GetProperty("name").GetString() ?? "";
                        // 名前衝突を避けるためサーバ�E名を接頭辞にする (侁E filesystem_read_file)
                        string namespacedName = $"{client.Name}_{name}";
                        
                        var inputSchema = tool.GetProperty("inputSchema").Clone();
                        
                        allTools.Add(new
                        {
                            type = "function",
                            function = new
                            {
                                name = namespacedName,
                                description = tool.GetProperty("description").GetString(),
                                parameters = inputSchema
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[McpManager] Failed to list tools for {client.Name}: {ex.Message}");
            }
        }
        return allTools;
    }

    public async Task<JsonElement> CallToolAsync(string namespacedName, JsonElement arguments)
    {
        if (!_settings.McpEnabled) throw new Exception("MCP is not enabled");

        int separatorIndex = namespacedName.IndexOf('_');
        if (separatorIndex == -1) throw new Exception("Invalid tool name format");

        string serverName = namespacedName.Substring(0, separatorIndex);
        string originalName = namespacedName.Substring(separatorIndex + 1);

        if (_clients.TryGetValue(serverName, out var client))
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
