namespace BPSR_SharpCombat.Models;

/// <summary>
/// Application settings with support for different categories
/// </summary>
public class AppSettings
{
    public CombatMeterSettings CombatMeter { get; set; } = new();
    public HotkeysSettings Hotkeys { get; set; } = new();
}

/// <summary>
/// Hotkey settings
/// </summary>
public class HotkeysSettings
{
    /// <summary>
    /// Hotkey to toggle visibility of all app windows
    /// </summary>
    public string ToggleWindows { get; set; } = "";
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
    /// <summary>
    /// Maximum number of completed encounters to keep in the in-memory history.
    /// 0 = keep none, typical default is 10. Clamped to 0..60.
    /// </summary>
    public int MaxEncounterHistory { get; set; } = 10;
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

    // Per-class color overrides. Key is class id (as used elsewhere in the app).
    // If a class id is not present here, the default color mapping will be used.
    public System.Collections.Generic.Dictionary<int, string> ClassColors { get; set; } = new();
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

    /// <summary>
    /// When true (only applicable for the meter font), use the per-class colors for meter text instead of the configured Color.
    /// Default is false so meter text uses the configured color unless the user explicitly opts into class colors.
    /// </summary>
    public bool UseClassColors { get; set; } = false;
}

/// <summary>
/// Meter appearance settings
/// </summary>
public class MeterSettings
{
    // Height of each meter bar in pixels. This controls the `Height` parameter of `BarView` components.
    // Keep a sensible default that matches the BarView default (28).
    public int BarHeight { get; set; } = 28;

    // When true, meter bars use per-class colors (from Appearance.ClassColors). When false, use BarColor for all bars.
    public bool UseClassBarColors { get; set; } = true;

    // Fallback/global bar fill color when UseClassColors is false.
    public string BarColor { get; set; } = "#ff6b6b";

    // Opacity for the meter fill (0.0 - 1.0). Applies regardless of whether class colors or global bar color are used.
    public double BarOpacity { get; set; } = 0.6;

    // The color used for the unfilled track area of bars (hex).
    public string TrackColor { get; set; } = "#000000";

    // Opacity for the track color (0.0 - 1.0)
    public double TrackOpacity { get; set; } = 1.0;
}
