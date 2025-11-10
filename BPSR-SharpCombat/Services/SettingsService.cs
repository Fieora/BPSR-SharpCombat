using System.Text.Json;
using BPSR_SharpCombat.Models;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Service for managing application settings with persistence and real-time updates
/// </summary>
public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService> _logger;
    private AppSettings _settings;
    private readonly object _lock = new();

    /// <summary>
    /// Event raised when settings are changed
    /// </summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        
        // Store settings in the application data folder
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "BPSR-SharpCombat");
        Directory.CreateDirectory(appFolder);
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
        
        _settings = LoadSettings();
    }

    /// <summary>
    /// Gets the current settings
    /// </summary>
    public AppSettings GetSettings()
    {
        lock (_lock)
        {
            return _settings;
        }
    }

    /// <summary>
    /// Updates settings and persists to disk
    /// </summary>
    public void UpdateSettings(AppSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates a specific setting and persists to disk
    /// </summary>
    public void UpdateEncounterResetTimer(int seconds)
    {
        lock (_lock)
        {
            var oldValue = _settings.CombatMeter.General.EncounterResetTimer;
            _settings.CombatMeter.General.EncounterResetTimer = seconds;
            _logger.LogInformation("Encounter reset timer changed: {OldValue}s -> {NewValue}s", oldValue, seconds);
            SaveSettings();
            _logger.LogInformation("Invoking SettingsChanged event with {SubscriberCount} subscribers", 
                SettingsChanged?.GetInvocationList().Length ?? 0);
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Loads settings from disk, or creates default settings if file doesn't exist
    /// </summary>
    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    _logger.LogInformation("Settings loaded from {Path} - EncounterResetTimer: {Timer}s", 
                        _settingsFilePath, settings.CombatMeter.General.EncounterResetTimer);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}", _settingsFilePath);
        }

        _logger.LogInformation("Creating default settings - EncounterResetTimer: 5s");
        return new AppSettings();
    }

    /// <summary>
    /// Saves settings to disk
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsFilePath, json);
            _logger.LogInformation("Settings saved to {Path}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsFilePath);
        }
    }
}
