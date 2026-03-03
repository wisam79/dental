using DentalID.Application.Interfaces;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DentalID.Desktop.Services;

/// <summary>
/// Persists user preferences (theme, language) to a JSON file.
/// </summary>
public class AppSettings : ISettingsService
{
    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "data", "settings.json");

    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Task.Run(() =>
        {
            try 
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(SettingsPath, json);
            }
            catch (IOException)
            {
                // Ignore concurrent write errors in tests/runtime if minimal impact
            }
        });
    }

    public static AppSettings Load() 
    {
        var settings = new AppSettings();
        settings.LoadInstance();
        return settings;
    }

    public void LoadInstance()
    {
        if (!File.Exists(SettingsPath)) return;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded != null)
            {
                Theme = loaded.Theme;
                Language = loaded.Language;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            throw new Exception($"Failed to load settings from {SettingsPath}: {ex.Message}", ex);
        }
    }

    // Explicit interface implementation if needed, or just public method
    void ISettingsService.Load() => LoadInstance();
}


