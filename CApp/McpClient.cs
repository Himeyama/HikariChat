using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CApp;

/// <summary>
/// MCP クライアント
/// </summary>
public class McpClient : IDisposable
{
    private readonly List<McpServerProcess> _processes = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private bool _disposed;

    /// <summary>
    /// MCP 設定に基づいてサーバーを初期化
    /// </summary>
    public async Task InitializeAsync(McpSettings settings)
    {
        DebugLogger.Mcp($"InitializeAsync called: enabled={settings.Enabled}, servers={settings.McpServers.Count}");
        
        if (!settings.Enabled)
        {
            DebugLogger.Mcp("MCP is disabled");
            return;
        }

        foreach (var kvp in settings.McpServers)
        {
            var serverName = kvp.Key;
            var server = kvp.Value;
            DebugLogger.Mcp($"Connecting to server: {serverName}, command={server.Command}, args={string.Join(" ", server.Args)}");

            try
            {
                await ConnectToServerAsync(serverName, server);
                DebugLogger.Mcp($"Connected to server: {serverName}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to connect to MCP server '{serverName}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// MCP サーバーに接続
    /// </summary>
    private async Task ConnectToServerAsync(string serverName, McpServerSettings server)
    {
        switch (server.Type.ToLower())
        {
            case "stdio":
                await ConnectStdioAsync(serverName, server);
                break;
            case "sse":
                await ConnectSseAsync(serverName, server);
                break;
            case "websocket":
                await ConnectWebSocketAsync(serverName, server);
                break;
            default:
                throw new InvalidOperationException($"Unsupported MCP connection type: {server.Type}");
        }
    }

    /// <summary>
    /// stdio 接続を確立
    /// </summary>
    private async Task ConnectStdioAsync(string serverName, McpServerSettings server)
    {
        if (string.IsNullOrEmpty(server.Command))
            throw new InvalidOperationException("Command is required for stdio type");

        var startInfo = new ProcessStartInfo
        {
            FileName = server.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 環境変数を設定
        foreach (var env in server.Env)
        {
            startInfo.EnvironmentVariables[env.Key] = env.Value;
        }

        // 引数を追加
        if (server.Args.Count > 0)
        {
            startInfo.Arguments = string.Join(" ", server.Args);
        }

        var process = new Process { StartInfo = startInfo };
        process.EnableRaisingEvents = true;
        process.Exited += (s, e) => OnProcessExited(serverName, server);
        process.ErrorDataReceived += (s, e) => OnProcessError(serverName, server, e.Data);

        process.Start();
        process.BeginErrorReadLine();

        var mcpProcess = new McpServerProcess
        {
            Name = serverName,
            Settings = server,
            Process = process,
            InputStream = process.StandardInput.BaseStream,
            OutputStream = process.StandardOutput.BaseStream
        };

        _processes.Add(mcpProcess);

        // 出力読み取りタスクを開始
        _ = ReadOutputAsync(mcpProcess);

        // MCP プロトコル初期化
        await InitializeMcpProtocolAsync(mcpProcess);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 出力ストリームからレスポンスを読み取る
    /// </summary>
    private async Task ReadOutputAsync(McpServerProcess process)
    {
        var buffer = new StringBuilder();
        var stream = process.OutputStream;
        var tempBuffer = new byte[4096];

        try
        {
            while (!process.Process.HasExited && !_disposed)
            {
                int bytesRead = await stream.ReadAsync(tempBuffer, 0, tempBuffer.Length);
                if (bytesRead == 0) break;

                string chunk = Encoding.UTF8.GetString(tempBuffer, 0, bytesRead);
                LogInfo($"[{process.Name}] Received: {chunk.Trim()}");
                buffer.Append(chunk);

                // 改行で区切って処理
                while (buffer.Length > 0)
                {
                    int newlineIndex = buffer.ToString().IndexOf('\n');
                    if (newlineIndex < 0) break;

                    string line = buffer.ToString().Substring(0, newlineIndex).Trim();
                    buffer.Remove(0, newlineIndex + 1);

                    if (string.IsNullOrEmpty(line)) continue;

                    LogInfo($"[{process.Name}] Processing line: {line}");
                    
                    // JSON-RPC レスポンスを処理
                    await ProcessJsonRpcResponseAsync(line, process);
                }
            }
        }
        catch (Exception ex)
        {
            LogError($"ReadOutput error: {ex.Message}");
        }
    }

    /// <summary>
    /// JSON-RPC レスポンスを処理
    /// </summary>
    private async Task ProcessJsonRpcResponseAsync(string line, McpServerProcess process)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElem) && idElem.TryGetInt64(out long id))
            {
                // 保留中のリクエストがあれば完了
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("result", out var resultElem))
                    {
                        // result を深くコピー
                        using var resultDoc = JsonDocument.Parse(resultElem.GetRawText());
                        tcs.SetResult(resultElem.Clone());
                    }
                    else if (root.TryGetProperty("error", out var errorElem))
                    {
                        tcs.SetException(new InvalidOperationException($"MCP Error: {errorElem}"));
                    }
                }
            }
            else if (root.TryGetProperty("method", out var methodElem))
            {
                // 通知またはサーバーからの要求（例：notifications）
                string method = methodElem.GetString() ?? "";
                LogInfo($"Notification from {process.Name}: {method}");
            }
        }
        catch (Exception ex)
        {
            LogError($"ProcessJsonRpcResponse error: {ex.Message}, line={line}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// SSE 接続を確立
    /// </summary>
    private async Task ConnectSseAsync(string serverName, McpServerSettings server)
    {
        if (string.IsNullOrEmpty(server.Url))
            throw new InvalidOperationException("URL is required for SSE type");

        // TODO: SSE 接続実装
        await Task.CompletedTask;
    }

    /// <summary>
    /// WebSocket 接続を確立
    /// </summary>
    private async Task ConnectWebSocketAsync(string serverName, McpServerSettings server)
    {
        if (string.IsNullOrEmpty(server.Url))
            throw new InvalidOperationException("URL is required for WebSocket type");

        // TODO: WebSocket 接続実装
        await Task.CompletedTask;
    }

    /// <summary>
    /// MCP プロトコルを初期化
    /// </summary>
    private async Task InitializeMcpProtocolAsync(McpServerProcess process)
    {
        var initializeRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "CApp",
                    version = "1.0.0"
                }
            }
        };

        var json = JsonSerializer.Serialize(initializeRequest);
        await SendJsonRpcMessageAsync(process, json);
    }

    /// <summary>
    /// JSON-RPC メッセージを送信
    /// </summary>
    private async Task SendJsonRpcMessageAsync(McpServerProcess process, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await process.InputStream.WriteAsync(bytes, 0, bytes.Length);
        await process.InputStream.FlushAsync();
    }

    /// <summary>
    /// ツールを呼び出し
    /// </summary>
    public async Task<JsonElement?> CallToolAsync(string serverName, string toolName, JsonElement? arguments = null)
    {
        var process = _processes.Find(p => p.Name == serverName);
        if (process == null)
            throw new InvalidOperationException($"MCP server '{serverName}' is not connected");

        long id = DateTime.Now.Ticks;
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[id] = tcs;

        var request = new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        var json = JsonSerializer.Serialize(request);
        await SendJsonRpcMessageAsync(process, json);

        // タイムアウト付きで待機
        var timeoutCts = new CancellationTokenSource(30000);
        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new TimeoutException($"MCP tool call timed out: {serverName}/{toolName}");
        }
    }

    /// <summary>
    /// 利用可能なツール一覧を取得
    /// </summary>
    public async Task<List<JsonElement>> ListToolsAsync(string serverName)
    {
        var process = _processes.Find(p => p.Name == serverName);
        if (process == null)
            throw new InvalidOperationException($"MCP server '{serverName}' is not connected");

        long id = DateTime.Now.Ticks;
        var tcs = new TaskCompletionSource<JsonElement?>();
        _pendingRequests[id] = tcs;

        var request = new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/list",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(request);
        await SendJsonRpcMessageAsync(process, json);

        var timeoutCts = new CancellationTokenSource(30000);
        try
        {
            var result = await tcs.Task.WaitAsync(timeoutCts.Token);
            var tools = new List<JsonElement>();
            if (result.HasValue && result.Value.TryGetProperty("tools", out var toolsElem))
            {
                foreach (var tool in toolsElem.EnumerateArray())
                {
                    tools.Add(tool.Clone());
                }
            }
            return tools;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(id, out _);
            throw new TimeoutException($"MCP list tools timed out: {serverName}");
        }
    }

    /// <summary>
    /// サーバー名を取得
    /// </summary>
    public List<string> GetConnectedServers()
    {
        return _processes.Select(p => p.Name).ToList();
    }

    private void OnProcessExited(string serverName, McpServerSettings server)
    {
        LogError($"MCP server '{serverName}' process exited");
    }

    private void OnProcessError(string serverName, McpServerSettings server, string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            LogError($"MCP server '{serverName}' error: {error}");
        }
    }

    private void LogError(string message)
    {
        DebugLogger.Error(message);
        Console.WriteLine($"[MCP ERROR] {message}");
    }

    private void LogInfo(string message)
    {
        DebugLogger.Info(message);
        Console.WriteLine($"[MCP INFO] {message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var process in _processes)
        {
            try
            {
                if (!process.Process.HasExited)
                {
                    process.Process.Kill();
                }
                process.Dispose();
            }
            catch { }
        }

        _processes.Clear();
        _pendingRequests.Clear();
        _disposed = true;
    }
}

/// <summary>
/// MCP サーバープロセス情報
/// </summary>
public class McpServerProcess : IDisposable
{
    public string Name { get; set; } = "";
    public McpServerSettings Settings { get; set; } = new();
    public Process Process { get; set; } = new();
    public Stream InputStream { get; set; } = Stream.Null;
    public Stream OutputStream { get; set; } = Stream.Null;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (!Process.HasExited)
            {
                Process.Kill();
            }
        }
        catch { }

        Process.Dispose();
        InputStream.Dispose();
        OutputStream.Dispose();
        _disposed = true;
    }
}
