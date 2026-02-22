using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

namespace CApp.Server;

/// <summary>
/// MCP ツールの定義
/// </summary>
public class McpToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public JsonElement InputSchema { get; set; }
}

/// <summary>
/// MCP ツール呼び出しの結果
/// </summary>
public class McpCallToolResult
{
    public List<ContentBlock> Content { get; set; } = new();
    public bool IsError { get; set; }
}

/// <summary>
/// コンテンツブロック
/// </summary>
public class ContentBlock
{
    public string Type { get; set; } = "";
    public string? Text { get; set; }
    public string? Title { get; set; }
    public string? ResourceUri { get; set; }
    public JsonElement? Data { get; set; }
}

/// <summary>
/// MCP クライアントのラッパー
/// </summary>
public class McpClientWrapper : IDisposable
{
    private object? _client;
    private readonly string _name;
    private readonly McpServerConfig _options;
    private readonly CancellationTokenSource _cts = new();

    public string Name => _name;
    public bool IsConnected => _client != null;

    public McpClientWrapper(string name, McpServerConfig options)
    {
        _name = name;
        _options = options;
    }

    public async Task ConnectAsync()
    {
        Console.WriteLine($"[{_name}] Starting process: {_options.Command} {string.Join(" ", _options.Args)}");
        
        var transportOptions = new StdioClientTransportOptions
        {
            Name = _name,
            Command = _options.Command,
            Arguments = [.. _options.Args],
        };

        if (_options.Env != null)
        {
            foreach (var kv in _options.Env)
            {
                transportOptions.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        var transport = new StdioClientTransport(transportOptions);
        Console.WriteLine($"[{_name}] Creating MCP client transport...");
        _client = await McpClient.CreateAsync(transport, cancellationToken: _cts.Token);
        Console.WriteLine($"[{_name}] MCP client created successfully");
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Not connected");
        
        var tools = new List<McpToolDefinition>();
        
        // リフレクションを使って ListToolsAsync を呼び出し
        var listToolsMethod = _client.GetType().GetMethod("ListToolsAsync");
        if (listToolsMethod != null)
        {
            var resultTask = listToolsMethod.Invoke(_client, new object[] { _cts.Token }) as Task;
            if (resultTask != null)
            {
                await resultTask.ConfigureAwait(false);
                var result = resultTask.GetType().GetProperty("Result")?.GetValue(resultTask);
                if (result is System.Collections.IEnumerable toolList)
                {
                    foreach (var tool in toolList)
                    {
                        var nameProp = tool.GetType().GetProperty("Name");
                        var descProp = tool.GetType().GetProperty("Description");
                        var schemaProp = tool.GetType().GetProperty("Parameters");
                        
                        tools.Add(new McpToolDefinition
                        {
                            Name = nameProp?.GetValue(tool)?.ToString() ?? "",
                            Description = descProp?.GetValue(tool)?.ToString() ?? "",
                            InputSchema = schemaProp != null ? 
                                JsonSerializer.Deserialize<JsonElement>(
                                    JsonSerializer.Serialize(schemaProp.GetValue(tool))) 
                                : default
                        });
                    }
                }
            }
        }
        return tools;
    }

    public async Task<McpCallToolResult> CallToolAsync(string name, JsonElement arguments)
    {
        if (_client == null) throw new InvalidOperationException("Not connected");

        var argsDict = new Dictionary<string, object?>();
        
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
            {
                argsDict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
            }
        }

        // リフレクションを使って CallToolAsync を呼び出し
        var callToolMethod = _client.GetType().GetMethod("CallToolAsync", 
            new[] { typeof(string), typeof(Dictionary<string, object?>), typeof(CancellationToken) });
        
        if (callToolMethod != null)
        {
            var result = await (dynamic?)callToolMethod.Invoke(_client, new object[] { name, argsDict, _cts.Token })!;
            if (result != null)
            {
                var contentProp = result.GetType().GetProperty("Content");
                var isErrorProp = result.GetType().GetProperty("IsError");
                
                var toolResult = new McpCallToolResult
                {
                    IsError = isErrorProp?.GetValue(result) as bool? ?? false
                };
                
                if (contentProp != null)
                {
                    var content = contentProp.GetValue(result);
                    if (content is System.Collections.IEnumerable contentList)
                    {
                        foreach (var item in contentList)
                        {
                            var typeProp = item.GetType().GetProperty("Type");
                            var textProp = item.GetType().GetProperty("Text");
                            toolResult.Content.Add(new ContentBlock
                            {
                                Type = typeProp?.GetValue(item)?.ToString() ?? "",
                                Text = textProp?.GetValue(item)?.ToString()
                            });
                        }
                    }
                }
                return toolResult;
            }
        }
        
        return new McpCallToolResult();
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_client != null)
        {
            var disposeAsyncMethod = _client.GetType().GetMethod("DisposeAsync");
            if (disposeAsyncMethod != null)
            {
                var result = disposeAsyncMethod.Invoke(_client, null);
                if (result is ValueTask valueTask)
                {
                    valueTask.AsTask().Wait();
                }
            }
        }
        _cts.Dispose();
    }
}
