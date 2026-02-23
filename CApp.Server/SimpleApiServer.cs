using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CApp.Server;

public class SimpleApiServer : IDisposable
{
    readonly HttpListener _listener;
    readonly CancellationTokenSource _cts = new();
    Task? _listenTask;
    readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly McpManager? _mcpManager;

#pragma warning disable CS8618
    public SimpleApiServer(string prefix, McpManager? mcpManager = null)
#pragma warning restore CS8618
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _mcpManager = mcpManager;
    }

    // UI から注入されるデリゲート
    public Func<string, Task<string?>>? ExecuteScriptAsync { get; set; }
    public Func<Task<string?>>? GetChatHistoryAsync { get; set; }

    public void Start()
    {
        _listener.Start();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch { }
        _listenTask?.Wait(2000);
    }

    async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleContextAsync(ctx), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ListenLoop exception: " + ex);
            }
        }
    }

    async Task WriteFileAsync(HttpListenerResponse res, string filePath)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);
        res.ContentLength64 = fileBytes.Length;
        await res.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length).ConfigureAwait(false);
    }

    async Task HandleContextAsync(HttpListenerContext ctx)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse res = ctx.Response;
        res.ContentType = "application/json; charset=utf-8";
        res.AddHeader("Access-Control-Allow-Origin", "*");

        try
        {
            string path = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                res.Close();
                return;
            }

            if (path.Equals("/api/mcp/execute", StringComparison.OrdinalIgnoreCase))
            {
                if (req.HttpMethod != "POST")
                {
                    res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteJsonAsync(res, new { error = "Method not allowed" });
                    return;
                }
                await HandleMcpExecuteAsync(req, res);
                return;
            }

            // テスト自動化用：WebView2 で JavaScript を実行
            if (path.Equals("/api/test/execute-script", StringComparison.OrdinalIgnoreCase))
            {
                if (req.HttpMethod != "POST")
                {
                    res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteJsonAsync(res, new { error = "Method not allowed" });
                    return;
                }
                await HandleExecuteScriptAsync(req, res);
                return;
            }

            // テスト自動化用：チャット履歴を取得
            if (path.Equals("/api/test/chat-history", StringComparison.OrdinalIgnoreCase))
            {
                if (req.HttpMethod != "GET")
                {
                    res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteJsonAsync(res, new { error = "Method not allowed" });
                    return;
                }
                await HandleGetChatHistoryAsync(req, res);
                return;
            }

            if (req.HttpMethod != "GET")
            {
                res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await WriteJsonAsync(res, new { error = "Method not allowed" });
                return;
            }

            if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "index.html");
                res.ContentType = "text/html; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            // Removed hardcoded paths for old static assets

            // Generic static file serving
            string requestedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", path.TrimStart('/'));

            // Prevent directory traversal attacks
            if (!requestedFilePath.StartsWith(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI")), StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = (int)HttpStatusCode.Forbidden;
                await WriteJsonAsync(res, new { error = "Forbidden" });
                return;
            }

            if (File.Exists(requestedFilePath))
            {
                string mimeType = GetMimeType(requestedFilePath);
                res.ContentType = mimeType;
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, requestedFilePath);
                return;
            }

            res.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteJsonAsync(res, new { error = "Not found" });
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "Internal server error", detail = ex.Message });
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    async Task HandleMcpExecuteAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string requestBody;
            using (StreamReader reader = new(req.InputStream, req.ContentEncoding))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            using JsonDocument jsonDoc = JsonDocument.Parse(requestBody);
            JsonElement root = jsonDoc.RootElement;

            if (!root.TryGetProperty("name", out JsonElement nameElem))
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteJsonAsync(res, new { error = "tool name is required" });
                return;
            }

            string toolName = nameElem.GetString() ?? "";
            JsonElement arguments = root.TryGetProperty("arguments", out JsonElement argsElem) ? argsElem : default;

            DebugLogger.Mcp($"Tool execution requested: {toolName}");
            DebugLogger.Mcp($"Tool arguments: {arguments}");
            Console.WriteLine($"[MCP] Executing tool: {toolName}");

            if (_mcpManager == null)
            {
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteJsonAsync(res, new { error = "MCP manager not initialized" });
                return;
            }

            McpCallToolResult result = await _mcpManager.CallToolAsync(toolName, arguments);

            DebugLogger.Mcp($"Tool execution completed: {toolName}");
            Console.WriteLine($"[MCP] Tool execution completed: {toolName}");

            // MCP ツールの実行結果をフロントエンドに返す
            // Content からテキストを抽出
            string? responseText = null;
            if (result.Content != null && result.Content.Count > 0)
            {
                ContentBlock? textContent = result.Content.FirstOrDefault(c => c.Type == "text");
                if (textContent != null)
                {
                    responseText = textContent.Text;
                }
            }

            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "application/json; charset=utf-8";
            
            // ツール実行結果を返す
            await WriteJsonAsync(res, new 
            {
                success = !result.IsError,
                content = responseText,
                result = result // 元の結果も含める
            });
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "MCP execution error", detail = ex.Message });
        }
    }

    /// <summary>
    /// テスト自動化用：WebView2 で JavaScript を実行
    /// </summary>
    async Task HandleExecuteScriptAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            Console.WriteLine($"[TestAPI] HandleExecuteScriptAsync called");
            
            string requestBody;
            using (StreamReader reader = new(req.InputStream, req.ContentEncoding))
            {
                requestBody = await reader.ReadToEndAsync();
            }
            
            Console.WriteLine($"[TestAPI] Request body: {requestBody}");

            using JsonDocument jsonDoc = JsonDocument.Parse(requestBody);
            JsonElement root = jsonDoc.RootElement;

            if (!root.TryGetProperty("script", out JsonElement scriptElem))
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteJsonAsync(res, new { error = "script is required" });
                return;
            }

            string script = scriptElem.GetString() ?? "";
            Console.WriteLine($"[TestAPI] Script: {script}");

            // 注入されたデリゲートでスクリプトを実行
            if (ExecuteScriptAsync != null)
            {
                Console.WriteLine($"[TestAPI] Executing script via delegate...");
                string? result = await ExecuteScriptAsync(script);
                Console.WriteLine($"[TestAPI] Script result: '{result}'");
                res.StatusCode = (int)HttpStatusCode.OK;
                res.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(res, new { result = result ?? "" });
            }
            else
            {
                Console.WriteLine($"[TestAPI] ExecuteScriptAsync delegate not available");
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteJsonAsync(res, new { error = "ExecuteScriptAsync delegate not available" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAPI] Exception: {ex.Message}");
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "Script execution error", detail = ex.Message });
        }
    }

    /// <summary>
    /// テスト自動化用：チャット履歴を取得
    /// </summary>
    async Task HandleGetChatHistoryAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            if (GetChatHistoryAsync != null)
            {
                string? history = await GetChatHistoryAsync();
                res.StatusCode = (int)HttpStatusCode.OK;
                res.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(res, new { history = history ?? "" });
            }
            else
            {
                res.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteJsonAsync(res, new { error = "GetChatHistoryAsync delegate not available" });
            }
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "Chat history fetch error", detail = ex.Message });
        }
    }

    async Task WriteJsonAsync(HttpListenerResponse res, object obj)
    {
        string json = JsonSerializer.Serialize(obj, _jsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private string GetMimeType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            _ => "application/octet-stream",
        };
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }
}
