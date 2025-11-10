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
            NormalizeSettings(_settings);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Ensures settings have safe default values for any missing or invalid fields.
    /// Called after load and before saving so UI/JS can rely on non-null, sane values.
    /// </summary>
    private void NormalizeSettings(AppSettings settings)
    {
        if (settings == null) return;

        // Background defaults
        if (settings.CombatMeter?.Appearance?.Background == null)
            settings.CombatMeter.Appearance.Background = new BackgroundSettings();

        var bg = settings.CombatMeter.Appearance.Background;
        if (string.IsNullOrEmpty(bg.AppRootColor)) bg.AppRootColor = "#1a1a1a";
        if (bg.AppRootOpacity < 0 || bg.AppRootOpacity > 1) bg.AppRootOpacity = 1.0;
        if (string.IsNullOrEmpty(bg.TitlebarColor)) bg.TitlebarColor = "#2b2b2b";
        if (bg.TitlebarOpacity < 0 || bg.TitlebarOpacity > 1) bg.TitlebarOpacity = 1.0;
        if (string.IsNullOrEmpty(bg.FooterColor)) bg.FooterColor = "#000000";
        if (bg.FooterOpacity < 0 || bg.FooterOpacity > 1) bg.FooterOpacity = 0.35;

        // Fonts defaults and clamping
        if (settings.CombatMeter.Appearance.Fonts == null)
            settings.CombatMeter.Appearance.Fonts = new FontSettings();

        void SanitizeFont(FontSpec f)
        {
            if (f == null) return;
            if (string.IsNullOrEmpty(f.Family)) f.Family = "system-ui";
            if (string.IsNullOrEmpty(f.Color)) f.Color = "#ffffff";
            // clamp size to a reasonable range
            if (f.Size <= 0) f.Size = 13;
            f.Size = Math.Clamp(f.Size, 8, 72);
            // Bold/Italic default false handled by model defaults
        }

        SanitizeFont(settings.CombatMeter.Appearance.Fonts.TitleFont);
        SanitizeFont(settings.CombatMeter.Appearance.Fonts.FooterFont);
        SanitizeFont(settings.CombatMeter.Appearance.Fonts.MeterFont);
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
    /// Updates the app root background color
    /// </summary>
    public void UpdateAppRootBackgroundColor(string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.AppRootColor = color;
            _logger.LogInformation("App root background color changed to: {Color}", color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the app root background opacity
    /// </summary>
    public void UpdateAppRootBackgroundOpacity(double opacity)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.AppRootOpacity = opacity;
            _logger.LogInformation("App root background opacity changed to: {Opacity}", opacity);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the titlebar background color
    /// </summary>
    public void UpdateTitlebarBackgroundColor(string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.TitlebarColor = color;
            _logger.LogInformation("Titlebar background color changed to: {Color}", color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the titlebar background opacity
    /// </summary>
    public void UpdateTitlebarBackgroundOpacity(double opacity)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.TitlebarOpacity = opacity;
            _logger.LogInformation("Titlebar background opacity changed to: {Opacity}", opacity);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the footer background color
    /// </summary>
    public void UpdateFooterBackgroundColor(string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.FooterColor = color;
            _logger.LogInformation("Footer background color changed to: {Color}", color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the footer background opacity
    /// </summary>
    public void UpdateFooterBackgroundOpacity(double opacity)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Background.FooterOpacity = opacity;
            _logger.LogInformation("Footer background opacity changed to: {Opacity}", opacity);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates Title font spec
    /// </summary>
    public void UpdateTitleFont(FontSpec spec)
    {
        if (spec == null) return;
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Fonts.TitleFont = spec;
            _logger.LogInformation("Title font updated: {Family}", spec.Family);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates Footer font spec
    /// </summary>
    public void UpdateFooterFont(FontSpec spec)
    {
        if (spec == null) return;
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Fonts.FooterFont = spec;
            _logger.LogInformation("Footer font updated: {Family}", spec.Family);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates Meter font spec
    /// </summary>
    public void UpdateMeterFont(FontSpec spec)
    {
        if (spec == null) return;
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Fonts.MeterFont = spec;
            _logger.LogInformation("Meter font updated: {Family}", spec.Family);
            SaveSettings();
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
