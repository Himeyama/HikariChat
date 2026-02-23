using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CApp.Server;

public static class ApiSettingsManager
{
    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CApp",
        "settings.json"
    );

    public static async Task SaveAsync(ApiSettings settings)
    {
        try
        {
            DebugLogger.Settings("SaveAsync called");
            DebugLogger.Settings($"Settings path: {SettingsPath}");

            string? directory = Path.GetDirectoryName(SettingsPath);
            DebugLogger.Settings($"Directory: {directory}");
            
            if (directory != null && !Directory.Exists(directory))
            {
                DebugLogger.Settings($"Creating directory: {directory}");
                Directory.CreateDirectory(directory);
            }

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            string json = JsonSerializer.Serialize(settings, options);
            DebugLogger.Settings($"Saving JSON: {json}");
            
            await File.WriteAllTextAsync(SettingsPath, json);
            DebugLogger.Settings("Save completed");
        }
        catch (Exception ex)
        {
            DebugLogger.Error($"Failed to save settings: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    public static async Task<ApiSettings> LoadAsync()
    {
        try
        {
            DebugLogger.Settings($"Loading from: {SettingsPath}");
            DebugLogger.Settings($"File exists: {File.Exists(SettingsPath)}");
            
            if (File.Exists(SettingsPath))
            {
                string json = await File.ReadAllTextAsync(SettingsPath);
                DebugLogger.Settings($"JSON: {json}");

                ApiSettings? settings = JsonSerializer.Deserialize<ApiSettings>(json);
                return settings ?? new ApiSettings();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error($"Failed to load settings: {ex.Message}", ex);
        }

        DebugLogger.Settings("Using default settings");
        return new ApiSettings();
    }

    /// <summary>
    /// 設定を削除
    /// </summary>
    public static void Delete()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete settings: {ex.Message}");
        }
    }
}
