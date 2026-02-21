using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CApp;

public class McpServerConfig
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}

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

    [JsonPropertyName("mcpEnabled")]
    public bool McpEnabled { get; set; } = false;

    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers { get; set; } = new();
}
