using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CApp.Server;

public class McpClient : IDisposable
{
    private Process? _process;
    private readonly string _name;
    private readonly McpServerConfig _config;
    private int _requestId = 0;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Name => _name;

    public McpClient(string name, McpServerConfig config)
    {
        _name = name;
        _config = config;
    }

    public async Task StartAsync()
    {
        DebugLogger.Mcp($"[{_name}] Starting process: {_config.Command} {string.Join(" ", _config.Args)}");
        
        ProcessStartInfo psi = new()
        {
            FileName = _config.Command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var arg in _config.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (_config.Env != null)
        {
            foreach (var kv in _config.Env)
            {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        _process = Process.Start(psi);
        if (_process == null) throw new Exception($"Failed to start MCP server: {_name}");
        
        DebugLogger.Mcp($"[{_name}] Process started, PID: {_process.Id}");

        _ = Task.Run(ListenOutputAsync);
        _ = Task.Run(ListenErrorAsync);

        // Initialize MCP
        DebugLogger.Mcp($"[{_name}] Initializing MCP protocol...");
        await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "CApp", version = "1.0.0" }
        });

        DebugLogger.Mcp($"[{_name}] Sending initialized notification...");
        await SendNotificationAsync("notifications/initialized", new { });
        DebugLogger.Mcp($"[{_name}] MCP server initialized successfully");
    }

    private async Task ListenOutputAsync()
    {
        if (_process == null) return;
        using var reader = _process.StandardOutput;
        while (!_process.HasExited)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var idProp))
                {
                    int id = idProp.GetInt32();
                    if (_pendingRequests.TryGetValue(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var result))
                        {
                            tcs.SetResult(result.Clone());
                        }
                        else if (root.TryGetProperty("error", out var error))
                        {
                            tcs.SetException(new Exception(error.GetRawText()));
                        }
                        _pendingRequests.Remove(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MCP:{_name}] Parse error: {ex.Message}\nLine: {line}");
            }
        }
    }

    private async Task ListenErrorAsync()
    {
        if (_process == null) return;
        using var reader = _process.StandardError;
        while (!_process.HasExited)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            DebugLogger.Mcp($"[{_name} ERR] {line}");
            Console.WriteLine($"[MCP:{_name} ERR] {line}");
        }
        DebugLogger.Mcp($"[{_name}] Process exited, ExitCode: {_process.ExitCode}");
    }

    public async Task<JsonElement> SendRequestAsync(string method, object @params)
    {
        int id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement>();
        _pendingRequests[id] = tcs;

        var request = new
        {
            jsonrpc = "2.0",
            id = id,
            method = method,
            @params = @params
        };

        string json = JsonSerializer.Serialize(request);
        await _writeLock.WaitAsync();
        try
        {
            if (_process == null || _process.HasExited) throw new Exception("Process not running");
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }

        return await tcs.Task;
    }

    public async Task SendNotificationAsync(string method, object @params)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = method,
            @params = @params
        };

        string json = JsonSerializer.Serialize(notification);
        await _writeLock.WaitAsync();
        try
        {
            if (_process == null || _process.HasExited) return;
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<JsonElement> ListToolsAsync()
    {
        return await SendRequestAsync("tools/list", new { });
    }

    public async Task<JsonElement> CallToolAsync(string name, JsonElement arguments)
    {
        return await SendRequestAsync("tools/call", new
        {
            name = name,
            arguments = arguments
        });
    }

    public void Dispose()
    {
        if (_process != null && !_process.HasExited)
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
        }
        _writeLock.Dispose();
    }
}
