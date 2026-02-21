using System.Text.Json.Serialization;

namespace CApp;

public class ApiSettings
{
    [JsonPropertyName("apiType")]
    public string ApiType { get; set; } = "chat_completions";

    [JsonPropertyName("endpointPreset")]
    public string EndpointPreset { get; set; } = "openai";

    [JsonPropertyName("apiEndpoint")]
    public string ApiEndpoint { get; set; } = "";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("azureDeployment")]
    public string AzureDeployment { get; set; } = "";

    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; } = true;
}
