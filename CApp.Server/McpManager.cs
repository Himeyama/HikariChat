using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

namespace CApp.Server;

/// <summary>
/// MCP クライアントの管理
/// </summary>
public class McpManager : IDisposable
{
    private readonly Dictionary<string, McpClientWrapper> _clients = new();
    private ApiSettings _settings = new();

    public bool IsEnabled => _settings.McpEnabled;

    public async Task UpdateSettingsAsync(ApiSettings settings)
    {
        _settings = settings;

        // 削除されたサーバーを停止
        var removed = _clients.Keys.Except(settings.McpServers.Keys).ToList();
        foreach (var name in removed)
        {
            _clients[name].Dispose();
            _clients.Remove(name);
        }

        // 有効なサーバーのみ起動
        if (settings.McpEnabled)
        {
            foreach (var kv in settings.McpServers)
            {
                if (!_clients.ContainsKey(kv.Key))
                {
                    try
                    {
                        var client = new McpClientWrapper(kv.Key, kv.Value);
                        await client.ConnectAsync();
                        _clients[kv.Key] = client;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[McpManager] Failed to start server {kv.Key}: {ex.Message}");
                    }
                }
            }
        }
        else
        {
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
                var tools = await client.ListToolsAsync();
                foreach (var tool in tools)
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
