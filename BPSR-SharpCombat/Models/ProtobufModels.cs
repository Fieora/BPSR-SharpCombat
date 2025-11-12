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
        try
        {
            var tag = input.ReadTag();
            if (tag == 0) return (0, 0);

            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            return (fieldNumber, wireType);
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            // If parsing a tag fails (malformed/invalid tag), return a neutral (0,0) to allow callers
            // to treat this as end-of-message/unknown and continue gracefully.
            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    // Safe wrapper around CodedInputStream.SkipLastField to avoid throwing when encountering malformed end-group tags
    public static void SafeSkipLastField(CodedInputStream? input)
    {
        if (input == null) return;
        try
        {
            input.SkipLastField();
        }
        catch (Google.Protobuf.InvalidProtocolBufferException)
        {
            // ignore malformed end-group / missing start-group situations
        }
        catch
        {
            // swallow any other parsing issue for robustness in lenient parser
        }
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
                    ProtobufReader.SafeSkipLastField(input);
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
    // Attrs (optional) - some AOI deltas include an AttrCollection with player attributes
    public AttrCollection? Attrs { get; set; }

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
                case 6: // attrs (length-delimited) - try to parse AttrCollection
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var attrsData = input.ReadBytes().ToByteArray();
                        var ac = AttrCollection.ParseOptional(attrsData);
                        if (ac != null) message.Attrs = ac;
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 7: // skill_effects
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var skillData = input.ReadBytes().ToByteArray();
                        message.SkillEffects = SkillEffect.Parse(skillData);
                    }
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
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
    public List<SyncDamageInfo> Damages { get; set; } = new List<SyncDamageInfo>();
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
                    ProtobufReader.SafeSkipLastField(input);
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
                        ProtobufReader.SafeSkipLastField(input);
                        break;
                }
            }
            catch
            {
                ProtobufReader.SafeSkipLastField(input);
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

/// <summary>
/// SyncNearEntities message (top-level contains repeated entity records)
/// We implement a lenient parser: it looks for repeated length-delimited entity entries
/// and inside each entity tries to read uuid and an optional PlayerMeta submessage that
/// can contain a player name (string).
/// </summary>
public class SyncNearEntities
{
    public List<SyncEntity> Entities { get; set; } = new();

    public static SyncNearEntities Parse(byte[] data)
    {
        var message = new SyncNearEntities();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            switch (fieldNumber)
            {
                case 1: // repeated entity entries (length-delimited)
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var entityData = input.ReadBytes().ToByteArray();
                        message.Entities.Add(SyncEntity.Parse(entityData));
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                default:
                    // Some servers embed entity entries under non-standard field numbers.
                    // Be permissive: if the unknown field is length-delimited, attempt to parse it as a SyncEntity
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var maybeEntityData = input.ReadBytes().ToByteArray();
                        try
                        {
                            var maybeEntity = SyncEntity.Parse(maybeEntityData);
                            if (maybeEntity.Uuid != null || maybeEntity.Attrs != null)
                            {
                                message.Entities.Add(maybeEntity);
                            }
                            else
                            {
                                // Try parsing as nested SyncNearEntities
                                try
                                {
                                    var nested = SyncNearEntities.Parse(maybeEntityData);
                                    if (nested.Entities.Count > 0)
                                    {
                                        message.Entities.AddRange(nested.Entities);
                                    }
                                }
                                catch
                                {
                                    // ignore
                                }
                            }
                        }
                        catch
                        {
                            // If parsing fails, try nested SyncNearEntities
                            try
                            {
                                var nested = SyncNearEntities.Parse(maybeEntityData);
                                if (nested.Entities.Count > 0)
                                {
                                    message.Entities.AddRange(nested.Entities);
                                }
                            }
                            catch
                            {
                                // ignore
                            }
                        }
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
            }
        }

        return message;
    }
}

/// <summary>
/// Entity record inside SyncNearEntities. Matches Rust protobuf Entity struct:
/// - Field 1: uuid (int64)
/// - Field 2: ent_type (EEntityType enum as int32)
/// - Field 3: attrs (AttrCollection) - contains player attributes
/// - Field 7: buff_infos (BuffInfoSync)
/// </summary>
public class SyncEntity
{
    public long? Uuid { get; set; }
    public int? Type { get; set; }
    public AttrCollection? Attrs { get; set; }
    // Raw bytes of field 7 for debugging
    public byte[]? PlayerRaw { get; set; }

    public static SyncEntity Parse(byte[] data)
    {
        var message = new SyncEntity();
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
                case 2: // ent_type (entity type enum)
                    if (wireType == WireFormat.WireType.Varint)
                    {
                        message.Type = input.ReadInt32();
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 3: // attrs (AttrCollection) - THIS is where player data is!
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var attrsData = input.ReadBytes().ToByteArray();
                        var ac = AttrCollection.ParseOptional(attrsData);
                        if (ac != null && ac.Attrs.Count > 0)
                        {
                            message.Attrs = ac;
                        }
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 7: // Some other data (not player names) - save for debugging but don't parse
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var field7Data = input.ReadBytes().ToByteArray();
                        message.PlayerRaw = field7Data; // Save for debugging only
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                default:
                    // Skip unknown fields - we only care about field 1 (uuid) and field 2 (attrs)
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }

        return message;
    }
}

/// <summary>
/// Player metadata block. We only care about name (field 1) for now.
/// Extended to parse class_id (field 2), spec_id (field 3) and abilities (field 4).
/// </summary>
public class PlayerMeta
{
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public int? SpecId { get; set; }
    // AbilityScore is a single integer (fight_point) sent elsewhere; keep as int?
    public int? AbilityScore { get; set; }

    public static PlayerMeta Parse(byte[] data)
    {
        var message = new PlayerMeta();
        var input = new CodedInputStream(data);

        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;

            switch (fieldNumber)
            {
                case 1: // name
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        message.Name = input.ReadString();
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 2: // class_id
                    if (wireType == WireFormat.WireType.Varint)
                    {
                        message.ClassId = input.ReadInt32();
                    }
                    else if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var bytes = input.ReadBytes().ToByteArray();
                        var nested = new CodedInputStream(bytes);
                        while (!nested.IsAtEnd)
                        {
                            var (nfn, nwt) = ProtobufReader.ReadTag(nested);
                            if (nfn == 0) break;
                            if (nwt == WireFormat.WireType.Varint)
                            {
                                try { message.ClassId = nested.ReadInt32(); break; } catch { ProtobufReader.SafeSkipLastField(nested); }
                            }
                            else
                            {
                                ProtobufReader.SafeSkipLastField(nested);
                            }
                        }
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 3: // spec_id
                    if (wireType == WireFormat.WireType.Varint)
                    {
                        message.SpecId = input.ReadInt32();
                    }
                    else if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var bytes = input.ReadBytes().ToByteArray();
                        var nested = new CodedInputStream(bytes);
                        while (!nested.IsAtEnd)
                        {
                            var (nfn, nwt) = ProtobufReader.ReadTag(nested);
                            if (nfn == 0) break;
                            if (nwt == WireFormat.WireType.Varint)
                            {
                                try { message.SpecId = nested.ReadInt32(); break; } catch { ProtobufReader.SafeSkipLastField(nested); }
                            }
                            else
                            {
                                ProtobufReader.SafeSkipLastField(nested);
                            }
                        }
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                case 4: // ability score (could be varint or a nested message containing varint)
                    try
                    {
                        if (wireType == WireFormat.WireType.Varint)
                        {
                            message.AbilityScore = input.ReadInt32();
                        }
                        else if (wireType == WireFormat.WireType.LengthDelimited)
                        {
                            var bytes = input.ReadBytes().ToByteArray();
                            // try to read a varint from the bytes
                            var nested = new CodedInputStream(bytes);
                            while (!nested.IsAtEnd)
                            {
                                var tag = nested.ReadTag();
                                if (tag == 0) break;
                                var fn = WireFormat.GetTagFieldNumber(tag);
                                var wt = WireFormat.GetTagWireType(tag);
                                if (wt == WireFormat.WireType.Varint)
                                {
                                    try { message.AbilityScore = nested.ReadInt32(); break; } catch { ProtobufReader.SafeSkipLastField(nested); }
                                }
                                else
                                {
                                    try { ProtobufReader.SafeSkipLastField(nested); } catch { break; }
                                }
                            }
                        }
                        else
                        {
                            ProtobufReader.SafeSkipLastField(input);
                        }
                    }
                    catch
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
                default:
                    // existing salvage logic (try decode nested strings / recursive parse)
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var unknown = input.ReadBytes().ToByteArray();

                        try
                        {
                            var s = System.Text.Encoding.UTF8.GetString(unknown);
                            s = s.Trim('\0', '\r', '\n');
                            if (message.Name == null && PlayerMeta.IsPlausibleName(s))
                            {
                                message.Name = s;
                            }
                            else
                            {
                                try
                                {
                                    var s16le = System.Text.Encoding.Unicode.GetString(unknown).Trim('\0', '\r', '\n');
                                    if (message.Name == null && PlayerMeta.IsPlausibleName(s16le))
                                    {
                                        message.Name = s16le;
                                        continue;
                                    }
                                }
                                catch { }

                                try
                                {
                                    var s16be = System.Text.Encoding.BigEndianUnicode.GetString(unknown).Trim('\0', '\r', '\n');
                                    if (message.Name == null && PlayerMeta.IsPlausibleName(s16be))
                                    {
                                        message.Name = s16be;
                                        continue;
                                    }
                                }
                                catch { }

                                var nested = Parse(unknown);
                                if (message.Name == null && nested.Name != null)
                                    message.Name = nested.Name;
                                if (message.ClassId == null && nested.ClassId != null)
                                    message.ClassId = nested.ClassId;
                                if (message.SpecId == null && nested.SpecId != null)
                                    message.SpecId = nested.SpecId;
                                if (message.AbilityScore == null && nested.AbilityScore != null)
                                    message.AbilityScore = nested.AbilityScore;

                                if (message.Name == null)
                                {
                                    var candidate = TryExtractPrintableName(unknown);
                                    if (candidate != null && PlayerMeta.IsPlausibleName(candidate))
                                    {
                                        message.Name = candidate;
                                    }
                                }

                                // Heuristic: some PlayerMeta blobs contain repeated small messages of the form
                                // { field1: indicator (varint), field2: id (varint) }
                                // e.g. bytes: 08 02 10 BE 01  (indicator=2, id=190)
                                // We'll parse those and map indicator==2 => ClassId, indicator==1 => SpecId.
                                try
                                {
                                    var cisMini = new CodedInputStream(unknown);
                                    while (!cisMini.IsAtEnd)
                                    {
                                        int? indicator = null;
                                        int? miniId = null;
                                        // Read fields from the small message
                                        while (!cisMini.IsAtEnd)
                                        {
                                            var tagMini = cisMini.ReadTag();
                                            if (tagMini == 0) break;
                                            var fn = WireFormat.GetTagFieldNumber(tagMini);
                                            var wt = WireFormat.GetTagWireType(tagMini);
                                            if (wt == WireFormat.WireType.Varint)
                                            {
                                                var v = cisMini.ReadInt32();
                                                if (fn == 1) indicator = v;
                                                else if (fn == 2) miniId = v;
                                            }
                                            else
                                            {
                                                ProtobufReader.SafeSkipLastField(cisMini);
                                            }
                                        }

                                        if (miniId != null)
                                        {
                                            if (indicator == 2)
                                            {
                                                if (message.ClassId == null) message.ClassId = miniId;
                                            }
                                            else if (indicator == 1)
                                            {
                                                if (message.SpecId == null) message.SpecId = miniId;
                                            }
                                            else
                                            {
                                                if (message.ClassId == null) message.ClassId = miniId;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            // ignore decoding errors
                        }
                    }
                    else
                    {
                        ProtobufReader.SafeSkipLastField(input);
                    }
                    break;
            }
        }

        return message;
    }

    public static bool IsPlausibleName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length > 64) return false;

        int printable = 0;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) || "-_.'".Contains(ch)) printable++;
        }
        // Require at least half printable and at least one letter
        if (printable * 2 < s.Length) return false;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch)) return true;
        }
        return false;
    }

    // Heuristic: scan a byte blob for the longest printable ASCII or UTF-16LE/BE run and return it
    private static string? TryExtractPrintableName(byte[] data)
    {
        string? best = null;

        // ASCII scan: find runs of printable chars (letter/digit/space and -_.'), min length 3
        int minRun = 3;
        int i = 0;
        while (i < data.Length)
        {
            int j = i;
            while (j < data.Length && IsPrintableByte(data[j])) j++;
            var len = j - i;
            if (len >= minRun)
            {
                try
                {
                    var s = System.Text.Encoding.ASCII.GetString(data, i, len).Trim('\0', '\r', '\n');
                    if (PlayerMeta.IsPlausibleName(s) && (best == null || s.Length > best.Length)) best = s;
                }
                catch { }
            }
            i = j + 1;
        }

        // UTF-16LE scan: look for runs where even bytes are printable ASCII and odd bytes are zero (common for UTF-16LE short strings)
        for (int start = 0; start + 1 < data.Length; start += 2)
        {
            int k = start;
            while (k + 1 < data.Length && data[k+1] == 0 && IsPrintableByte(data[k])) k += 2;
            var runLen = k - start + 1;
            var chars = runLen / 2;
            if (chars >= minRun)
            {
                try
                {
                    var s = System.Text.Encoding.Unicode.GetString(data, start, chars * 2).Trim('\0', '\r', '\n');
                    if (PlayerMeta.IsPlausibleName(s) && (best == null || s.Length > best.Length)) best = s;
                }
                catch { }
            }
        }

        // UTF-16BE scan
        for (int start = 0; start + 1 < data.Length; start += 2)
        {
            int k = start;
            while (k + 1 < data.Length && data[k] == 0 && IsPrintableByte(data[k+1])) k += 2;
            var runLen = k - start + 1;
            var chars = runLen / 2;
            if (chars >= minRun)
            {
                try
                {
                    var s = System.Text.Encoding.BigEndianUnicode.GetString(data, start, chars * 2).Trim('\0', '\r', '\n');
                    if (PlayerMeta.IsPlausibleName(s) && (best == null || s.Length > best.Length)) best = s;
                }
                catch { }
            }
        }

        return best;
    }

    private static bool IsPrintableByte(byte b)
    {
        // printable ASCII, space and common punctuation
        return b >= 0x20 && b <= 0x7E && (char.IsLetterOrDigit((char)b) || char.IsWhiteSpace((char)b) || "-_.'".IndexOf((char)b) >= 0);
    }
}

/// <summary>
/// Attribute (id + raw_data) used in AttrCollection
/// </summary>
public class Attr
{
    public int? Id { get; set; }
    public byte[]? RawData { get; set; }

    public static Attr Parse(byte[] data)
    {
        var a = new Attr();
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;
            switch (fieldNumber)
            {
                case 1:
                    a.Id = input.ReadInt32();
                    break;
                case 2:
                    if (wireType == WireFormat.WireType.LengthDelimited)
                        a.RawData = input.ReadBytes().ToByteArray();
                    else
                        ProtobufReader.SafeSkipLastField(input);
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }
        return a;
    }
}

public class AttrCollection
{
    public long? Uuid { get; set; }
    public List<Attr> Attrs { get; set; } = new();

    public static AttrCollection? ParseOptional(byte[] data)
    {
        try
        {
            var c = new AttrCollection();
            var input = new CodedInputStream(data);
            while (!input.IsAtEnd)
            {
                var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
                if (fieldNumber == 0) break;
                switch (fieldNumber)
                {
                    case 1:
                        c.Uuid = input.ReadInt64();
                        break;
                    case 2:
                        if (wireType == WireFormat.WireType.LengthDelimited)
                        {
                            var attrData = input.ReadBytes().ToByteArray();
                            c.Attrs.Add(Attr.Parse(attrData));
                        }
                        else
                            ProtobufReader.SafeSkipLastField(input);
                        break;
                    default:
                        ProtobufReader.SafeSkipLastField(input);
                        break;
                }
            }
            return c;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// CharBaseInfo minimal: we only parse name and fight_point
/// Matches Rust CharBaseInfo.fight_point = int32 tag 35, name = tag 5
/// </summary>
public class CharBaseInfo
{
    public long? CharId { get; set; }
    public string? Name { get; set; }
    public int? FightPoint { get; set; }

    public static CharBaseInfo Parse(byte[] data)
    {
        var msg = new CharBaseInfo();
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;
            switch (fieldNumber)
            {
                case 1:
                    msg.CharId = input.ReadInt64();
                    break;
                case 5:
                    if (wireType == WireFormat.WireType.LengthDelimited)
                        msg.Name = input.ReadString();
                    else
                        ProtobufReader.SafeSkipLastField(input);
                    break;
                case 35:
                    // fight_point
                    msg.FightPoint = input.ReadInt32();
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }
        return msg;
    }
}

/// <summary>
/// ProfessionList: field 61 in CharSerialize, contains cur_profession_id
/// </summary>
public class ProfessionList
{
    public int? CurProfessionId { get; set; }

    public static ProfessionList Parse(byte[] data)
    {
        var m = new ProfessionList();
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;
            // cur_profession_id is likely field 1 or 2 in ProfessionList
            switch (fieldNumber)
            {
                case 1:
                case 2:
                    if (wireType == WireFormat.WireType.Varint)
                        m.CurProfessionId = input.ReadInt32();
                    else
                        ProtobufReader.SafeSkipLastField(input);
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }
        return m;
    }
}

/// <summary>
/// CharSerialize minimal parsing: field 2 = char_base, field 61 = profession_list
/// </summary>
public class CharSerialize
{
    public CharBaseInfo? CharBase { get; set; }
    public ProfessionList? ProfessionList { get; set; }

    public static CharSerialize Parse(byte[] data)
    {
        var m = new CharSerialize();
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;
            switch (fieldNumber)
            {
                case 2:
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var cb = input.ReadBytes().ToByteArray();
                        m.CharBase = CharBaseInfo.Parse(cb);
                    }
                    else ProtobufReader.SafeSkipLastField(input);
                    break;
                case 61:
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var pl = input.ReadBytes().ToByteArray();
                        m.ProfessionList = ProfessionList.Parse(pl);
                    }
                    else ProtobufReader.SafeSkipLastField(input);
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }
        return m;
    }
}

/// <summary>
/// SyncContainerData minimal: field 1 = v_data (CharSerialize)
/// </summary>
public class SyncContainerData
{
    public CharSerialize? VData { get; set; }

    public static SyncContainerData Parse(byte[] data)
    {
        var msg = new SyncContainerData();
        var input = new CodedInputStream(data);
        while (!input.IsAtEnd)
        {
            var (fieldNumber, wireType) = ProtobufReader.ReadTag(input);
            if (fieldNumber == 0) break;
            switch (fieldNumber)
            {
                case 1:
                    if (wireType == WireFormat.WireType.LengthDelimited)
                    {
                        var vd = input.ReadBytes().ToByteArray();
                        msg.VData = CharSerialize.Parse(vd);
                    }
                    else ProtobufReader.SafeSkipLastField(input);
                    break;
                default:
                    ProtobufReader.SafeSkipLastField(input);
                    break;
            }
        }
        return msg;
    }
}
