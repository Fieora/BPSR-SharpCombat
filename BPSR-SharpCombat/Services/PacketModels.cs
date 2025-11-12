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
/// Changed from record to class to provide value-based equality on byte arrays.
/// </summary>
public sealed class Server : IEquatable<Server>
{
    public byte[] SourceAddr { get; }
    public ushort SourcePort { get; }
    public byte[] DestAddr { get; }
    public ushort DestPort { get; }

    public Server(byte[] sourceAddr, ushort sourcePort, byte[] destAddr, ushort destPort)
    {
        SourceAddr = sourceAddr ?? throw new ArgumentNullException(nameof(sourceAddr));
        SourcePort = sourcePort;
        DestAddr = destAddr ?? throw new ArgumentNullException(nameof(destAddr));
        DestPort = destPort;
    }

    public override string ToString()
    {
        return $"{FormatIp(SourceAddr)}:{SourcePort} -> {FormatIp(DestAddr)}:{DestPort}";
    }

    private static string FormatIp(byte[] ip) => $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";

    public bool Equals(Server? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        return SourcePort == other.SourcePort
               && DestPort == other.DestPort
               && SourceAddr.SequenceEqual(other.SourceAddr)
               && DestAddr.SequenceEqual(other.DestAddr);
    }

    public override bool Equals(object? obj) => obj is Server s && Equals(s);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(SourcePort);
        foreach (var b in SourceAddr) hc.Add(b);
        hc.Add(DestPort);
        foreach (var b in DestAddr) hc.Add(b);
        return hc.ToHashCode();
    }
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

    // Set next sequence without clearing accumulated data (used when appending cached segments)
    public void SetNextSequence(uint sequenceNumber)
    {
        _nextSeq = sequenceNumber;
    }
}
