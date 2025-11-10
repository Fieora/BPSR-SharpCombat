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
}
