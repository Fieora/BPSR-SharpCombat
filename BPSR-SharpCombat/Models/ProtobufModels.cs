using Google.Protobuf;

namespace BPSR_SharpCombat.Models;

/// <summary>
/// Protobuf helper for reading wire format manually
/// Based on the Rust blueprotobuf_package.rs structures
/// </summary>
public static class ProtobufReader
{
    public static (int fieldNumber, WireFormat.WireType wireType) ReadTag(CodedInputStream input)
    {
        var tag = input.ReadTag();
        if (tag == 0) return (0, 0);
        
        var fieldNumber = WireFormat.GetTagFieldNumber(tag);
        var wireType = WireFormat.GetTagWireType(tag);
        return (fieldNumber, wireType);
    }
}

/// <summary>
/// SyncNearDeltaInfo message
/// </summary>
public class SyncNearDeltaInfo
{
    public List<AoiSyncDelta> DeltaInfos { get; set; } = new();

    public static SyncNearDeltaInfo Parse(byte[] data)
    {
        var message = new SyncNearDeltaInfo();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            switch (fieldNumber)
            {
                case 1: // delta_infos (repeated)
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var deltaData = input.ReadBytes().ToByteArray();
                        message.DeltaInfos.Add(AoiSyncDelta.Parse(deltaData));
                    }
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return message;
    }
}

/// <summary>
/// AoiSyncDelta message (field 7 contains SkillEffect)
/// </summary>
public class AoiSyncDelta
{
    public long? Uuid { get; set; }
    public SkillEffect? SkillEffects { get; set; }

    public static AoiSyncDelta Parse(byte[] data)
    {
        var message = new AoiSyncDelta();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            switch (fieldNumber)
            {
                case 1: // uuid
                    message.Uuid = input.ReadInt64();
                    break;
                case 7: // skill_effects
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var skillData = input.ReadBytes().ToByteArray();
                        message.SkillEffects = SkillEffect.Parse(skillData);
                    }
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return message;
    }
}

/// <summary>
/// SkillEffect message (field 2 contains damages array)
/// </summary>
public class SkillEffect
{
    public long? Uuid { get; set; }
    public List<SyncDamageInfo> Damages { get; set; } = new();
    public long? TotalDamage { get; set; }

    public static SkillEffect Parse(byte[] data)
    {
        var message = new SkillEffect();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            switch (fieldNumber)
            {
                case 1: // uuid
                    message.Uuid = input.ReadInt64();
                    break;
                case 2: // damages (repeated)
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var damageData = input.ReadBytes().ToByteArray();
                        message.Damages.Add(SyncDamageInfo.Parse(damageData));
                    }
                    break;
                case 3: // total_damage
                    message.TotalDamage = input.ReadInt64();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return message;
    }
}

/// <summary>
/// SyncDamageInfo message - contains all damage/heal information
/// </summary>
public class SyncDamageInfo
{
    public int? DamageSource { get; set; }
    public bool? IsMiss { get; set; }
    public bool? IsCrit { get; set; }
    public int? Type { get; set; } // EDamageType
    public int? TypeFlag { get; set; }
    public long? Value { get; set; }
    public long? ActualValue { get; set; }
    public long? LuckyValue { get; set; }
    public long? HpLessenValue { get; set; }
    public long? ShieldLessenValue { get; set; }
    public long? AttackerUuid { get; set; }
    public int? OwnerId { get; set; } // skill_id
    public int? OwnerLevel { get; set; }
    public int? OwnerStage { get; set; }
    public int? HitEventId { get; set; }
    public bool? IsNormal { get; set; }
    public bool? IsDead { get; set; }
    public int? Property { get; set; }
    public long? TopSummonerId { get; set; }
    public bool? IsRainbow { get; set; }
    public int? DamageMode { get; set; }

    public static SyncDamageInfo Parse(byte[] data)
    {
        var message = new SyncDamageInfo();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, _) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            try
            {
                switch (fieldNumber)
                {
                    case 1: message.DamageSource = input.ReadInt32(); break;
                    case 2: message.IsMiss = input.ReadBool(); break;
                    case 3: message.IsCrit = input.ReadBool(); break;
                    case 4: message.Type = input.ReadInt32(); break;
                    case 5: message.TypeFlag = input.ReadInt32(); break;
                    case 6: message.Value = input.ReadInt64(); break;
                    case 7: message.ActualValue = input.ReadInt64(); break;
                    case 8: message.LuckyValue = input.ReadInt64(); break;
                    case 9: message.HpLessenValue = input.ReadInt64(); break;
                    case 10: message.ShieldLessenValue = input.ReadInt64(); break;
                    case 11: message.AttackerUuid = input.ReadInt64(); break;
                    case 12: message.OwnerId = input.ReadInt32(); break;
                    case 13: message.OwnerLevel = input.ReadInt32(); break;
                    case 14: message.OwnerStage = input.ReadInt32(); break;
                    case 15: message.HitEventId = input.ReadInt32(); break;
                    case 16: message.IsNormal = input.ReadBool(); break;
                    case 17: message.IsDead = input.ReadBool(); break;
                    case 18: message.Property = input.ReadInt32(); break;
                    case 21: message.TopSummonerId = input.ReadInt64(); break;
                    case 24: message.IsRainbow = input.ReadBool(); break;
                    case 25: message.DamageMode = input.ReadInt32(); break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }
            catch
            {
                input.SkipLastField();
            }
        }

        return message;
    }
}

/// <summary>
/// EDamageType enum (from Rust lib.rs commented code)
/// </summary>
public enum EDamageType
{
    Normal = 0,
    Miss = 1,
    Heal = 2,
    Immune = 3,
    Fall = 4,
    Absorbed = 5
}

