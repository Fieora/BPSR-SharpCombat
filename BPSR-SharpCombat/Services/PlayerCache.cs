using System.Collections.Concurrent;
using BPSR_SharpCombat.Models;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Thread-safe cache mapping player uid (shifted) to PlayerInfo.
/// </summary>
public class PlayerCache
{
    private readonly ConcurrentDictionary<long, PlayerInfo> _cache = new();

    public void Set(PlayerInfo info)
    {
        _cache[info.Uid] = info;
    }

    private static bool IsValidPlayerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (trimmed.Equals("Unknown", StringComparison.Ordinal)) return false;
        if (trimmed.Equals("Unknown Name", StringComparison.Ordinal)) return false;
        if (trimmed.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }

    /// <summary>
    /// Merge partial player info into existing cache entry (or create new).
    /// Null fields are treated as "not provided" and do not overwrite existing values.
    /// Name is treated conservatively: do not overwrite an existing valid name with an invalid one.
    /// </summary>
    public void Merge(long uid, string? name = null, int? classId = null, int? specId = null, int? abilityScore = null)
    {
        _cache.AddOrUpdate(uid, k =>
        {
            // creation path: only accept name if it's valid
            var acceptedName = IsValidPlayerName(name) ? name : null;
            return new PlayerInfo(uid, acceptedName, classId, specId, abilityScore);
        }, (k, existing) =>
        {
            // Name: only set when incoming name is valid and either we don't have an existing valid name
            if (IsValidPlayerName(name))
            {
                if (!IsValidPlayerName(existing.Name) || string.IsNullOrEmpty(existing.Name))
                {
                    existing.Name = name;
                }
                else if (existing.Name != name)
                {
                    // keep the existing valid name; do not overwrite with a different one
                    // (Rust logs when names differ; our higher-level code already logs caching events)
                }
            }

            // Only update if new value is provided (not null) - don't overwrite existing data
            if (classId.HasValue && classId.Value > 0)
            {
                existing.ClassId = classId;
            }
            
            if (specId.HasValue && specId.Value > 0)
            {
                existing.SpecId = specId;
            }
            
            if (abilityScore.HasValue && abilityScore.Value > 0)
            {
                existing.AbilityScore = abilityScore;
            }
            
            return existing;
        });
    }

    public bool TryGet(long uid, out PlayerInfo? info)
    {
        return _cache.TryGetValue(uid, out info);
    }

    public IReadOnlyDictionary<long, PlayerInfo> Snapshot() => _cache;
}
