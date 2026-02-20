namespace CApp;

public class ApiSettings
{
    public string ApiType { get; set; } = "chat_completions";
    public string EndpointPreset { get; set; } = "openai";
    public string ApiEndpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string AzureDeployment { get; set; } = "";
    public bool Streaming { get; set; } = true;
}
