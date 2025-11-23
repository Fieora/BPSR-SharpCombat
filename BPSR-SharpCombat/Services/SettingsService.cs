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

        // Ensure top-level sections exist to avoid null refs
        if (settings.CombatMeter == null) settings.CombatMeter = new CombatMeterSettings();
        if (settings.CombatMeter.Appearance == null) settings.CombatMeter.Appearance = new AppearanceSettings();
        if (settings.Hotkeys == null) settings.Hotkeys = new HotkeysSettings();
        

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

        // Meter defaults and clamping
        if (settings.CombatMeter.Appearance.Meters == null)
            settings.CombatMeter.Appearance.Meters = new MeterSettings();
        // BarHeight should be within a sensible pixel range
        if (settings.CombatMeter.Appearance.Meters.BarHeight <= 0) settings.CombatMeter.Appearance.Meters.BarHeight = 28;
        settings.CombatMeter.Appearance.Meters.BarHeight = Math.Clamp(settings.CombatMeter.Appearance.Meters.BarHeight, 8, 72);
        // Meter bar color defaults and use-class-colors flag
        if (string.IsNullOrEmpty(settings.CombatMeter.Appearance.Meters.BarColor))
            settings.CombatMeter.Appearance.Meters.BarColor = "#ff6b6b";
        if (string.IsNullOrEmpty(settings.CombatMeter.Appearance.Meters.TrackColor))
            settings.CombatMeter.Appearance.Meters.TrackColor = "#000000";
        if (settings.CombatMeter.Appearance.Meters.TrackOpacity < 0 || settings.CombatMeter.Appearance.Meters.TrackOpacity > 1)
            settings.CombatMeter.Appearance.Meters.TrackOpacity = 1.0;
        // Ensure BarOpacity is within (0.0 - 1.0)
        if (settings.CombatMeter.Appearance.Meters.BarOpacity < 0 || settings.CombatMeter.Appearance.Meters.BarOpacity > 1)
            settings.CombatMeter.Appearance.Meters.BarOpacity = 0.6;
        // UseClassColors default true handled by model default

        // Ensure ClassColors dictionary exists and populate sensible defaults if missing
        if (settings.CombatMeter.Appearance.ClassColors == null)
            settings.CombatMeter.Appearance.ClassColors = new System.Collections.Generic.Dictionary<int, string>();

        // Provide default colors for known classes if not already set
        var defaults = GetDefaultClassColors();
        foreach (var kv in defaults)
        {
            if (!settings.CombatMeter.Appearance.ClassColors.ContainsKey(kv.Key))
            {
                settings.CombatMeter.Appearance.ClassColors[kv.Key] = kv.Value;
            }
        }

        // Ensure General.MaxEncounterHistory exists and is in a sane range (0..60)
        if (settings.CombatMeter.General == null)
            settings.CombatMeter.General = new GeneralSettings();
        settings.CombatMeter.General.MaxEncounterHistory = Math.Clamp(settings.CombatMeter.General.MaxEncounterHistory, 0, 60);
    }

    private static System.Collections.Generic.Dictionary<int, string> GetDefaultClassColors()
    {
        return new System.Collections.Generic.Dictionary<int, string>
        {
            {1, "#674598"}, // Stormblade
            {2, "#4de3d1"}, // Frost Mage
            {4, "#0099c6"}, // Wind Knight
            {5, "#66aa00"}, // Verdant Oracle
            {9, "#b38915"}, // Heavy Guardian
            {11, "#ffee00"}, // Marksman
            {12, "#7b9aa2"}, // Shield Knight
            {13, "#ee2e48"}, // Beat Performer
        };
    }

    /// <summary>
    /// Resets all per-class color overrides to the built-in defaults and persists.
    /// </summary>
    public void ResetClassColorsToDefaults()
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.ClassColors = new System.Collections.Generic.Dictionary<int, string>(GetDefaultClassColors());
            _logger.LogInformation("Class colors reset to defaults");
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
    /// Updates the maximum number of stored encounters in memory (0..60)
    /// </summary>
    public void UpdateMaxEncounterHistory(int max)
    {
        lock (_lock)
        {
            var clamped = Math.Clamp(max, 0, 60);
            var old = _settings.CombatMeter.General.MaxEncounterHistory;
            _settings.CombatMeter.General.MaxEncounterHistory = clamped;
            _logger.LogInformation("Max encounter history changed: {Old} -> {New}", old, clamped);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the meter bar height (pixels)
    /// </summary>
    public void UpdateMeterBarHeight(int height)
    {
        lock (_lock)
        {
            var oldValue = _settings.CombatMeter.Appearance.Meters.BarHeight;
            _settings.CombatMeter.Appearance.Meters.BarHeight = Math.Clamp(height, 8, 72);
            _logger.LogInformation("Meter bar height changed: {OldValue}px -> {NewValue}px", oldValue, _settings.CombatMeter.Appearance.Meters.BarHeight);
            SaveSettings();
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
    /// Updates a per-class color override
    /// </summary>
    public void UpdateClassColor(int classId, string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.ClassColors[classId] = color;
            _logger.LogInformation("Class {ClassId} color changed to: {Color}", classId, color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the meter bar fallback color (used when UseClassColors is false)
    /// </summary>
    public void UpdateMeterBarColor(string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Meters.BarColor = color;
            _logger.LogInformation("Meter bar color changed to: {Color}", color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates whether meter bars use class colors for fill
    /// </summary>
    public void UpdateMeterUseClassColors(bool useClassColors)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Meters.UseClassBarColors = useClassColors;
            _logger.LogInformation("Meter UseClassBarColors changed to: {Value}", useClassColors);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the meter bar track color (the unfilled area) and persists
    /// </summary>
    public void UpdateMeterTrackColor(string color)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Meters.TrackColor = color;
            _logger.LogInformation("Meter track color changed to: {Color}", color);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the meter bar track opacity (0.0-1.0)
    /// </summary>
    public void UpdateMeterTrackOpacity(double opacity)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Meters.TrackOpacity = Math.Clamp(opacity, 0.0, 1.0);
            _logger.LogInformation("Meter track opacity changed to: {Opacity}", _settings.CombatMeter.Appearance.Meters.TrackOpacity);
            SaveSettings();
            SettingsChanged?.Invoke(this, _settings);
        }
    }

    /// <summary>
    /// Updates the meter bar fill opacity (0.0-1.0)
    /// </summary>
    public void UpdateMeterBarOpacity(double opacity)
    {
        lock (_lock)
        {
            _settings.CombatMeter.Appearance.Meters.BarOpacity = Math.Clamp(opacity, 0.0, 1.0);
            _logger.LogInformation("Meter bar opacity changed to: {Opacity}", _settings.CombatMeter.Appearance.Meters.BarOpacity);
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
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                if (settings != null)
                {
                    // Migration: support legacy JSON where both font and meter used the same "UseClassColors" key.
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        bool? metersLegacy = null;
                        bool? fontLegacy = null;

                        if (root.TryGetProperty("CombatMeter", out var cm) && cm.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (cm.TryGetProperty("Appearance", out var app) && app.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                // If someone placed a UseClassColors directly under Appearance (legacy/ambiguous), treat it as meters setting.
                                if (app.TryGetProperty("UseClassColors", out var appUse) && (appUse.ValueKind == System.Text.Json.JsonValueKind.True || appUse.ValueKind == System.Text.Json.JsonValueKind.False))
                                {
                                    metersLegacy = appUse.GetBoolean();
                                }

                                // Meters.UseClassColors -> map to MeterSettings.UseClassBarColors (legacy explicit meters value)
                                if (app.TryGetProperty("Meters", out var metersEl) && metersEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    if (metersEl.TryGetProperty("UseClassColors", out var useClassMeters) && (useClassMeters.ValueKind == System.Text.Json.JsonValueKind.True || useClassMeters.ValueKind == System.Text.Json.JsonValueKind.False))
                                    {
                                        metersLegacy = useClassMeters.GetBoolean();
                                    }
                                }

                                // Fonts.MeterFont.UseClassColors -> detect explicit font setting (do NOT inherit meters value)
                                if (app.TryGetProperty("Fonts", out var fontsEl) && fontsEl.ValueKind == System.Text.Json.JsonValueKind.Object && fontsEl.TryGetProperty("MeterFont", out var meterFontEl) && meterFontEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    if (meterFontEl.TryGetProperty("UseClassColors", out var useClassFont) && (useClassFont.ValueKind == System.Text.Json.JsonValueKind.True || useClassFont.ValueKind == System.Text.Json.JsonValueKind.False))
                                    {
                                        fontLegacy = useClassFont.GetBoolean();
                                    }
                                }
                            }
                        }

                        // Apply detected legacy values independently. If not present, leave defaults from the model (meters true, font false).
                        if (metersLegacy.HasValue)
                        {
                            settings.CombatMeter.Appearance.Meters.UseClassBarColors = metersLegacy.Value;
                        }

                        if (fontLegacy.HasValue)
                        {
                            settings.CombatMeter.Appearance.Fonts.MeterFont.UseClassColors = fontLegacy.Value;
                        }
                    }
                    catch
                    {
                        // ignore migration errors and continue with deserialized settings
                    }

                    // Ensure all defaults are present (including class colors)
                    NormalizeSettings(settings);
                    // Persist normalized settings so future loads are consistent
                    _settings = settings;
                    SaveSettings();

                    _logger.LogInformation("Settings loaded from {Path} - EncounterResetTimer: {Timer}s, Hotkey: '{Hotkey}'", 
                        _settingsFilePath, settings.CombatMeter.General.EncounterResetTimer, settings.Hotkeys?.ToggleWindows);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}", _settingsFilePath);
        }

        _logger.LogInformation("Creating default settings - EncounterResetTimer: 5s");
        var defaults = new AppSettings();
        NormalizeSettings(defaults);
        _settings = defaults;
        SaveSettings();
        return defaults;
    }

    /// <summary>
    /// Saves settings to disk
    /// </summary>
    public void SaveSettings(AppSettings settings = null)
    {
        if (settings != null)
        {
            lock (_lock)
            {
                _settings = settings;
                NormalizeSettings(_settings);
            }
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_settings, options);
            }
            File.WriteAllText(_settingsFilePath, json);
            _logger.LogInformation("Settings saved to {Path}", _settingsFilePath);
            
            if (settings != null)
            {
                SettingsChanged?.Invoke(this, _settings);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _settingsFilePath);
        }
    }
}
