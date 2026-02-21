using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CApp;

/// <summary>
/// デバッグログ管理
/// </summary>
public static class DebugLogger
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "mcp_debug.log"
    );

    private static readonly object LockObj = new();

    /// <summary>
    /// ログを出力
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
    /// 情報ログ
    /// </summary>
    public static void Info(string message)
    {
        Write($"[INFO] {message}");
    }

    /// <summary>
    /// エラーログ
    /// </summary>
    public static void Error(string message)
    {
        Write($"[ERROR] {message}");
    }

    /// <summary>
    /// エラーログ（スタックトレース付き）
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        Write($"[ERROR] {message}");
        Write($"[ERROR] StackTrace: {ex.StackTrace}");
    }

    /// <summary>
    /// MCP ログ
    /// </summary>
    public static void Mcp(string message)
    {
        Write($"[MCP] {message}");
    }

    /// <summary>
    /// 設定ログ
    /// </summary>
    public static void Settings(string message)
    {
        Write($"[SETTINGS] {message}");
    }

    /// <summary>
    /// API ログ
    /// </summary>
    public static void Api(string message)
    {
        Write($"[API] {message}");
    }

    /// <summary>
    /// ログファイルをクリア
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
    /// ログファイルのパスを取得
    /// </summary>
    public static string GetLogFilePath()
    {
        return LogFilePath;
    }
}
