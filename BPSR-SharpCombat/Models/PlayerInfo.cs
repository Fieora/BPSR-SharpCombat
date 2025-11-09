namespace BPSR_SharpCombat.Models;

/// <summary>
/// Consolidated player information for caching and UI display.
/// UID should be the shifted UID (uuid >> 16) used throughout the codebase.
/// </summary>
public class PlayerInfo
{
    public long Uid { get; set; }
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public int? SpecId { get; set; }
    public int? AbilityScore { get; set; }

    public PlayerInfo() { }

    public PlayerInfo(long uid, string? name, int? classId = null, int? specId = null, int? abilityScore = null)
    {
        Uid = uid;
        Name = name;
        ClassId = classId;
        SpecId = specId;
        AbilityScore = abilityScore;
    }
}
