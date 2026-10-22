using System.IO;
using System.Text.Json;
using Textream.Windows.Common;
using Textream.Windows.Models;

namespace Textream.Windows.Services;

public class SettingsService : ISettingsService
{
    private static readonly Logger Log = new(nameof(SettingsService));
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public NotchSettings Current { get; private set; }

    public SettingsService(string? customFilePath = null)
    {
        if (!string.IsNullOrEmpty(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "Textream");
            Directory.CreateDirectory(dir);
            _settingsFilePath = Path.Combine(dir, "settings.json");
        }

        Current = Load();
    }

    private NotchSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<NotchSettings>(json, JsonOptions);
                if (settings != null)
                {
                    Sanitize(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load settings from '{_settingsFilePath}', using defaults.", ex);
        }

        var defaultSettings = new NotchSettings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void Save()
    {
        Sanitize(Current);
        SaveSettings(Current);
    }

    public void Reload()
    {
        Current = Load();
    }

    public void ResetToDefaults()
    {
        Current = new NotchSettings();
        Save();
    }

    private void SaveSettings(NotchSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tempFile = _settingsFilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _settingsFilePath, overwrite: true);
            Log.Debug($"Saved settings to '{_settingsFilePath}'");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save settings to '{_settingsFilePath}'", ex);
        }
    }

    private static void Sanitize(NotchSettings s)
    {
        s.NotchWidth = Math.Clamp(s.NotchWidth, NotchSettings.MinWidth, NotchSettings.MaxWidth);
        s.TextAreaHeight = Math.Clamp(s.TextAreaHeight, NotchSettings.MinHeight, NotchSettings.MaxHeight);
        s.GlassOpacity = Math.Clamp(s.GlassOpacity, 0.05, 1.0);
        s.OverlayTransparencyOpacity = Math.Clamp(s.OverlayTransparencyOpacity, 0.1, 1.0);
        s.ScrollSpeed = Math.Max(0.5, s.ScrollSpeed);

        if (s.BrowserServerPort < 1024) s.BrowserServerPort = 7373;
        if (s.DirectorServerPort < 1024) s.DirectorServerPort = 7575;

        var validDelays = new[] { 0, 1, 3, 5 };
        if (!validDelays.Contains(s.AutoNextPageDelay))
        {
            s.AutoNextPageDelay = 3;
        }
    }
}
