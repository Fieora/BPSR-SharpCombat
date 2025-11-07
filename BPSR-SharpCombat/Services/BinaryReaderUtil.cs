using System.Buffers.Binary;
using System.Text;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Big-endian binary reader for network packet parsing
/// </summary>
public class BinaryReaderUtil
{
    private readonly byte[] _data;
    private int _position;

    public BinaryReaderUtil(byte[] data)
    {
        _data = data;
        _position = 0;
    }

    public int Remaining => _data.Length - _position;
    public int Length => _data.Length;
    public int Position => _position;

    public ushort ReadUInt16()
    {
        if (Remaining < 2)
            throw new EndOfStreamException("Not enough data to read UInt16");
        
        var value = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_position, 2));
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        if (Remaining < 4)
            throw new EndOfStreamException("Not enough data to read UInt32");
        
        var value = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_position, 4));
        _position += 4;
        return value;
    }

    public uint PeekUInt32()
    {
        if (Remaining < 4)
            throw new EndOfStreamException("Not enough data to peek UInt32");
        
        return BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_position, 4));
    }

    public ulong ReadUInt64()
    {
        if (Remaining < 8)
            throw new EndOfStreamException("Not enough data to read UInt64");
        
        var value = BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_position, 8));
        _position += 8;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        if (Remaining < count)
            throw new EndOfStreamException($"Not enough data to read {count} bytes");
        
        var bytes = new byte[count];
        Array.Copy(_data, _position, bytes, 0, count);
        _position += count;
        return bytes;
    }

    public string ReadString()
    {
        var bytes = ReadRemaining();
        return Encoding.UTF8.GetString(bytes);
    }

    public byte[] ReadRemaining()
    {
        var bytes = new byte[Remaining];
        Array.Copy(_data, _position, bytes, 0, Remaining);
        _position = _data.Length;
        return bytes;
    }

    public ReadOnlySpan<byte> GetRemaining()
    {
        return _data.AsSpan(_position);
    }

    public void Skip(int count)
    {
        if (Remaining < count)
            throw new EndOfStreamException($"Not enough data to skip {count} bytes");
        
        _position += count;
    }
}

