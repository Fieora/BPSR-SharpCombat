using BPSR_SharpCombat.Models;
using System.Linq;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Manages the current encounter and tracks damage/healing events
/// Handles encounter start/end logic with configurable idle timeout from settings
/// </summary>
public class EncounterService
{
    private readonly ILogger<EncounterService> _logger;
    private readonly PlayerCache _playerCache;
    private readonly SettingsService _settingsService;
    
    private Encounter? _currentEncounter;
    // In-memory history of completed encounters (most recent first)
    private readonly List<Encounter> _history = new();
    // Currently selected encounter for display. If null => show live/current encounter
    private Encounter? _selectedEncounter;
    private readonly object _encounterLock = new object();
    
    private Timer? _timeoutTimer;

    /// <summary>
    /// Raised when an encounter starts
    /// </summary>
    public event EventHandler<Encounter>? EncounterStarted;

    /// <summary>
    /// Raised when an encounter ends
    /// </summary>
    public event EventHandler<Encounter>? EncounterEnded;

    /// <summary>
    /// Raised when the stored history list changes (new encounter added/cleared)
    /// </summary>
    public event EventHandler? HistoryChanged;

    /// <summary>
    /// Raised when the selected encounter (what the UI should display) changes.
    /// Parameter is the newly selected encounter or null when switching back to live/current.
    /// </summary>
    public event EventHandler<Encounter?>? SelectedEncounterChanged;

    /// <summary>
    /// Raised when damage/healing events are processed
    /// </summary>
    public event EventHandler<Encounter>? EncounterUpdated;

    public EncounterService(ILogger<EncounterService> logger, PlayerCache playerCache, SettingsService settingsService)
    {
        _logger = logger;
        _playerCache = playerCache;
        _settingsService = settingsService;
        
        // Listen for settings changes to update timeout behavior
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _logger.LogInformation("Settings changed - encounter reset timer: {Timer}s", 
            settings.CombatMeter.General.EncounterResetTimer);
        
        // When settings change, reschedule the timeout if an encounter is active
        lock (_encounterLock)
        {
            if (_currentEncounter != null && _currentEncounter.IsActive)
            {
                var timeSinceLastActivity = DateTime.UtcNow - _currentEncounter.LastActivityTime;
                var newTimeout = GetIdleTimeout();
                var remainingTime = newTimeout - timeSinceLastActivity;
                
                if (remainingTime <= TimeSpan.Zero)
                {
                    // Already exceeded the new timeout, end immediately
                    _logger.LogInformation("New timeout already exceeded (elapsed: {Elapsed}s, new timeout: {Timeout}s), ending encounter now", 
                        timeSinceLastActivity.TotalSeconds, newTimeout.TotalSeconds);
                    EndEncounterInternal();
                }
                else
                {
                    // Reschedule with remaining time
                    _logger.LogInformation("Active encounter found, rescheduling timeout (elapsed: {Elapsed}s, remaining: {Remaining}s)", 
                        timeSinceLastActivity.TotalSeconds, remainingTime.TotalSeconds);
                    _timeoutTimer?.Dispose();
                    _timeoutTimer = new Timer(EndEncounterIfIdle, null, remainingTime, Timeout.InfiniteTimeSpan);
                }
            }
            else
            {
                _logger.LogDebug("No active encounter, timeout will apply on next encounter start");
            }
        }

        // When settings change we may need to trim the stored history according to the max configured
        try
        {
            lock (_encounterLock)
            {
                var max = settings.CombatMeter.General.MaxEncounterHistory;
                if (max < 0) max = 0;
                if (_history.Count > max)
                {
                    _logger.LogInformation("Trimming encounter history from {OldCount} to {NewMax}", _history.Count, max);
                    // remove oldest entries beyond the max (keep most-recent-first)
                    while (_history.Count > max)
                    {
                        _history.RemoveAt(_history.Count - 1);
                    }
                    // notify UI
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed trimming encounter history on settings change");
        }
    }
    
    /// <summary>
    /// Gets the current idle timeout from settings
    /// </summary>
    private TimeSpan GetIdleTimeout()
    {
        var seconds = _settingsService.GetSettings().CombatMeter.General.EncounterResetTimer;
        var timeout = seconds == 0 ? TimeSpan.FromDays(365) : TimeSpan.FromSeconds(seconds);
        _logger.LogTrace("GetIdleTimeout: {Seconds}s -> {Timeout}", seconds, timeout);
        return timeout;
    }

    // Helper mapping raw UUID -> EntityType (match Rust rule using low 16 bits)
    private EntityType GetEntityTypeFromUuid(long uuid)
    {
        var low = (int)(uuid & 0xffff);
        return low switch
        {
            64 => EntityType.EntMonster,
            640 => EntityType.EntChar,
            _ => EntityType.EntErrType,
        };
    }

    /// <summary>
    /// Gets the current encounter, or null if no active encounter
    /// </summary>
    public Encounter? GetCurrentEncounter()
    {
        lock (_encounterLock)
        {
            return _currentEncounter;
        }
    }

    /// <summary>
    /// Returns the encounter that should be displayed: the selected historical encounter if set,
    /// otherwise the live current encounter (may be null).
    /// </summary>
    public Encounter? GetDisplayedEncounter()
    {
        lock (_encounterLock)
        {
            return _selectedEncounter ?? _currentEncounter;
        }
    }

    /// <summary>
    /// Returns a snapshot of the in-memory encounter history (most recent first).
    /// </summary>
    public IReadOnlyList<Encounter> GetHistory()
    {
        lock (_encounterLock)
        {
            return _history.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Selects an encounter to be displayed. Pass null to switch back to live/current encounter.
    /// </summary>
    public void SelectEncounter(Encounter? enc)
    {
        lock (_encounterLock)
        {
            // If selecting by reference not present in history, allow null only
            if (enc != null && !_history.Contains(enc))
            {
                // ignore invalid selection
                return;
            }

            _selectedEncounter = enc;
        }
        _logger.LogInformation("SelectedEncounterChanged -> selected={Selected}", enc == null ? "<live>" : enc.StartTime.ToString());
        SelectedEncounterChanged?.Invoke(this, enc);
    }

    /// <summary>
    /// Update or create an EntityInfo entry in the current encounter. This is used by
    /// CombatDataService when parsing attrs so the encounter knows whether a uid is a player.
    /// </summary>
    public void UpdateEntityFromParsedAttrs(long rawUuid, string? name = null, int? classId = null, int? abilityScore = null, string? classSpec = null)
    {
        var uid = rawUuid >> 16;
        var et = GetEntityTypeFromUuid(rawUuid);

        lock (_encounterLock)
        {
            if (_currentEncounter == null) return;
            if (!_currentEncounter.Entities.ContainsKey(uid))
            {
                _currentEncounter.Entities[uid] = new EntityInfo { EntityType = et };
            }

            var ent = _currentEncounter.Entities[uid];
            // Do not overwrite existing name with invalid values
            if (!string.IsNullOrWhiteSpace(name)) ent.Name = name;
            if (classId.HasValue && classId.Value > 0) ent.ClassId = classId;
            if (!string.IsNullOrWhiteSpace(classSpec)) ent.ClassSpec = classSpec;
            if (abilityScore.HasValue && abilityScore.Value > 0) ent.AbilityScore = abilityScore;
        }
    }

    /// <summary>
    /// Processes damage/healing events from skill effects
    /// </summary>
    public void ProcessDamageEvent(long attackerUuid, long targetUuid, SyncDamageInfo damageInfo)
    {
        if (damageInfo.Value == null || damageInfo.Value == 0)
            return;

        // Check damage type early - only process actual damage and healing
        var damageType = (EDamageType)(damageInfo.Type ?? 0);
        var shouldExtendEncounter = damageType == EDamageType.Normal || damageType == EDamageType.Heal;
        
        // Skip non-combat events (Miss, Immune, Fall, Absorbed don't extend encounter timer)
        if (!shouldExtendEncounter)
        {
            _logger.LogTrace("Skipping non-combat event: {Type}", damageType);
            return;
        }

        // Shift UUIDs from raw format to player format
        var attackerUid = attackerUuid >> 16;
        var targetUid = targetUuid >> 16;

        var attackerEntityType = GetEntityTypeFromUuid(attackerUuid);

        lock (_encounterLock)
        {
            // Start encounter if not active
            if (_currentEncounter == null || !_currentEncounter.IsActive)
            {
                _currentEncounter = new Encounter
                {
                    StartTime = DateTime.UtcNow,
                    LastActivityTime = DateTime.UtcNow,
                    IsActive = true
                };
                _logger.LogInformation("Encounter started");
                EncounterStarted?.Invoke(this, _currentEncounter);
            }

            // Update last activity time (only for actual damage/healing)
            _currentEncounter.LastActivityTime = DateTime.UtcNow;

            // Create or update attacker stats only for player entities
            if (attackerEntityType == EntityType.EntChar)
            {
                // Try to get cached player info up-front so we can seed ClassSpec/Name when creating stats
                _playerCache.TryGet(attackerUid, out var playerInfo);

                if (!_currentEncounter.DamageByAttacker.ContainsKey(attackerUid))
                {
                    _currentEncounter.DamageByAttacker[attackerUid] = new AttackerStats
                    {
                        Uid = attackerUid,
                        Name = playerInfo?.Name,
                        ClassId = playerInfo?.ClassId,
                        ClassSpec = playerInfo?.SpecName, // seed spec name from cache if available
                        AbilityScore = playerInfo?.AbilityScore,
                        TotalDamage = 0,
                        DamageCount = 0,
                        CritCount = 0,
                        HealingDone = 0
                    };
                }

                var stats = _currentEncounter.DamageByAttacker[attackerUid];

                // If stats lack a ClassSpec but the player cache has one, populate it so UI picks up colors/specs
                if (string.IsNullOrEmpty(stats.ClassSpec) && playerInfo != null && !string.IsNullOrEmpty(playerInfo.SpecName))
                {
                    stats.ClassSpec = playerInfo.SpecName;
                }

                // Track skill ID for spec detection
                if (damageInfo.OwnerId.HasValue)
                {
                    stats.SkillIds.Add(damageInfo.OwnerId.Value);
                    // Try to detect spec based on skill IDs
                    var detectedSpec = GetClassSpecFromSkillIds(stats.SkillIds);
                    if (detectedSpec != null && string.IsNullOrEmpty(stats.ClassSpec))
                    {
                        stats.ClassSpec = detectedSpec;
                        // Infer class from spec and persist into player cache and encounter entity info
                        var inferredClass = GetClassFromSpec(detectedSpec);
                        if (inferredClass.HasValue)
                        {
                            stats.ClassId = inferredClass.Value;
                            try
                            {
                                // persist class into player cache (uid is shifted attackerUid)
                                _playerCache.Merge(attackerUid, null, inferredClass.Value, null, null, detectedSpec);
                            }
                            catch { }

                            try
                            {
                                // Update entity map so UI bars pick up new class immediately
                                UpdateEntityFromParsedAttrs(attackerUuid, null, inferredClass.Value, null, detectedSpec);
                            }
                            catch { }
                        }
                    }
                }

                // Record the event (damageType already determined at method start)
                
                // Determine if it's a crit - check both IsCrit field and TypeFlag bit
                var isCrit = damageInfo.IsCrit ?? false;
                if (!isCrit && damageInfo.TypeFlag.HasValue)
                {
                    const int critBit = 0b00_00_00_01;
                    isCrit = (damageInfo.TypeFlag.Value & critBit) != 0;
                }
                
                var damageEvent = new DamageEvent
                {
                    AttackerUid = attackerUid,
                    TargetUid = targetUid,
                    Amount = damageInfo.Value.Value,
                    Type = damageType,
                    IsCrit = isCrit,
                    IsMiss = damageInfo.IsMiss ?? false,
                    Timestamp = DateTime.UtcNow
                };
                _currentEncounter.AllEvents.Add(damageEvent);

                // Update stats based on damage type
                if (damageType == EDamageType.Heal)
                {
                    stats.HealingDone += damageInfo.Value.Value;
                }
                else if (damageType != EDamageType.Miss)
                {
                    // Attribute damage to the skill id (if available) for skill breakdown
                    if (damageInfo.OwnerId.HasValue)
                    {
                        var skillId = damageInfo.OwnerId.Value;
                        if (!stats.DamageBySkill.ContainsKey(skillId)) stats.DamageBySkill[skillId] = 0;
                        stats.DamageBySkill[skillId] += damageInfo.Value.Value;
                    }
                    stats.TotalDamage += damageInfo.Value.Value;
                    stats.DamageCount++;
                    if (isCrit)
                    {
                        stats.CritCount++;
                        _logger.LogDebug("Crit recorded for {AttackerUid}: Total crits now = {CritCount}", 
                            attackerUid, stats.CritCount);
                    }
                }

                _logger.LogDebug("Recorded {DamageType} from {AttackerUid}: {Amount}, IsCrit={IsCrit}, DamageCount={DamageCount}, CritCount={CritCount}", 
                    damageType, attackerUid, damageInfo.Value, isCrit, stats.DamageCount, stats.CritCount);
            }
            else
            {
                // For non-player entities (e.g., monsters), just record the event without affecting encounter stats
                var damageEvent = new DamageEvent
                {
                    AttackerUid = attackerUid,
                    TargetUid = targetUid,
                    Amount = damageInfo.Value.Value,
                    Type = damageType,
                    IsCrit = damageInfo.IsCrit ?? false,
                    IsMiss = damageInfo.IsMiss ?? false,
                    Timestamp = DateTime.UtcNow
                };
                _currentEncounter.AllEvents.Add(damageEvent);
            }

            // Reschedule the timeout timer
            RescheduleIdleTimeout();

            // Notify subscribers
            EncounterUpdated?.Invoke(this, _currentEncounter);
        }
    }

    /// <summary>
    /// Reschedules the idle timeout timer
    /// </summary>
    private void RescheduleIdleTimeout()
    {
        _timeoutTimer?.Dispose();
        var timeout = GetIdleTimeout();
        _logger.LogTrace("Rescheduling encounter timeout to {Timeout}", timeout);
        _timeoutTimer = new Timer(EndEncounterIfIdle, null, timeout, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Called when the idle timeout expires - ends encounter if still idle
    /// </summary>
    private void EndEncounterIfIdle(object? state)
    {
        lock (_encounterLock)
        {
            if (_currentEncounter == null || !_currentEncounter.IsActive)
                return;

            var idleTimeout = GetIdleTimeout();
            var timeSinceLastActivity = DateTime.UtcNow - _currentEncounter.LastActivityTime;
            if (timeSinceLastActivity >= idleTimeout)
            {
                EndEncounterInternal();
            }
            else
            {
                // Reschedule if activity happened after timer was set
                RescheduleIdleTimeout();
            }
        }
    }

    /// <summary>
    /// Manually ends the current encounter
    /// </summary>
    public void EndEncounter()
    {
        lock (_encounterLock)
        {
            EndEncounterInternal();
        }
    }

    /// <summary>
    /// Internal method to end the encounter (must be called with lock held)
    /// </summary>
    private void EndEncounterInternal()
    {
        if (_currentEncounter == null || !_currentEncounter.IsActive)
            return;

        // Set LastActivityTime to the timestamp of the last combat event
        // This ensures the duration reflects actual combat time, not the idle timeout period
        var lastEvent = _currentEncounter.AllEvents.OrderByDescending(e => e.Timestamp).FirstOrDefault();
        if (lastEvent != null)
        {
            _currentEncounter.LastActivityTime = lastEvent.Timestamp;
            _logger.LogDebug("Setting encounter end time to last event timestamp: {Timestamp}", lastEvent.Timestamp);
        }

        _currentEncounter.IsActive = false;
        var duration = _currentEncounter.GetDuration();
        var totalDamage = _currentEncounter.GetTotalDamage();
        var totalDps = _currentEncounter.GetTotalDps();

        _logger.LogInformation(
            "Encounter ended: Duration={Duration:F2}s, TotalDamage={TotalDamage}, TotalDPS={TotalDps:F2}",
            duration.TotalSeconds, totalDamage, totalDps);

    var endedEncounter = _currentEncounter;
    // Keep the ended encounter available as the current encounter so the UI can continue
    // showing the last encounter until new combat data arrives. Do still clear the timeout.
    _timeoutTimer?.Dispose();
    _timeoutTimer = null;

        // Store ended encounter in in-memory history (most-recent-first)
        if (endedEncounter != null)
        {
            _logger.LogInformation("Adding encounter to history: start={Start}, events={Events}, totalDamage={Total}", endedEncounter.StartTime, endedEncounter.AllEvents?.Count ?? 0, endedEncounter.GetTotalDamage());
            _history.Insert(0, endedEncounter);

            // Enforce max history size from settings (0..60)
            try
            {
                var max = _settingsService.GetSettings().CombatMeter.General.MaxEncounterHistory;
                if (max < 0) max = 0;
                while (_history.Count > max)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enforce max encounter history when adding ended encounter");
            }

            _logger.LogInformation("Raising HistoryChanged (history now {Count})", _history.Count);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

    EncounterEnded?.Invoke(this, endedEncounter!);
    }

    /// <summary>
    /// Resets and clears the current encounter
    /// </summary>
    public void Reset()
    {
        lock (_encounterLock)
        {
            _currentEncounter = null;
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;
        }
    }

    /// <summary>
    /// Detects the class spec from skill IDs used, following the Rust project's mapping
    /// </summary>
    private string? GetClassSpecFromSkillIds(HashSet<int> skillIds)
    {
        // Stormblade specs
        if (skillIds.Contains(1714) || skillIds.Contains(1734)) return "Iaido";
        if (skillIds.Contains(44701) || skillIds.Contains(179906)) return "Moonstrike";

        // Frost Mage specs
        if (skillIds.Contains(120901) || skillIds.Contains(120902)) return "Icicle";
        if (skillIds.Contains(1241)) return "Frostbeam";

        // Wind Knight specs
        if (skillIds.Contains(1405) || skillIds.Contains(1418)) return "Vanguard";
        if (skillIds.Contains(1419)) return "Skyward";

        // Verdant Oracle specs
        if (skillIds.Contains(1518) || skillIds.Contains(1541) || skillIds.Contains(21402)) return "Smite";
        if (skillIds.Contains(20301)) return "Lifebind";

        // Heavy Guardian specs
        if (skillIds.Contains(199902)) return "Earthfort";
        if (skillIds.Contains(1930) || skillIds.Contains(1931) || skillIds.Contains(1934) || skillIds.Contains(1935)) return "Block";

        // Marksman specs
        if (skillIds.Contains(220112) || skillIds.Contains(2203622)) return "Falconry";
        if (skillIds.Contains(2292) || skillIds.Contains(1700820) || skillIds.Contains(1700825) || skillIds.Contains(1700827)) return "Wildpack";

        // Shield Knight specs
        if (skillIds.Contains(2405)) return "Recovery";
        if (skillIds.Contains(2406)) return "Shield";

        // Beat Performer specs
        if (skillIds.Contains(2306)) return "Dissonance";
        if (skillIds.Contains(2307) || skillIds.Contains(2361) || skillIds.Contains(55302)) return "Concerto";

        return null;
    }

    // Map spec name to class id (matches Rust mapping of spec -> class)
    private int? GetClassFromSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        return spec switch
        {
            // Stormblade
            "Iaido" or "Moonstrike" => 1,
            // Frost Mage
            "Icicle" or "Frostbeam" => 2,
            // Wind Knight
            "Vanguard" or "Skyward" => 4,
            // Verdant Oracle
            "Smite" or "Lifebind" => 5,
            // Heavy Guardian
            "Earthfort" or "Block" => 9,
            // Marksman
            "Falconry" or "Wildpack" => 11,
            // Shield Knight
            "Recovery" or "Shield" => 12,
            // Beat Performer
            "Dissonance" or "Concerto" => 13,
            _ => null,
        };
    }
}
