using BPSR_SharpCombat.Models;
using Google.Protobuf;
using System.IO;
using System.Text;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Consumes packet data and logs combat information
/// </summary>
public class CombatDataService : BackgroundService
{
    private readonly ILogger<CombatDataService> _logger;
    private readonly PacketCaptureService _captureService;
    private readonly PlayerCache _playerCache;

    public CombatDataService(
        ILogger<CombatDataService> logger,
        PacketCaptureService captureService,
        PlayerCache playerCache)
    {
        _logger = logger;
        _captureService = captureService;
        _playerCache = playerCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Combat data service started");

        var reader = _captureService.PacketReader;

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (reader.TryRead(out var packet))
                {
                    var (opcode, data) = packet;
                    
                    try
                    {
                        ProcessPacket(opcode, data);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing packet {Opcode}", opcode);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Combat data service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Combat data service error");
        }
    }

    private void ProcessPacket(PacketOpcode opcode, byte[] data)
    {
        switch (opcode)
        {
            case PacketOpcode.ServerChangeInfo:
                _logger.LogInformation("Server change detected");
                break;

            case PacketOpcode.SyncNearDeltaInfo:
                ProcessSyncNearDeltaInfo(data);
                break;

            case PacketOpcode.SyncToMeDeltaInfo:
                ProcessSyncToMeDeltaInfo(data);
                break;

            case PacketOpcode.SyncNearEntities:
                ProcessSyncNearEntities(data);
                break;

            case PacketOpcode.SyncContainerData:
                try
                {
                    var scd = SyncContainerData.Parse(data);
                    if (scd?.VData?.CharBase != null)
                    {
                        var cb = scd.VData.CharBase;
                        if (cb.CharId != null)
                        {
                            var uid = cb.CharId.Value; // char_id is actual uid (not shifted?) Rust uses player_uid directly for container data
                            // In Rust, player_uid = v_data.char_id; they use it directly as uid
                            var nameToMerge = !string.IsNullOrWhiteSpace(cb.Name) ? cb.Name : null;
                            int? fp = cb.FightPoint;
                            
                            // Extract profession_id (class) from profession_list if available
                            int? profId = scd.VData.ProfessionList?.CurProfessionId;
                            
                            try
                            {
                                _playerCache.Merge(uid, nameToMerge, profId, null, fp);
                                _logger.LogDebug("Cached SyncContainerData player {Uid}: Name={Name}, ClassId={ClassId}, AbilityScore={FP}", 
                                    uid, 
                                    nameToMerge ?? "(missing)", 
                                    profId.HasValue ? profId.Value.ToString() : "(null)",
                                    fp.HasValue ? fp.Value.ToString() : "(null)");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Error merging SyncContainerData for uid {Uid}", uid);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("SyncContainerData received but no char_id");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Received SyncContainerData but no char_base");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error parsing SyncContainerData");
                }
                break;

            case PacketOpcode.SyncServerTime:
                _logger.LogTrace("Received SyncServerTime");
                break;

            default:
                _logger.LogTrace("Unhandled opcode: {Opcode}", opcode);
                break;
        }
    }

    // Helper enum to match Rust blueprotobuf's EEntityType mapping
    private enum BlueEntityType
    {
        EntErrType = 0,
        EntMonster = 1,
        EntChar = 2,
    }

    // Map raw UUID to BlueEntityType using the same rule as Rust: (entity_type & 0xffff)
    // 64 -> monster, 640 -> char, otherwise error type
    private BlueEntityType GetEntityTypeFromUuid(long uuid)
    {
        var low = (int)(uuid & 0xffff);
        return low switch
        {
            64 => BlueEntityType.EntMonster,
            640 => BlueEntityType.EntChar,
            _ => BlueEntityType.EntErrType,
        };
    }

    private void ProcessSyncNearEntities(byte[] data)
    {
        try
        {
            var message = SyncNearEntities.Parse(data);
            int cached = 0;
            foreach (var ent in message.Entities)
            {
                if (ent.Uuid == null) continue;
                var uuid = ent.Uuid.Value;
                var uid = uuid >> 16;

                var entityType = GetEntityTypeFromUuid(uuid);

                // Process Attrs directly from entity (matches Rust: process_player_attrs for EntChar)
                if (entityType == BlueEntityType.EntChar && ent.Attrs != null)
                {
                    _logger.LogTrace("Processing entity {Uid} with Attrs (count: {Count})", uid, ent.Attrs.Attrs.Count);
                    if (ProcessAttrCollectionForUid(uid, ent.Attrs))
                    {
                        cached++;
                    }
                }

                // Debug logging for entities with no Attrs
                if (entityType == BlueEntityType.EntChar && (ent.Attrs == null || ent.Attrs.Attrs.Count == 0))
                {
                    _logger.LogTrace("Entity {Uid} is EntChar but has no Attrs", uid);
                }

                // Debug logging for entities with no Attrs
                if (entityType == BlueEntityType.EntChar && (ent.Attrs == null || ent.Attrs.Attrs.Count == 0))
                {
                    _logger.LogTrace("Entity {Uid} is EntChar but has no Attrs", uid);
                    
                    // Save field 7 raw data for debugging (not player names)
                    if (ent.PlayerRaw != null && ent.PlayerRaw.Length > 0)
                    {
                        var previewLen = Math.Min(64, ent.PlayerRaw.Length);
                        var hexPreview = BitConverter.ToString(ent.PlayerRaw, 0, previewLen).Replace('-', ' ');
                        _logger.LogTrace("Entity {Uid} field 7 raw data (first {Len} bytes): {HexPreview}", uid, previewLen, hexPreview);
                    }
                }
            }

            if (message.Entities.Count == 0)
            {
                // Log a hex preview of the top-level payload to help debugging why parsing failed
                var previewLen = Math.Min(128, data.Length);
                var hexPreview = BitConverter.ToString(data, 0, previewLen).Replace('-', ' ');
                _logger.LogDebug("Processed SyncNearEntities: 0 entities, 0 names cached; payload (first {Len} bytes): {Hex}", previewLen, hexPreview);

                try
                {
                    var logsDir = Path.Combine(AppContext.BaseDirectory, "bpsr-logs", "raw-syncnearentities");
                    Directory.CreateDirectory(logsDir);
                    var filePath = Path.Combine(logsDir, $"payload_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.bin");
                    if (!File.Exists(filePath)) File.WriteAllBytes(filePath, data);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to save raw SyncNearEntities payload");
                }
            }
            else
            {
                _logger.LogDebug("Processed SyncNearEntities: {EntityCount} entities, {Cached} names cached", message.Entities.Count, cached);
            }

            // final summary already logged above
         }
         catch (Exception ex)
         {
             _logger.LogTrace(ex, "Error parsing SyncNearEntities");
         }
     }

     private void ProcessSyncNearDeltaInfo(byte[] data)
     {
         try
         {
             var message = SyncNearDeltaInfo.Parse(data);
             int cached = 0;

             foreach (var delta in message.DeltaInfos)
             {
                 if (delta.SkillEffects == null || delta.Uuid == null)
                     continue;

                 var rawUuid = delta.Uuid.Value;
                 var targetUid = rawUuid >> 16;
                 var entityType = GetEntityTypeFromUuid(rawUuid);

                 // If this AOI delta contains attributes and the entity is a character, extract player metadata
                 if (delta.Attrs != null && entityType == BlueEntityType.EntChar)
                 {
                     if (ProcessAttrCollectionForUid(targetUid, delta.Attrs))
                     {
                         cached++;
                     }
                 }

                 foreach (var damage in delta.SkillEffects.Damages)
                 {
                     ProcessDamageInfo(damage, targetUid);
                 }
             }

             if (cached > 0)
             {
                 _logger.LogDebug("Processed SyncNearDeltaInfo: merged {Cached} player attributes", cached);
             }
         }
         catch (Exception ex)
         {
             _logger.LogTrace(ex, "Error parsing SyncNearDeltaInfo");
         }
     }

     private void ProcessSyncToMeDeltaInfo(byte[] data)
     {
         try
         {
             // SyncToMeDeltaInfo structure: delta_info.base_delta contains AoiSyncDelta
             var input = new CodedInputStream(data);

             while (!input.IsAtEnd)
             {
                 var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
                 if (fieldNumber == 0) break;

                 if (fieldNumber == 1 && wireType == WireFormat.WireType.LengthDelimited)
                 {
                     // delta_info
                     var deltaInfoData = input.ReadBytes().ToByteArray();
                     var deltaInfoInput = new CodedInputStream(deltaInfoData);

                     while (!deltaInfoInput.IsAtEnd)
                     {
                         var (deltaFieldNum, deltaWireType) = ProtobufReader.ReadTag(deltaInfoInput);
                         if (deltaFieldNum == 0) break;

                         if (deltaFieldNum == 2 && deltaWireType == WireFormat.WireType.LengthDelimited)
                         {
                             // base_delta (AoiSyncDelta)
                             var baseDeltaData = deltaInfoInput.ReadBytes().ToByteArray();
                             var aoiSyncDelta = AoiSyncDelta.Parse(baseDeltaData);

                             if (aoiSyncDelta.Uuid != null)
                             {
                                 var rawUuid = aoiSyncDelta.Uuid.Value;
                                 var targetUid = rawUuid >> 16;
                                 var entityType = GetEntityTypeFromUuid(rawUuid);

                                 // Merge attrs into cache only for character entities
                                 if (aoiSyncDelta.Attrs != null && entityType == BlueEntityType.EntChar)
                                 {
                                     if (ProcessAttrCollectionForUid(targetUid, aoiSyncDelta.Attrs))
                                     {
                                         _logger.LogDebug("Processed SyncToMeDeltaInfo: merged player attributes for uid {Uid}", targetUid);
                                     }
                                 }

                                 if (aoiSyncDelta.SkillEffects != null)
                                 {
                                     foreach (var damage in aoiSyncDelta.SkillEffects.Damages)
                                     {
                                         ProcessDamageInfo(damage, targetUid);
                                     }
                                 }
                             }
                         }
                         else
                         {
                             ProtobufReader.SafeSkipLastField(deltaInfoInput);
                         }
                     }
                 }
                 else
                 {
                     ProtobufReader.SafeSkipLastField(input);
                 }
             }
         }
         catch (Exception ex)
         {
             _logger.LogTrace(ex, "Error parsing SyncToMeDeltaInfo");
         }
     }

    private void ProcessDamageInfo(SyncDamageInfo damage, long targetUid)
    {
        try
        {
            // Get source (attacker)
            var sourceUuid = damage.TopSummonerId ?? damage.AttackerUuid;
            if (sourceUuid == null)
            {
                _logger.LogTrace("No source UUID in damage packet");
                return;
            }

            var sourceUid = sourceUuid.Value >> 16;

            // Get skill ID
            if (damage.OwnerId == null)
            {
                _logger.LogTrace("No skill ID in damage packet");
                return;
            }
            var skillId = damage.OwnerId.Value;

            // Get value (damage or heal amount)
            // Use lucky_value if present, otherwise value (they're mutually exclusive)
            var nonLuckyValue = damage.Value;
            var luckyValue = damage.LuckyValue;
            var value = luckyValue ?? nonLuckyValue ?? 0;

            // Determine if it's heal or damage
            var damageType = damage.Type ?? 0;
            var isHeal = damageType == 2; // Heal type
            var action = isHeal ? "heals" : "damages";

            // Check for crit and lucky hit
            var typeFlag = damage.TypeFlag ?? 0;
            const int critBit = 0b00_00_00_01;
            var isCrit = (typeFlag & critBit) != 0;
            var isLucky = luckyValue.HasValue;

            // Build modifiers string
            var modifiers = new List<string>();
            if (isCrit) modifiers.Add("CRIT");
            if (isLucky) modifiers.Add("LUCKY");
            var modifiersStr = modifiers.Count > 0 ? $" ({string.Join(", ", modifiers)})" : "";

            // Log in the required format: "{sourceId} damages/heals {targetUid} with {skillId} for {value}"
            _logger.LogInformation("{SourceId} {Action} {TargetId} with {SkillId} for {Value}{Modifiers}",
                sourceUid, action, targetUid, skillId, value, modifiersStr);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error processing damage info");
        }
    }

    // Parse an AttrCollection and merge any found player metadata into the cache
    // Returns true if any useful attributes were merged into the cache
    private bool ProcessAttrCollectionForUid(long uid, AttrCollection? ac)
    {
        if (ac == null || ac.Attrs == null || ac.Attrs.Count == 0) return false;
        bool gotSomething = false;
        foreach (var a in ac.Attrs)
        {
            if (a?.Id == null) continue;
            
            // ATTR_NAME = 0x01 - Player name (skip first byte, then decode as UTF-8)
            if (a.Id == AttrType.ATTR_NAME && a.RawData != null && a.RawData.Length > 0)
            {
                var nameBytes = a.RawData.Length > 1 ? a.RawData.Skip(1).ToArray() : Array.Empty<byte>();
                bool mergedName = false;
                try
                {
                    var reader = new BinaryReaderUtil(nameBytes);
                    var playerName = reader.ReadString().Trim('\0', '\r', '\n');
                    if (!string.IsNullOrWhiteSpace(playerName) && playerName.Length < 64)
                    {
                        var hexName = BitConverter.ToString(Encoding.UTF8.GetBytes(playerName)).Replace('-', ' ');
                        _playerCache.Merge(uid, playerName, null, null, null);
                        _logger.LogDebug("Cached player {Uid} from Attr: Name={Name} (hex: {HexName})", uid, playerName, hexName);
                        gotSomething = true;
                        mergedName = true;
                    }
                    else
                    {
                        _logger.LogDebug("Skipping invalid or empty player name for uid {Uid} from Attr (primary parse)", uid);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse ATTR_NAME for uid {Uid} using BinaryReaderUtil; will try heuristics", uid);
                }

                if (!mergedName)
                {
                    // Heuristic 1: try several skip offsets and common encodings
                    try
                    {
                        string? found = null;
                        for (int skip = 0; skip <= Math.Min(4, a.RawData.Length - 1); skip++)
                        {
                            var tryBytes = a.RawData.Skip(skip).ToArray();
                            try
                            {
                                var s = System.Text.Encoding.UTF8.GetString(tryBytes).Trim('\0', '\r', '\n');
                                if (PlayerMeta.IsPlausibleName(s)) { found = s; break; }
                            }
                            catch { }

                            try
                            {
                                var s16le = System.Text.Encoding.Unicode.GetString(tryBytes).Trim('\0', '\r', '\n');
                                if (PlayerMeta.IsPlausibleName(s16le)) { found = s16le; break; }
                            }
                            catch { }

                            try
                            {
                                var s16be = System.Text.Encoding.BigEndianUnicode.GetString(tryBytes).Trim('\0', '\r', '\n');
                                if (PlayerMeta.IsPlausibleName(s16be)) { found = s16be; break; }
                            }
                            catch { }
                        }

                        if (found != null)
                        {
                            _playerCache.Merge(uid, found, null, null, null);
                            _logger.LogDebug("Cached player {Uid} from Attr (heuristic text): Name={Name}", uid, found);
                            gotSomething = true;
                            mergedName = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "ATTR_NAME heuristic text attempts failed for uid {Uid}", uid);
                    }
                }

                if (!mergedName)
                {
                    // Heuristic 2: try to parse the raw bytes as a protobuf-encoded blob and inspect nested length-delimited fields
                    try
                    {
                        var cis = new CodedInputStream(a.RawData);
                        while (!cis.IsAtEnd)
                        {
                            var (fn, wt) = ProtobufReader.ReadTag(cis);
                            if (fn == 0) break;
                            if (wt == WireFormat.WireType.LengthDelimited)
                            {
                                var inner = cis.ReadBytes().ToByteArray();
                                try
                                {
                                    var s = System.Text.Encoding.UTF8.GetString(inner).Trim('\0', '\r', '\n');
                                    if (PlayerMeta.IsPlausibleName(s))
                                    {
                                        _playerCache.Merge(uid, s, null, null, null);
                                        _logger.LogDebug("Cached player {Uid} from Attr (nested proto UTF8): Name={Name}", uid, s);
                                        gotSomething = true;
                                        mergedName = true;
                                        break;
                                    }
                                }
                                catch { }

                                try
                                {
                                    var s16le = System.Text.Encoding.Unicode.GetString(inner).Trim('\0', '\r', '\n');
                                    if (PlayerMeta.IsPlausibleName(s16le))
                                    {
                                        _playerCache.Merge(uid, s16le, null, null, null);
                                        _logger.LogDebug("Cached player {Uid} from Attr (nested proto UTF16LE): Name={Name}", uid, s16le);
                                        gotSomething = true;
                                        mergedName = true;
                                        break;
                                    }
                                }
                                catch { }

                                try
                                {
                                    var s16be = System.Text.Encoding.BigEndianUnicode.GetString(inner).Trim('\0', '\r', '\n');
                                    if (PlayerMeta.IsPlausibleName(s16be))
                                    {
                                        _playerCache.Merge(uid, s16be, null, null, null);
                                        _logger.LogDebug("Cached player {Uid} from Attr (nested proto UTF16BE): Name={Name}", uid, s16be);
                                        gotSomething = true;
                                        mergedName = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                            else
                            {
                                ProtobufReader.SafeSkipLastField(cis);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "ATTR_NAME nested protobuf inspection failed for uid {Uid}", uid);
                    }
                }

                // If still not merged, leave PlayerRaw in place for offline inspection (already done elsewhere)
            }

            // ATTR_FIGHT_POINT = 0x272e - Ability score (combat power)
            if (a.Id == AttrType.ATTR_FIGHT_POINT && a.RawData != null)
            {
                try
                {
                    var cis = new CodedInputStream(a.RawData);
                    var fp = cis.ReadInt32();
                    if (fp > 0)
                    {
                        _playerCache.Merge(uid, null, null, null, fp);
                        _logger.LogDebug("Cached player {Uid}: AbilityScore={FP}", uid, fp);
                        gotSomething = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse ATTR_FIGHT_POINT for uid {Uid}", uid);
                }
            }

            // ATTR_PROFESSION_ID = 0xdc - Class/Profession ID
            if (a.Id == AttrType.ATTR_PROFESSION_ID && a.RawData != null)
            {
                try
                {
                    var cis = new CodedInputStream(a.RawData);
                    var profId = cis.ReadInt32();
                    if (profId > 0)
                    {
                        _playerCache.Merge(uid, null, profId, null, null);
                        _logger.LogDebug("Cached player {Uid}: ClassId={ClassId}", uid, profId);
                        gotSomething = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse ATTR_PROFESSION_ID for uid {Uid}", uid);
                }
            }
        }

        if (!gotSomething)
        {
            _logger.LogDebug("Processed AttrCollection for uid {Uid} but found no usable attributes", uid);
        }

        return gotSomething;
    }
}

