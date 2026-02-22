using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CApp;

/// <summary>
/// Ollama API 繧ｯ繝ｩ繧､繧｢繝ｳ繝・
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
    /// Ollama 縺悟茜逕ｨ蜿ｯ閭ｽ縺九メ繧ｧ繝・け
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
    /// 繝｢繝・Ν荳隕ｧ繧貞叙蠕・
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
            // 繧ｨ繝ｩ繝ｼ譎ゅ・遨ｺ繝ｪ繧ｹ繝医ｒ霑斐☆
        }
        return [];
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Ollama API /api/tags 縺ｮ繝ｬ繧ｹ繝昴Φ繧ｹ
/// </summary>
public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModel>? Models { get; set; }
}

/// <summary>
/// Ollama 繝｢繝・Ν諠・ｱ
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
