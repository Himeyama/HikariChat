using System;
using System.Collections.Generic;
using System.IO;
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
    public string? InputSchemaJson { get; set; }
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
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_client_wrapper.log");
        void Log(string msg) => File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{_name}] {msg}{Environment.NewLine}");
        
        if (_client == null) throw new InvalidOperationException("Not connected");

        List<McpToolDefinition> tools = new List<McpToolDefinition>();

        // リフレクションを使って ListToolsAsync を呼び出し
        // ListToolsAsync(RequestOptions? options, CancellationToken cancellationToken) のオーバーロードを取得
        var methods = _client.GetType().GetMethods();
        MethodInfo? listToolsMethod = null;
        foreach (var method in methods)
        {
            if (method.Name == "ListToolsAsync" 
                && method.GetParameters().Length == 2
                && method.GetParameters()[1].ParameterType == typeof(CancellationToken))
            {
                listToolsMethod = method;
                break;
            }
        }
        
        Log($"Found ListToolsAsync method: {listToolsMethod != null}");
        Log($"ListToolsAsync return type: {listToolsMethod?.ReturnType.FullName}");
        
        if (listToolsMethod != null)
        {
            // null と CancellationToken を渡して呼び出し
            Log($"Calling ListToolsAsync...");
            try
            {
                Log($"Invoking ListToolsAsync with parameters: null, {_cts.Token}");
                var invokeResult = listToolsMethod.Invoke(_client, [null, _cts.Token]);
                Log($"Invoke result type: {invokeResult?.GetType().FullName ?? "null"}");
                
                // ValueTask または Task のいずれかを処理
                Task? task = null;
                if (invokeResult is Task t)
                {
                    task = t;
                    Log("Result is Task");
                }
                else if (invokeResult != null && invokeResult.GetType().Name.StartsWith("ValueTask"))
                {
                    // ValueTask<TResult> から Result プロパティを取得
                    var resultProp = invokeResult.GetType().GetProperty("Result");
                    if (resultProp != null)
                    {
                        var valueTaskResult = resultProp.GetValue(invokeResult);
                        Log($"ValueTask result type: {valueTaskResult?.GetType().FullName ?? "null"}");
                        
                        // IList として処理
                        if (valueTaskResult is System.Collections.IList resultList)
                        {
                            Log($"Result count: {resultList.Count}");
                            
                            for (int i = 0; i < resultList.Count; i++)
                            {
                                var tool = resultList[i];
                                Log($"Tool[{i}] type: {tool?.GetType().FullName ?? "null"}");
                                
                                if (tool != null)
                                {
                                    var nameProp = tool.GetType().GetProperty("Name");
                                    var descProp = tool.GetType().GetProperty("Description");
                                    var schemaProp = tool.GetType().GetProperty("Parameters");
                                    
                                    var toolDef = new McpToolDefinition
                                    {
                                        Name = nameProp?.GetValue(tool)?.ToString() ?? "",
                                        Description = descProp?.GetValue(tool)?.ToString() ?? "",
                                        InputSchemaJson = schemaProp?.GetValue(tool) != null ? 
                                            System.Text.Json.JsonSerializer.Serialize(schemaProp.GetValue(tool)) : 
                                            null
                                    };
                                    Log($"Found tool: {toolDef.Name} - {toolDef.Description}");
                                    tools.Add(toolDef);
                                }
                            }
                        }
                        else if (valueTaskResult is System.Collections.IEnumerable enumerable)
                        {
                            int toolCount = 0;
                            foreach (var tool in enumerable)
                            {
                                toolCount++;
                                Log($"Tool[{toolCount}] type: {tool?.GetType().FullName ?? "null"}");
                                
                                if (tool != null)
                                {
                                    var nameProp = tool.GetType().GetProperty("Name");
                                    var descProp = tool.GetType().GetProperty("Description");
                                    var schemaProp = tool.GetType().GetProperty("Parameters");
                                    
                                    var toolDef = new McpToolDefinition
                                    {
                                        Name = nameProp?.GetValue(tool)?.ToString() ?? "",
                                        Description = descProp?.GetValue(tool)?.ToString() ?? "",
                                        InputSchemaJson = schemaProp?.GetValue(tool) != null ? 
                                            System.Text.Json.JsonSerializer.Serialize(schemaProp.GetValue(tool)) : 
                                            null
                                    };
                                    Log($"Found tool: {toolDef.Name}");
                                    tools.Add(toolDef);
                                }
                            }
                            Log($"Total tools from IEnumerable: {toolCount}");
                        }
                        else
                        {
                            Log($"ValueTask result is neither IList nor IEnumerable: {valueTaskResult?.GetType().FullName ?? "null"}");
                        }
                        
                        Log($"Returning {tools.Count} tools");
                        return tools;
                    }
                }
                
                if (task != null)
                {
                    Log($"Waiting for task to complete...");
                    await task.ConfigureAwait(false);
                    Log($"Task completed.");
                    
                    // Result プロパティを取得
                    var resultProp = task.GetType().GetProperty("Result");
                    Log($"Result property: {resultProp?.Name ?? "null"}");
                    object? result = resultProp?.GetValue(task);
                    
                    Log($"ListToolsAsync result type: {result?.GetType().FullName ?? "null"}");
                    Log($"ListToolsAsync result is IList: {result is System.Collections.IList}");
                    
                    if (result is System.Collections.IList resultList)
                    {
                        Log($"Result count: {resultList.Count}");
                        
                        for (int i = 0; i < resultList.Count; i++)
                        {
                            var tool = resultList[i];
                            Log($"Tool[{i}] type: {tool?.GetType().FullName ?? "null"}");
                            
                            if (tool != null)
                            {
                                var nameProp = tool.GetType().GetProperty("Name");
                                var descProp = tool.GetType().GetProperty("Description");
                                var schemaProp = tool.GetType().GetProperty("Parameters");
                                
                                var toolDef = new McpToolDefinition
                                {
                                    Name = nameProp?.GetValue(tool)?.ToString() ?? "",
                                    Description = descProp?.GetValue(tool)?.ToString() ?? "",
                                    InputSchemaJson = schemaProp?.GetValue(tool) != null ? 
                                        System.Text.Json.JsonSerializer.Serialize(schemaProp.GetValue(tool)) : 
                                        null
                                };
                                Log($"Found tool: {toolDef.Name} - {toolDef.Description}");
                                tools.Add(toolDef);
                            }
                        }
                    }
                    else if (result is System.Collections.IEnumerable enumerable)
                    {
                        int toolCount = 0;
                        foreach (var tool in enumerable)
                        {
                            toolCount++;
                            Log($"Tool[{toolCount}] type: {tool?.GetType().FullName ?? "null"}");
                            
                            if (tool != null)
                            {
                                var nameProp = tool.GetType().GetProperty("Name");
                                var descProp = tool.GetType().GetProperty("Description");
                                var schemaProp = tool.GetType().GetProperty("Parameters");
                                
                                var toolDef = new McpToolDefinition
                                {
                                    Name = nameProp?.GetValue(tool)?.ToString() ?? "",
                                    Description = descProp?.GetValue(tool)?.ToString() ?? "",
                                    InputSchemaJson = schemaProp?.GetValue(tool) != null ? 
                                        System.Text.Json.JsonSerializer.Serialize(schemaProp.GetValue(tool)) : 
                                        null
                                };
                                Log($"Found tool: {toolDef.Name}");
                                tools.Add(toolDef);
                            }
                        }
                        Log($"Total tools from IEnumerable: {toolCount}");
                    }
                    else
                    {
                        Log($"Result is neither IList nor IEnumerable: {result?.GetType().FullName ?? "null"}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in ListToolsAsync: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else
        {
            Log($"ListToolsAsync method not found!");
        }
        
        Log($"Returning {tools.Count} tools");
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
        else if (arguments.ValueKind == JsonValueKind.Undefined || arguments.ValueKind == JsonValueKind.Null)
        {
            // 引数が指定されていない場合は空の辞書を使用
            argsDict = new Dictionary<string, object?>();
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
