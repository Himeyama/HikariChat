using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

#pragma warning disable CS8602

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
    object? _client;
    readonly string _name;
    readonly McpServerConfig _options;
    readonly CancellationTokenSource _cts = new();

    public string Name => _name;

    public McpClientWrapper(string name, McpServerConfig options)
    {
        _name = name;
        _options = options;
    }

    public async Task ConnectAsync()
    {
        Console.WriteLine($"[{_name}] Starting process: {_options.Command} {string.Join(" ", _options.Args)}");

        StdioClientTransportOptions transportOptions = new StdioClientTransportOptions
        {
            Name = _name,
            Command = _options.Command,
            Arguments = [.. _options.Args],
        };

        if (_options.Env != null)
        {
            foreach (KeyValuePair<string, string> kv in _options.Env)
            {
                transportOptions.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        StdioClientTransport transport = new StdioClientTransport(transportOptions);
        Console.WriteLine($"[{_name}] Creating MCP client transport...");
        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: _cts.Token).ConfigureAwait(false);
        _client = client!;
        Console.WriteLine($"[{_name}] MCP client created successfully");
    }

    public async Task<List<McpToolDefinition>> ListToolsAsync()
    {
        if (_client == null) throw new InvalidOperationException("Not connected");

        List<McpToolDefinition> tools = new List<McpToolDefinition>();

        // リフレクションを使って ListToolsAsync を呼び出し
        MethodInfo? listToolsMethod = _client.GetType().GetMethod("ListToolsAsync");
        if (listToolsMethod != null)
        {
            if (listToolsMethod.Invoke(_client, [_cts.Token]) is Task resultTask)
            {
                await resultTask.ConfigureAwait(false);
                object? result = resultTask.GetType().GetProperty("Result")?.GetValue(resultTask);
                if (result is System.Collections.IEnumerable toolList)
                {
                    foreach (object? tool in toolList)
                    {
                        PropertyInfo? nameProp = tool.GetType().GetProperty("Name");
                        PropertyInfo? descProp = tool.GetType().GetProperty("Description");
                        PropertyInfo? schemaProp = tool.GetType().GetProperty("Parameters");


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

        Dictionary<string, object?> argsDict = new Dictionary<string, object?>();
        
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in arguments.EnumerateObject())
            {
                argsDict[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
            }
        }

        // リフレクションを使って CallToolAsync を呼び出し
        MethodInfo? callToolMethod = _client.GetType().GetMethod("CallToolAsync", 
            [typeof(string), typeof(Dictionary<string, object?>), typeof(CancellationToken)]);
        
        if (callToolMethod != null)
        {
            dynamic result = await (dynamic?)callToolMethod.Invoke(_client, [name, argsDict, _cts.Token])!;
            if (result != null)
            {
                dynamic contentProp = result.GetType().GetProperty("Content");
                dynamic isErrorProp = result.GetType().GetProperty("IsError");

                McpCallToolResult toolResult = new McpCallToolResult
                {
                    IsError = isErrorProp?.GetValue(result) as bool? ?? false
                };
                
                if (contentProp != null)
                {
                    dynamic content = contentProp.GetValue(result);
                    if (content is System.Collections.IEnumerable contentList)
                    {
                        foreach (object? item in contentList)
                        {
                            PropertyInfo? typeProp = item.GetType().GetProperty("Type");
                            PropertyInfo? textProp = item.GetType().GetProperty("Text");
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
            MethodInfo? disposeAsyncMethod = _client.GetType().GetMethod("DisposeAsync");
            if (disposeAsyncMethod != null)
            {
                object? result = disposeAsyncMethod.Invoke(_client, null);
                if (result is ValueTask valueTask)
                {
                    valueTask.AsTask().Wait();
                }
            }
        }
        _cts.Dispose();
    }
}
