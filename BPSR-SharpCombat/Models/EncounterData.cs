using System.Collections.Concurrent;

namespace BPSR_SharpCombat.Models;

/// <summary>
/// Entity type mapping from Blue's protobuf UUID low bits.
/// Values chosen to match blueprotobuf mapping used in Rust.
/// </summary>
public enum EntityType
{
    EntErrType = 0,
    EntMonster = 1,
    EntChar = 2,
}

/// <summary>
/// Minimal entity info stored in an encounter for each UID.
/// This mirrors the Rust project's entity_uid_to_entity mapping so we can
/// distinguish players from monsters when recording damage stats.
/// </summary>
public class EntityInfo
{
    public EntityType EntityType { get; set; } = EntityType.EntErrType;
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public string? ClassSpec { get; set; }
    public int? AbilityScore { get; set; }
}

/// <summary>
/// Represents a single damage or healing event
/// </summary>
public class DamageEvent
{
    public long AttackerUid { get; set; }
    public long TargetUid { get; set; }
    public long Amount { get; set; }
    public EDamageType Type { get; set; }
    public bool IsCrit { get; set; }
    public bool IsMiss { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Represents cumulative damage for a single attacker
/// </summary>
public class AttackerStats
{
    public long Uid { get; set; }
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public string? ClassSpec { get; set; }
    public int? AbilityScore { get; set; }
    public long TotalDamage { get; set; }
    public int DamageCount { get; set; }
    public int CritCount { get; set; }
    public long HealingDone { get; set; }
    // Tracks total healing by skill id for this attacker (populated during event processing)
    public ConcurrentDictionary<int, long> HealingBySkill { get; set; } = new();
    // Use ConcurrentDictionary keys as a thread-safe HashSet
    public ConcurrentDictionary<int, bool> SkillIds { get; set; } = new();
    // Tracks total damage by skill id for this attacker (populated during event processing)
    public ConcurrentDictionary<int, long> DamageBySkill { get; set; } = new();

    public double GetDps(double encountDurationSeconds)
    {
        if (encountDurationSeconds <= 0) return 0;
        return TotalDamage / encountDurationSeconds;
    }

    public double GetHps(double encountDurationSeconds)
    {
        if (encountDurationSeconds <= 0) return 0;
        return HealingDone / encountDurationSeconds;
    }
}

/// <summary>
/// Represents an encounter (combat session)
/// </summary>
public class Encounter
{
    public DateTime StartTime { get; set; }
    public DateTime LastActivityTime { get; set; }
    public bool IsActive { get; set; }
    public ConcurrentDictionary<long, AttackerStats> DamageByAttacker { get; set; } = new();
    public ConcurrentQueue<DamageEvent> AllEvents { get; set; } = new();

    // Map of entity uid -> EntityInfo (type and optional metadata)
    public ConcurrentDictionary<long, EntityInfo> Entities { get; set; } = new();

    public TimeSpan GetDuration()
    {
        return (IsActive ? DateTime.UtcNow : LastActivityTime) - StartTime;
    }

    public long GetTotalDamage()
    {
        return DamageByAttacker.Values.Sum(s => s.TotalDamage);
    }

    public double GetTotalDps()
    {
        var duration = GetDuration().TotalSeconds;
        if (duration <= 0) return 0;
        return GetTotalDamage() / duration;
    }

    public double GetEncounterDurationSeconds()
    {
        return GetDuration().TotalSeconds;
    }
}
