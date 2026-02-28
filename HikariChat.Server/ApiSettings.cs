using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HikariChat.Server;

public class McpServerConfig : ObservableObject
{
    private string _command = "";

    [JsonPropertyName("command")]
    public string Command
    {
        get => _command;
        set => SetProperty(ref _command, value);
    }

    private List<string> _args = [];

    [JsonPropertyName("args")]
    public List<string> Args
    {
        get => _args;
        set => SetProperty(ref _args, value);
    }

    private Dictionary<string, string>? _env;

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env
    {
        get => _env;
        set => SetProperty(ref _env, value);
    }
}

public class ApiSettings : ObservableObject
{
    private string _apiType = "chat_completions";

    [JsonPropertyName("apiType")]
    public string ApiType
    {
        get => _apiType;
        set => SetProperty(ref _apiType, value);
    }

    private string _endpointPreset = "openai";

    [JsonPropertyName("endpointPreset")]
    public string EndpointPreset
    {
        get => _endpointPreset;
        set => SetProperty(ref _endpointPreset, value);
    }

    private string _apiEndpoint = "";

    [JsonPropertyName("apiEndpoint")]
    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set => SetProperty(ref _apiEndpoint, value);
    }

    private string _openaiApiKey = "";

    [JsonPropertyName("openaiApiKey")]
    public string OpenaiApiKey
    {
        get => _openaiApiKey;
        set => SetProperty(ref _openaiApiKey, value);
    }

    private string _anthropicApiKey = "";

    [JsonPropertyName("anthropicApiKey")]
    public string AnthropicApiKey
    {
        get => _anthropicApiKey;
        set => SetProperty(ref _anthropicApiKey, value);
    }

    private string _googleApiKey = "";

    [JsonPropertyName("googleApiKey")]
    public string GoogleApiKey
    {
        get => _googleApiKey;
        set => SetProperty(ref _googleApiKey, value);
    }

    private string _grokApiKey = "";

    [JsonPropertyName("grokApiKey")]
    public string GrokApiKey
    {
        get => _grokApiKey;
        set => SetProperty(ref _grokApiKey, value);
    }

    private string _deepseekApiKey = "";

    [JsonPropertyName("deepseekApiKey")]
    public string DeepseekApiKey
    {
        get => _deepseekApiKey;
        set => SetProperty(ref _deepseekApiKey, value);
    }

    private string _openrouterApiKey = "";

    [JsonPropertyName("openrouterApiKey")]
    public string OpenrouterApiKey
    {
        get => _openrouterApiKey;
        set => SetProperty(ref _openrouterApiKey, value);
    }

    private string _huggingfaceApiKey = "";

    [JsonPropertyName("huggingfaceApiKey")]
    public string HuggingfaceApiKey
    {
        get => _huggingfaceApiKey;
        set => SetProperty(ref _huggingfaceApiKey, value);
    }

    private string _customApiKey = "";

    [JsonPropertyName("customApiKey")]
    public string CustomApiKey
    {
        get => _customApiKey;
        set => SetProperty(ref _customApiKey, value);
    }

    private string _model = "";

    [JsonPropertyName("model")]
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    private string _azureDeployment = "";

    [JsonPropertyName("azureDeployment")]
    public string AzureDeployment
    {
        get => _azureDeployment;
        set => SetProperty(ref _azureDeployment, value);
    }

    private bool _streaming = true;

    [JsonPropertyName("streaming")]
    public bool Streaming
    {
        get => _streaming;
        set => SetProperty(ref _streaming, value);
    }

    private bool _mcpEnabled = false;

    [JsonPropertyName("mcpEnabled")]
    public bool McpEnabled
    {
        get => _mcpEnabled;
        set => SetProperty(ref _mcpEnabled, value);
    }

    private Dictionary<string, McpServerConfig> _mcpServers = [];

    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers
    {
        get => _mcpServers;
        set => SetProperty(ref _mcpServers, value);
    }
}
