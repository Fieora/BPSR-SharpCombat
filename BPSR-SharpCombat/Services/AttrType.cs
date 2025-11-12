namespace BPSR_SharpCombat.Services;

/// <summary>
/// Attribute type constants matching Rust attr_type module
/// Used when parsing AttrCollection from game packets
/// </summary>
public static class AttrType
{
    /// <summary>
    /// Player name (0x01) - UTF-8 string with first byte skipped
    /// Example: Skip byte 0, then read remaining as UTF-8 string
    /// </summary>
    public const int ATTR_NAME = 0x01;

    /// <summary>
    /// Monster/Entity ID (0x0a) - Varint encoded
    /// </summary>
    public const int ATTR_ID = 0x0a;

    /// <summary>
    /// Profession/Class ID (0xdc) - Varint encoded
    /// Maps to Class enum (1=Stormblade, 2=FrostMage, 4=WindKnight, etc.)
    /// </summary>
    public const int ATTR_PROFESSION_ID = 0xdc;

    /// <summary>
    /// Fight Point / Ability Score (0x272e) - Varint encoded
    /// Represents player's combat power/gear score
    /// </summary>
    public const int ATTR_FIGHT_POINT = 0x272e;

    /// <summary>
    /// Current HP (0x2c2e) - Varint encoded
    /// </summary>
    public const int ATTR_HP = 0x2c2e;

    /// <summary>
    /// Maximum HP (0x2c38) - Varint encoded
    /// </summary>
    public const int ATTR_MAX_HP = 0x2c38;

    // Additional attributes can be added here as discovered
    // Reference: bpsr-logs/src-tauri/src/live/opcodes_models.rs attr_type module
}

/// <summary>
/// Class/Profession enum matching Rust implementation
/// </summary>
public enum PlayerClass
{
    Unknown = 0,
    Stormblade = 1,
    FrostMage = 2,
    WindKnight = 4,
    VerdantOracle = 5,
    HeavyGuardian = 9,
    Marksman = 11,
    ShieldKnight = 12,
    BeatPerformer = 13,
    Unimplemented = 999
}

/// <summary>
/// Extension methods for PlayerClass
/// </summary>
public static class PlayerClassExtensions
{
    public static string GetDisplayName(this PlayerClass playerClass)
    {
        return playerClass switch
        {
            PlayerClass.Stormblade => "Stormblade",
            PlayerClass.FrostMage => "Frost Mage",
            PlayerClass.WindKnight => "Wind Knight",
            PlayerClass.VerdantOracle => "Verdant Oracle",
            PlayerClass.HeavyGuardian => "Heavy Guardian",
            PlayerClass.Marksman => "Marksman",
            PlayerClass.ShieldKnight => "Shield Knight",
            PlayerClass.BeatPerformer => "Beat Performer",
            PlayerClass.Unknown => "Unknown Class",
            PlayerClass.Unimplemented => "Unimplemented Class",
            _ => "Unknown Class"
        };
    }

    public static PlayerClass FromProfessionId(int professionId)
    {
        return professionId switch
        {
            1 => PlayerClass.Stormblade,
            2 => PlayerClass.FrostMage,
            4 => PlayerClass.WindKnight,
            5 => PlayerClass.VerdantOracle,
            9 => PlayerClass.HeavyGuardian,
            11 => PlayerClass.Marksman,
            12 => PlayerClass.ShieldKnight,
            13 => PlayerClass.BeatPerformer,
            _ => PlayerClass.Unimplemented
        };
    }
}

