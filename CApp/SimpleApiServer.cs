using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CApp;

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

    private readonly McpManager _mcpManager = new();
    private ApiSettings _currentSettings = new();

    public SimpleApiServer(string prefix)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    public async Task InitializeSettingsAsync(ApiSettings settings)
    {
        _currentSettings = settings;
        await _mcpManager.UpdateSettingsAsync(settings);
    }

    public (bool enabled, int activeCount, int totalCount) GetMcpStatus()
    {
        return _mcpManager.GetStatus();
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

            if (path.Equals("/styles.css", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "styles.css");
                res.ContentType = "text/css; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            if (path.Equals("/app.js", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "app.js");
                res.ContentType = "application/javascript; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            if (path.Equals("/settings.html", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "settings.html");
                res.ContentType = "text/html; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
                return;
            }

            if (path.Equals("/settings.js", StringComparison.OrdinalIgnoreCase))
            {
                string home = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "EditorUI", "settings.js");
                res.ContentType = "application/javascript; charset=utf-8";
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteFileAsync(res, home);
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

            var result = await _mcpManager.CallToolAsync(toolName, arguments);
            
            res.StatusCode = (int)HttpStatusCode.OK;
            res.ContentType = "application/json; charset=utf-8";
            await WriteJsonAsync(res, result);
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "MCP execution error", detail = ex.Message });
        }
    }

    async Task WriteJsonAsync(HttpListenerResponse res, object obj)
    {
        string json = JsonSerializer.Serialize(obj, _jsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
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
            if (mcpEnabled)
            {
                mcpTools = await _mcpManager.GetOpenAiToolsAsync();
                Console.WriteLine($"[MCP] Injected {mcpTools.Count} tools");
            }

            if (streaming)
            {
                bool success = await HandleStreamingAsync(res, apiType, apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment, mcpTools);
                if (!success && mcpTools != null)
                {
                    // ツールなしで再試行
                    DebugLogger.Api("Retrying streaming request without tools...");
                    
                    // UI 側に通知を送る
                    try {
                        var info = new { info = "使用中のモデルがツール実行に対応していないため、ツールなしで回答を生成します。" };
                        byte[] infoChunk = Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(info)}\n\n");
                        await res.OutputStream.WriteAsync(infoChunk, 0, infoChunk.Length);
                        await res.OutputStream.FlushAsync();
                    } catch { }

                    await HandleStreamingAsync(res, apiType, apiEndpoint, apiKey, model, messagesElement, endpointPreset, azureDeployment, null);
                }
                
                // 最後に [DONE] を送って閉じる
                try {
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), 0, 14);
                    res.OutputStream.Close();
                } catch { }
                return;
            }

            string? responseJson = null;
            string? warningInfo = null;
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
                warningInfo = "使用中のモデルがツール実行に対応していないため、ツールなしで回答を生成します。";
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

            res.StatusCode = (int)errorResponse.StatusCode;
            await WriteJsonAsync(res, new { error = $"API エラー：{errorResponse.StatusCode}", detail = errorJson });
        }
        catch (Exception ex)
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonAsync(res, new { error = "内部サーバーエラー", detail = ex.Message });
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

            object[]? messages = JsonSerializer.Deserialize<object[]>(messagesElement);

            object requestPayload;
            if (apiType == "anthropic")
            {
                List<object> anthropicMessages = new List<object>();
                string? systemMessage = null;

                if (messages != null)
                {
                    foreach (object msg in messages)
                    {
                        JsonElement elem = JsonSerializer.SerializeToElement(msg);
                        if (elem.TryGetProperty("role", out JsonElement roleElem) && elem.TryGetProperty("content", out JsonElement contentElem))
                        {
                            string role = roleElem.GetString() ?? "";
                            if (role == "system")
                            {
                                systemMessage = contentElem.GetString();
                            }
                            else
                            {
                                anthropicMessages.Add(new { role = role, content = contentElem.GetString() ?? "" });
                            }
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
                    tools = (tools != null && tools.Count > 0) ? tools : null
                };
            }
            else
            {
                requestPayload = new
                {
                    model,
                    messages,
                    stream = true,
                    tools = (tools != null && tools.Count > 0) ? tools : null
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
                
                // ツール非対応エラーの場合は呼び出し元に通知してリトライさせる
                if (tools != null && (errorBody.Contains("does not support tools") || errorBody.Contains("Unrecognized parameter: 'tools'")))
                {
                    DebugLogger.Api("Model does not support tools. Signalling retry without tools.");
                    return false;
                }
            }
            httpResponse.EnsureSuccessStatusCode();

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
            return false; // エラー時もリトライの可能性を考慮して false を返す
        }
    }

    async Task<string> HandleChatCompletionsApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement, string endpointPreset, string azureDeployment, List<object>? tools = null)
    {
        string endpoint = apiEndpoint;
        if (endpointPreset == "azure_openai" && azureDeployment != "")
        {
            endpoint = endpoint.Replace("{deployment}", azureDeployment);
        }

        var requestPayload = new
        {
            model,
            messages = JsonSerializer.Deserialize<object[]>(messagesElement),
            tools = (tools != null && tools.Count > 0) ? tools : null
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
            // ツール非対応エラーの場合はメッセージに含めて投げる
            if (tools != null && (responseJson.Contains("does not support tools") || responseJson.Contains("Unrecognized parameter: 'tools'")))
            {
                throw new HttpRequestException($"Model does not support tools: {responseJson}");
            }
        }

        httpResponse.EnsureSuccessStatusCode();
        return responseJson;
    }

    async Task<string> HandleResponsesApiAsync(string apiEndpoint, string apiKey, string model, JsonElement messagesElement, string endpointPreset, string azureDeployment)
    {
        object[]? messages = JsonSerializer.Deserialize<object[]>(messagesElement);
        List<object> inputMessages = new();
        if (messages != null)
        {
            foreach (object msg in messages)
            {
                JsonElement msgDict = JsonSerializer.SerializeToElement(msg);
                inputMessages.Add(msgDict);
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
        object[]? messages = JsonSerializer.Deserialize<object[]>(messagesElement);
        List<object> anthropicMessages = new();
        string? systemMessage = null;

        if (messages != null)
        {
            foreach (object msg in messages)
            {
                JsonElement elem = JsonSerializer.SerializeToElement(msg);
                if (elem.TryGetProperty("role", out JsonElement roleElem) && elem.TryGetProperty("content", out JsonElement contentElem))
                {
                    string role = roleElem.GetString() ?? "";
                    if (role == "system")
                    {
                        systemMessage = contentElem.GetString();
                    }
                    else
                    {
                        anthropicMessages.Add(new { role = role, content = contentElem.GetString() ?? "" });
                    }
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

        object[]? messages = JsonSerializer.Deserialize<object[]>(messagesElement);
        List<object> contents = new();

        if (messages != null)
        {
            foreach (object msg in messages)
            {
                JsonElement elem = JsonSerializer.SerializeToElement(msg);
                if (elem.TryGetProperty("role", out JsonElement roleElem) && elem.TryGetProperty("content", out JsonElement contentElem))
                {
                    string role = roleElem.GetString() ?? "";
                    string content = contentElem.GetString() ?? "";
                    string geminiRole = (role == "assistant") ? "model" : "user";
                    contents.Add(new { role = geminiRole, parts = new[] { new { text = content } } });
                }
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
