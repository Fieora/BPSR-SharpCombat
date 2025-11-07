using BPSR_SharpCombat.Models;
using Google.Protobuf;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Consumes packet data and logs combat information
/// </summary>
public class CombatDataService : BackgroundService
{
    private readonly ILogger<CombatDataService> _logger;
    private readonly PacketCaptureService _captureService;

    public CombatDataService(
        ILogger<CombatDataService> logger,
        PacketCaptureService captureService)
    {
        _logger = logger;
        _captureService = captureService;
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
                _logger.LogDebug("Received SyncNearEntities");
                break;

            case PacketOpcode.SyncContainerData:
                _logger.LogDebug("Received SyncContainerData");
                break;

            case PacketOpcode.SyncServerTime:
                _logger.LogTrace("Received SyncServerTime");
                break;

            default:
                _logger.LogTrace("Unhandled opcode: {Opcode}", opcode);
                break;
        }
    }

    private void ProcessSyncNearDeltaInfo(byte[] data)
    {
        try
        {
            var message = SyncNearDeltaInfo.Parse(data);
            
            foreach (var delta in message.DeltaInfos)
            {
                if (delta.SkillEffects == null || delta.Uuid == null)
                    continue;

                var targetUid = delta.Uuid.Value >> 16;

                foreach (var damage in delta.SkillEffects.Damages)
                {
                    ProcessDamageInfo(damage, targetUid);
                }
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
                            
                            if (aoiSyncDelta.SkillEffects != null && aoiSyncDelta.Uuid != null)
                            {
                                var targetUid = aoiSyncDelta.Uuid.Value >> 16;
                                foreach (var damage in aoiSyncDelta.SkillEffects.Damages)
                                {
                                    ProcessDamageInfo(damage, targetUid);
                                }
                            }
                        }
                        else
                        {
                            deltaInfoInput.SkipLastField();
                        }
                    }
                }
                else
                {
                    input.SkipLastField();
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
            // Based on Rust code lib.rs commented enum:
            // 0 = Normal, 1 = Miss, 2 = Heal, 3 = Immune, 4 = Fall, 5 = Absorbed
            var damageType = damage.Type ?? 0;
            var isHeal = damageType == 2; // Heal type
            var action = isHeal ? "heals" : "damages";

            // Check for crit and lucky hit
            // From Rust: CRIT_BIT = 0b00_00_00_01 (1st bit), lucky is when lucky_value is present
            var typeFlag = damage.TypeFlag ?? 0;
            const int CRIT_BIT = 0b00_00_00_01;
            var isCrit = (typeFlag & CRIT_BIT) != 0;
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
}

