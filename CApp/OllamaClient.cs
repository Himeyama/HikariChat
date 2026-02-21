using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CApp;

/// <summary>
/// Ollama API クライアント
/// </summary>
public class OllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public OllamaClient(string baseUrl = "http://localhost:11434")
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// Ollama が利用可能かチェック
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// モデル一覧を取得
    /// </summary>
    public async Task<List<string>> GetModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                OllamaTagsResponse? result = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
                return result?.Models?.Where(m => m.Name != null).Select(m => m.Name!).ToList() ?? [];
            }
        }
        catch
        {
            // エラー時は空リストを返す
        }
        return [];
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Ollama API /api/tags のレスポンス
/// </summary>
public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModel>? Models { get; set; }
}

/// <summary>
/// Ollama モデル情報
/// </summary>
public class OllamaModel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTime? ModifiedAt { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
