namespace BPSR_SharpCombat.Services;

/// <summary>
/// Packet operation codes
/// </summary>
public enum PacketOpcode : uint
{
    ServerChangeInfo = 0xFFFFFFFF, // Special marker for server changes
    SyncNearEntities = 0x00000006,
    SyncContainerData = 0x00000015,
    SyncServerTime = 0x0000002b,
    SyncToMeDeltaInfo = 0x0000002e,
    SyncNearDeltaInfo = 0x0000002d
}

/// <summary>
/// Fragment types in the packet structure
/// </summary>
public enum FragmentType : ushort
{
    None = 0,
    Call = 1,
    Notify = 2,
    Return = 3,
    Echo = 4,
    FrameUp = 5,
    FrameDown = 6
}

/// <summary>
/// Represents a server connection (IP:Port pair)
/// </summary>
public record Server(byte[] SourceAddr, ushort SourcePort, byte[] DestAddr, ushort DestPort)
{
    public override string ToString()
    {
        return $"{FormatIp(SourceAddr)}:{SourcePort} -> {FormatIp(DestAddr)}:{DestPort}";
    }

    private static string FormatIp(byte[] ip) => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
}

/// <summary>
/// TCP reassembler for handling fragmented packets
/// </summary>
public class TcpReassembler
{
    private readonly SortedDictionary<uint, byte[]> _cache = new();
    private uint? _nextSeq;
    private readonly List<byte> _data = new();

    public uint? NextSeq => _nextSeq;
    public SortedDictionary<uint, byte[]> Cache => _cache;
    public List<byte> Data => _data;

    public void Clear(uint sequenceNumber)
    {
        _cache.Clear();
        _data.Clear();
        _nextSeq = sequenceNumber;
    }
}

