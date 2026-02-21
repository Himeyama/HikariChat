using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CApp;

/// <summary>
/// MCP サーバー設定
/// </summary>
public class McpServerSettings
{
    /// <summary>
    /// コマンド (stdio タイプ用)
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// 引数 (stdio タイプ用)
    /// </summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    /// <summary>
    /// URL (sse, websocket タイプ用)
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// 接続タイプ (stdio, sse, websocket)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stdio";

    /// <summary>
    /// 環境変数
    /// </summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>
    /// タイムアウト (秒)
    /// </summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;
}

/// <summary>
/// MCP 設定
/// </summary>
public class McpSettings
{
    /// <summary>
    /// MCP を有効にする
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// MCP サーバー設定（サーバー名をキーとする辞書）
    /// </summary>
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerSettings> McpServers { get; set; } = new();
}
