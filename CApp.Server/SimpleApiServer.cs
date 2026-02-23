using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
    static readonly HttpClient _httpClient = new();

    private readonly McpManager? _mcpManager;
    private ApiSettings _currentSettings = new();

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

    // Frontend の ChatMessage 構造をミラーリング
    public class FrontendChatMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("tool_calls")]
        public JsonElement? ToolCalls { get; set; } // tool_calls は JsonElement のまま扱う
    }

    // AI API に送信する ChatMessage 構造
    public class OpenAIChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
        // 他のプロパティは AI サービスによってサポートされるもののみ
        // 例: name, tool_calls, tool_call_id などは必要に応じて追加する
        [JsonPropertyName("tool_calls")]
        public JsonElement? ToolCalls { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; } // tool response の name 用
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; } // tool response の tool_call_id 用
    }

    public async Task InitializeSettingsAsync(ApiSettings settings)
    {
        _currentSettings = settings;
        if (_mcpManager != null)
        {
            await _mcpManager.UpdateSettingsAsync(settings);
        }
    }

    public (bool enabled, int activeCount, int totalCount) GetMcpStatus()
    {
        return _mcpManager?.GetStatus() ?? (false, 0, 0);
    }

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

            if (path.Equals("/api/chat", StringComparison.OrdinalIgnoreCase))
            {
                if (req.HttpMethod != "POST")
                {
                    res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await WriteJsonAsync(res, new { error = "Method not allowed" });
                    return;
                }
                await HandleChatApiAsync(req, res);
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

            var result = await _mcpManager.CallToolAsync(toolName, arguments);

            DebugLogger.Mcp($"Tool execution completed: {toolName}");
            Console.WriteLine($"[MCP] Tool execution completed: {toolName}");

            // MCP ツールの実行結果をフロントエンドに返す
            // Content からテキストを抽出
            string? responseText = null;
            if (result.Content != null && result.Content.Count > 0)
            {
                var textContent = result.Content.FirstOrDefault(c => c.Type == "text");
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
                var result = await ExecuteScriptAsync(script);
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
                var history = await GetChatHistoryAsync();
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

    async Task HandleChatApiAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            string requestBody;
            using (StreamReader reader = new(req.InputStream, req.ContentEncoding))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            DebugLogger.Api($"Incoming request: {requestBody}");

            using JsonDocument jsonDoc = JsonDocument.Parse(requestBody);
            JsonElement messagesRoot = jsonDoc.RootElement;

            if (!messagesRoot.TryGetProperty("messages", out JsonElement messagesElement))
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteJsonAsync(res, new { error = "messages プロパティが必要です" });
                return;
            }

            string apiKey = messagesRoot.TryGetProperty("apiKey", out JsonElement apiKeyElem)
                ? apiKeyElem.GetString() ?? ""
                : "";
            string apiEndpoint = messagesRoot.TryGetProperty("apiEndpoint", out JsonElement endpointElem)
                ? endpointElem.GetString() ?? "https://api.openai.com/v1/chat/completions"
                : "https://api.openai.com/v1/chat/completions";
            string model = messagesRoot.TryGetProperty("model", out JsonElement modelElem)
                ? modelElem.GetString() ?? "gpt-4o-mini"
                : "gpt-4o-mini";
            string apiType = messagesRoot.TryGetProperty("apiType", out JsonElement apiTypeElem)
                ? apiTypeElem.GetString() ?? "chat_completions"
                : "chat_completions";
            string endpointPreset = messagesRoot.TryGetProperty("endpointPreset", out JsonElement endpointPresetElem)
                ? endpointPresetElem.GetString() ?? "openai"
                : "openai";
            string azureDeployment = messagesRoot.TryGetProperty("azureDeployment", out JsonElement azureDeploymentElem)
                ? azureDeploymentElem.GetString() ?? ""
                : "";
            bool streaming = messagesRoot.TryGetProperty("streaming", out JsonElement streamingElem)
                ? streamingElem.GetBoolean()
                : false;
            bool mcpEnabled = messagesRoot.TryGetProperty("mcpEnabled", out JsonElement mcpEnabledElem)
                ? mcpEnabledElem.GetBoolean()
                : false;

            DebugLogger.Api($"[Chat API] apiType={apiType}, endpointPreset={endpointPreset}, apiEndpoint={apiEndpoint}, model={model}, streaming={streaming}, mcpEnabled={mcpEnabled}");
            Console.WriteLine($"[API] Request: apiType={apiType}, endpointPreset={endpointPreset}, apiEndpoint={apiEndpoint}, model={model}, streaming={streaming}, mcpEnabled={mcpEnabled}");

            if (string.IsNullOrEmpty(apiKey) && endpointPreset != "ollama")
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteJsonAsync(res, new { error = "API キーが必要です" });
                return;
            }

            // MCP ツールを取得
            List<object>? mcpTools = null;
            if (mcpEnabled && _mcpManager != null)
            {
                mcpTools = await _mcpManager.GetOpenAiToolsAsync();
                DebugLogger.Mcp($"Injected {mcpTools.Count} tools for API request");
                Console.WriteLine($"[MCP] Injected {mcpTools.Count} tools");
            }
            else if (mcpEnabled && _mcpManager == null)
            {
                Console.WriteLine($"[MCP] MCP is enabled but _mcpManager is null");
            }

            if (streaming)
            {
                await HandleStreamingAsync(res, apiType, apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment, mcpTools);
                
                // 最後に [DONE] を送って閉じる
                try {
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), 0, 14);
                    res.OutputStream.Close();
                } catch { }
                return;
            }

            string? responseJson = null;
            // string? warningInfo = null;
            
            try
            {
                responseJson = apiType switch
                {
                    "responses" => await HandleResponsesApiAsync(apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment),
                    "anthropic" => await HandleAnthropicApiAsync(apiEndpoint, apiKey, model, messagesElement),
                    "gemini" => await HandleGeminiApiAsync(apiEndpoint, apiKey, model, messagesElement),
                    _ => await HandleChatCompletionsApiAsync(apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment, mcpTools)
                };
            }
            catch (HttpRequestException ex) when (mcpTools != null && (ex.Message.Contains("does not support tools") || ex.Message.Contains("Unrecognized parameter: 'tools'")))
            {
                // ツールなしで再試行（非ストリーミング）
                DebugLogger.Api("Retrying non-streaming request without tools...");
                // warningInfo = "使用中のモデルがツール実行に対応していないため、ツールなしで回答を生成します。";
                responseJson = apiType switch
                {
                    "responses" => await HandleResponsesApiAsync(apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment),
                    "anthropic" => await HandleAnthropicApiAsync(apiEndpoint, apiKey, model, messagesElement),
                    "gemini" => await HandleGeminiApiAsync(apiEndpoint, apiKey, model, messagesElement),
                    _ => await HandleChatCompletionsApiAsync(apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment, null)
                };
            }

            if (responseJson == null) throw new Exception("API response is null");

            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "application/json; charset=utf-8";
            
            // 警告がある場合はレスポンスに含める（簡易的に結合するか、フロントエンドでパースできるようにする）
            // ここでは responseJson をそのまま返しつつ、DebugLogger に残す。
            // 非ストリーミング時は responseJson 自体を加工するのは難しいため、一旦ストリーミングを優先。
            
            await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(responseJson));
        }
        catch (WebException ex) when (ex.Response != null)
        {
            using HttpWebResponse errorResponse = (HttpWebResponse)ex.Response;
            using Stream errorStream = errorResponse.GetResponseStream();
            using StreamReader errorReader = new(errorStream);
            string errorJson = await errorReader.ReadToEndAsync();

            DebugLogger.Error($"HTTP Error: {errorResponse.StatusCode} - {errorJson}");
            res.StatusCode = (int)errorResponse.StatusCode;
            await WriteJsonAsync(res, new { error = $"API エラー：{errorResponse.StatusCode}", detail = errorJson });
        }
        catch (Exception ex)
        {
            DebugLogger.Error($"Internal Server Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
            Console.WriteLine($"[API Error] {ex}");
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "内部サーバーエラー", detail = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    async Task<bool> HandleStreamingAsync(HttpListenerResponse res, string apiType, string apiEndpoint, string apiKey, string model, JsonElement messagesElement, string endpointPreset, string azureDeployment, List<object>? tools = null)
    {
        try
        {
            string endpoint = apiEndpoint;
            if (endpointPreset == "azure_openai" && azureDeployment != "")
            {
                endpoint = endpoint.Replace("{deployment}", azureDeployment);
            }

            DebugLogger.Api($"Streaming to: {endpoint}");

            // messagesElement を FrontendChatMessage のリストにデシリアライズ
            List<FrontendChatMessage>? frontendMessages = JsonSerializer.Deserialize<List<FrontendChatMessage>>(messagesElement);

            // AI API に送るメッセージリストを構築 (id などの不要なプロパティを除去)
            List<OpenAIChatMessage> apiMessages = new();
            if (frontendMessages != null)
            {
                foreach (var msg in frontendMessages)
                {
                    // ロールが空のメッセージはスキップ
                    if (string.IsNullOrEmpty(msg.Role))
                    {
                        DebugLogger.Api($"Skipping message with empty role: {JsonSerializer.Serialize(msg)}");
                        continue; 
                    }

                    // tool ロールのメッセージはツール呼び出し結果であり、APIには送らない
                    if (msg.Role == "tool") continue;

                    // system ロールのメッセージは Anthropic 以外では content が空のケースがあるので注意
                    // ここではシンプルに role と content のみを持つメッセージを作成
                    apiMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
                }
            }


            object requestPayload;
            // Ollama や Gemini など、tools パラメータを直接サポートしないエンドポイントでは tools を送らない
            List<object>? toolsToSend = (tools != null && tools.Count > 0 && endpointPreset != "ollama" && endpointPreset != "gemini") ? tools : null;

            if (apiType == "anthropic")
            {
                List<OpenAIChatMessage> anthropicMessages = new List<OpenAIChatMessage>();
                string? systemMessage = null;

                if (apiMessages != null) // apiMessages から構築
                {
                    foreach (var msg in apiMessages)
                    {
                        if (msg.Role == "system")
                        {
                            systemMessage = msg.Content;
                        }
                        else
                        {
                            anthropicMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
                        }
                    }
                }

                requestPayload = new
                {
                    model,
                    max_tokens = 4096,
                    messages = anthropicMessages,
                    system = systemMessage,
                    stream = true,
                    tools = toolsToSend
                };
            }
            else
            {
                requestPayload = new
                {
                    model,
                    messages = apiMessages, // apiMessages を使用
                    stream = true,
                    tools = toolsToSend
                };
            }

            string requestJson = JsonSerializer.Serialize(requestPayload, _jsonOptions);
            DebugLogger.Api($"Request JSON: {requestJson}");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            if (endpointPreset == "azure_openai")
            {
                httpRequest.Headers.Add("api-key", apiKey);
            }
            else if (!string.IsNullOrEmpty(apiKey))
            {
                httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            if (apiType == "anthropic")
            {
                httpRequest.Headers.Add("x-api-key", apiKey);
                httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            }

            using HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (!httpResponse.IsSuccessStatusCode)
            {
                string errorBody = await httpResponse.Content.ReadAsStringAsync();
                DebugLogger.Api($"Streaming HTTP error: {httpResponse.StatusCode} - {errorBody}");
                // ツール非対応エラーのリトライロジックを削除
                // 現在は toolsToSend で制御するため、ここでは一般的なエラーとして扱う
                httpResponse.EnsureSuccessStatusCode(); // エラーをここで投げる
            }

            // 成功してからレスポンスヘッダーを書き込む
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "text/event-stream; charset=utf-8";
            res.SendChunked = true;

            using Stream responseStream = await httpResponse.Content.ReadAsStreamAsync();
            using StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data: "))
                {
                    string data = line.Substring(6);
                    if (data == "[DONE]")
                    {
                        break;
                    }

                    byte[] chunk = Encoding.UTF8.GetBytes($"data: {data}\n\n");
                    await res.OutputStream.WriteAsync(chunk, 0, chunk.Length);
                    await res.OutputStream.FlushAsync();
                }
            }
            DebugLogger.Api("Streaming session finished");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Api($"[Streaming Error] {ex.Message}");
            Console.WriteLine($"[Streaming Error] {ex.Message}");
            // エラーを適切に処理するため、ここでは例外を再スロー
            throw; 
        }
    }

    async Task<string> HandleChatCompletionsApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement, string endpointPreset, string azureDeployment, List<object>? tools = null)
    {
        string endpoint = apiEndpoint;
        if (endpointPreset == "azure_openai" && azureDeployment != "")
        {
            endpoint = endpoint.Replace("{deployment}", azureDeployment);
        }

        // messagesElement を FrontendChatMessage のリストにデシリアライズ
        List<FrontendChatMessage>? frontendMessages = JsonSerializer.Deserialize<List<FrontendChatMessage>>(messagesElement);

        // AI API に送るメッセージリストを構築 (id などの不要なプロパティを除去)
        List<OpenAIChatMessage> apiMessages = new();
        if (frontendMessages != null)
        {
            foreach (var msg in frontendMessages)
            {
                // ロールが空のメッセージはスキップ
                if (string.IsNullOrEmpty(msg.Role))
                {
                    DebugLogger.Api($"Skipping message with empty role: {JsonSerializer.Serialize(msg)}");
                    continue; 
                }

                if (msg.Role == "tool") continue;
                apiMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
            }
        }

        // Ollama や Gemini など、tools パラメータを直接サポートしないエンドポイントでは tools を送らない
        List<object>? toolsToSend = (tools != null && tools.Count > 0 && endpointPreset != "ollama" && endpointPreset != "gemini") ? tools : null;

        var requestPayload = new
        {
            model,
            messages = apiMessages, // apiMessages を使用
            tools = toolsToSend // ToolsToSend を使用
        };

        string requestJson = JsonSerializer.Serialize(requestPayload, _jsonOptions);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        if (endpointPreset == "azure_openai")
        {
            httpRequest.Headers.Add("api-key", apiKey);
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest);
        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        
        if (!httpResponse.IsSuccessStatusCode)
        {
            DebugLogger.Api($"HTTP Error: {httpResponse.StatusCode} - {responseJson}");
            // ツール非対応エラーのリトライロジックを削除
            // 現在は toolsToSend で制御するため、ここでは一般的なエラーとして扱う
            httpResponse.EnsureSuccessStatusCode(); // エラーをここで投げる
        }

        return responseJson;
    }

    async Task<string> HandleResponsesApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement, string endpointPreset, string azureDeployment)
    {
        // messagesElement を FrontendChatMessage のリストにデシリアialize
        List<FrontendChatMessage>? frontendMessages = JsonSerializer.Deserialize<List<FrontendChatMessage>>(messagesElement);

        // AI API に送るメッセージリストを構築 (id などの不要なプロパティを除去)
        List<OpenAIChatMessage> apiMessages = new();
        if (frontendMessages != null)
        {
            foreach (var msg in frontendMessages)
            {
                // ロールが空のメッセージはスキップ
                if (string.IsNullOrEmpty(msg.Role))
                {
                    DebugLogger.Api($"Skipping message with empty role: {JsonSerializer.Serialize(msg)}");
                    continue; 
                }

                if (msg.Role == "tool") continue;
                apiMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
            }
        }

        List<object> inputMessages = new();
        if (apiMessages != null) // apiMessages から構築
        {
            foreach (var msg in apiMessages)
            {
                 // Responses API は特定のフォーマットを期待する可能性があるので、
                 // ここではシンプルに role と content のみを持つメッセージを作成
                inputMessages.Add(new { role = msg.Role, content = msg.Content });
            }
        }

        var requestPayload = new
        {
            model,
            input = inputMessages
        };

        string requestJson = JsonSerializer.Serialize(requestPayload, _jsonOptions);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, apiEndpoint);
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest);
        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        httpResponse.EnsureSuccessStatusCode();
        return responseJson;
    }

    async Task<string> HandleAnthropicApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement)
    {
        // messagesElement を FrontendChatMessage のリストにデシリアライズ
        List<FrontendChatMessage>? frontendMessages = JsonSerializer.Deserialize<List<FrontendChatMessage>>(messagesElement);

        // AI API に送るメッセージリストを構築 (id などの不要なプロパティを除去)
        List<OpenAIChatMessage> apiMessages = new();
        if (frontendMessages != null)
        {
            foreach (var msg in frontendMessages)
            {
                // ロールが空のメッセージはスキップ
                if (string.IsNullOrEmpty(msg.Role))
                {
                    DebugLogger.Api($"Skipping message with empty role: {JsonSerializer.Serialize(msg)}");
                    continue; 
                }

                if (msg.Role == "tool") continue;
                apiMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
            }
        }

        List<OpenAIChatMessage> anthropicMessages = new();
        string? systemMessage = null;

        if (apiMessages != null) // apiMessages から構築
        {
            foreach (var msg in apiMessages)
            {
                if (msg.Role == "system")
                {
                    systemMessage = msg.Content;
                }
                else
                {
                    anthropicMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
                }
            }
        }

        var requestPayload = new
        {
            model,
            max_tokens = 4096,
            messages = anthropicMessages,
            system = systemMessage
        };

        string requestJson = JsonSerializer.Serialize(requestPayload, _jsonOptions);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, apiEndpoint);
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        using HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest);
        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        httpResponse.EnsureSuccessStatusCode();
        return responseJson;
    }

    async Task<string> HandleGeminiApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement)
    {
        string endpoint = apiEndpoint.Replace("{model}", model);
        if (!endpoint.Contains("key="))
        {
            endpoint += (endpoint.Contains("?") ? "&" : "?") + "key=" + apiKey;
        }

        // messagesElement を FrontendChatMessage のリストにデシリアライズ
        List<FrontendChatMessage>? frontendMessages = JsonSerializer.Deserialize<List<FrontendChatMessage>>(messagesElement);

        // AI API に送るメッセージリストを構築 (id などの不要なプロパティを除去)
        List<OpenAIChatMessage> apiMessages = new();
        if (frontendMessages != null)
        {
            foreach (var msg in frontendMessages)
            {
                // ロールが空のメッセージはスキップ
                if (string.IsNullOrEmpty(msg.Role))
                {
                    DebugLogger.Api($"Skipping message with empty role: {JsonSerializer.Serialize(msg)}");
                    continue; 
                }

                if (msg.Role == "tool") continue;
                apiMessages.Add(new OpenAIChatMessage { Role = msg.Role, Content = msg.Content });
            }
        }

        List<object> contents = new();

        if (apiMessages != null) // apiMessages から構築
        {
            foreach (var msg in apiMessages)
            {
                string geminiRole = (msg.Role == "assistant") ? "model" : "user";
                contents.Add(new { role = geminiRole, parts = new[] { new { text = msg.Content } } });
            }
        }

        var requestPayload = new { contents };

        string requestJson = JsonSerializer.Serialize(requestPayload, _jsonOptions);

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using HttpResponseMessage httpResponse = await _httpClient.SendAsync(httpRequest);
        string responseJson = await httpResponse.Content.ReadAsStringAsync();
        httpResponse.EnsureSuccessStatusCode();
        return responseJson;
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }
}
