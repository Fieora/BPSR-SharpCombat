namespace BPSR_SharpCombat.Models;

/// <summary>
/// Application settings with support for different categories
/// </summary>
public class AppSettings
{
    public CombatMeterSettings CombatMeter { get; set; } = new();
}

/// <summary>
/// Settings specific to the combat meter functionality
/// </summary>
public class CombatMeterSettings
{
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
}

/// <summary>
/// General combat meter settings
/// </summary>
public class GeneralSettings
{
    /// <summary>
    /// Encounter reset timer in seconds. 0 means never auto-reset.
    /// </summary>
    public int EncounterResetTimer { get; set; } = 5;
}

/// <summary>
/// Appearance settings for combat meter
/// </summary>
public class AppearanceSettings
{
    // Placeholder for future appearance settings
    public bool ShowClassIcons { get; set; } = true;
    
    public BackgroundSettings Background { get; set; } = new();
    public FontSettings Fonts { get; set; } = new();
    public MeterSettings Meters { get; set; } = new();
}

/// <summary>
/// Background appearance settings
/// </summary>
public class BackgroundSettings
{
    /// <summary>
    /// App root background color in hex format (e.g., "#1a1a1a")
    /// </summary>
    public string AppRootColor { get; set; } = "#1a1a1a";
    
    /// <summary>
    /// App root background opacity (0.0 to 1.0)
    /// </summary>
    public double AppRootOpacity { get; set; } = 1.0;

    /// <summary>
    /// Titlebar background color in hex format
    /// </summary>
    public string TitlebarColor { get; set; } = "#2b2b2b";

    /// <summary>
    /// Titlebar background opacity (0.0 to 1.0)
    /// </summary>
    public double TitlebarOpacity { get; set; } = 1.0;

    /// <summary>
    /// Footer background color in hex format
    /// </summary>
    public string FooterColor { get; set; } = "#000000";

    /// <summary>
    /// Footer background opacity (0.0 to 1.0)
    /// </summary>
    public double FooterOpacity { get; set; } = 0.35;
}

/// <summary>
/// Font appearance settings
/// </summary>
public class FontSettings
{
    public FontSpec TitleFont { get; set; } = new FontSpec();
    public FontSpec FooterFont { get; set; } = new FontSpec();
    public FontSpec MeterFont { get; set; } = new FontSpec();
}

public class FontSpec
{
    /// <summary>
    /// CSS font-family string (single family or comma-separated fallbacks)
    /// </summary>
    public string Family { get; set; } = "system-ui";

    /// <summary>
    /// Color as hex like #ffffff
    /// </summary>
    public string Color { get; set; } = "#ffffff";

    /// <summary>
    /// Bold toggle
    /// </summary>
    public bool Bold { get; set; } = false;

    /// <summary>
    /// Italic toggle
    /// </summary>
    public bool Italic { get; set; } = false;

    /// <summary>
    /// Font size in pixels
    /// </summary>
    public int Size { get; set; } = 13;
}

/// <summary>
/// Meter appearance settings
/// </summary>
public class MeterSettings
{
    // Placeholder for future meter settings
}
