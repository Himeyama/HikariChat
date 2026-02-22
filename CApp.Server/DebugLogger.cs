using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CApp;

/// <summary>
/// 繝・ヰ繝・げ繝ｭ繧ｰ邂｡逅・
/// </summary>
public static class DebugLogger
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "mcp_debug.log"
    );

    private static readonly object LockObj = new();

    /// <summary>
    /// 繝ｭ繧ｰ繧貞・蜉・
    /// </summary>
    public static void Write(string message)
    {
        try
        {
            lock (LockObj)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Logger Error] {ex.Message}");
        }
    }

    /// <summary>
    /// 諠・ｱ繝ｭ繧ｰ
    /// </summary>
    public static void Info(string message)
    {
        Write($"[INFO] {message}");
    }

    /// <summary>
    /// 繧ｨ繝ｩ繝ｼ繝ｭ繧ｰ
    /// </summary>
    public static void Error(string message)
    {
        Write($"[ERROR] {message}");
    }

    /// <summary>
    /// 繧ｨ繝ｩ繝ｼ繝ｭ繧ｰ・医せ繧ｿ繝・け繝医Ξ繝ｼ繧ｹ莉倥″・・
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        Write($"[ERROR] {message}");
        Write($"[ERROR] StackTrace: {ex.StackTrace}");
    }

    /// <summary>
    /// MCP 繝ｭ繧ｰ
    /// </summary>
    public static void Mcp(string message)
    {
        Write($"[MCP] {message}");
    }

    /// <summary>
    /// 險ｭ螳壹Ο繧ｰ
    /// </summary>
    public static void Settings(string message)
    {
        Write($"[SETTINGS] {message}");
    }

    /// <summary>
    /// API 繝ｭ繧ｰ
    /// </summary>
    public static void Api(string message)
    {
        Write($"[API] {message}");
    }

    /// <summary>
    /// 繝ｭ繧ｰ繝輔ぃ繧､繝ｫ繧偵け繝ｪ繧｢
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                File.Delete(LogFilePath);
            }
        }
        catch { }
    }

    /// <summary>
    /// 繝ｭ繧ｰ繝輔ぃ繧､繝ｫ縺ｮ繝代せ繧貞叙蠕・
    /// </summary>
    public static string GetLogFilePath()
    {
        return LogFilePath;
    }
}
