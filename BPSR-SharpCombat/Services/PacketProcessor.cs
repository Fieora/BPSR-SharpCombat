using System.Threading.Channels;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Processes raw packets and extracts game protocol messages
/// </summary>
public class PacketProcessor
{
    private readonly ILogger<PacketProcessor> _logger;
    private const ulong ServiceUuid = 0x0000000063335342;

    public PacketProcessor(ILogger<PacketProcessor> logger)
    {
        _logger = logger;
    }

    private byte[] DecompressZstd(byte[] compressed)
    {
        // Match Rust's zstd::decode_all() behavior - decompress without knowing output size
        using var inputStream = new MemoryStream(compressed);
        using var decompressor = new ZstdNet.DecompressionStream(inputStream);
        using var outputStream = new MemoryStream();
        
        decompressor.CopyTo(outputStream);
        var result = outputStream.ToArray();
        
        _logger.LogTrace("Decompressed {CompressedSize} bytes to {DecompressedSize} bytes", 
            compressed.Length, result.Length);
        
        return result;
    }

    public async Task ProcessPacketAsync(BinaryReaderUtil reader, ChannelWriter<(PacketOpcode, byte[])> writer, CancellationToken cancellationToken = default)
    {
        while (reader.Remaining > 0)
        {
            try
            {
                if (reader.Remaining < 6)
                {
                    _logger.LogDebug("Insufficient data for packet header");
                    break;
                }

                var packetSize = reader.PeekUInt32();
                if (packetSize < 6)
                {
                    _logger.LogDebug("Malformed packet: packet_size < 6");
                    break;
                }

                if (reader.Remaining < packetSize)
                {
                    _logger.LogDebug("Incomplete packet: need {Size} but have {Remaining}", packetSize, reader.Remaining);
                    break;
                }

                var packetBytes = reader.ReadBytes((int)packetSize);
                var packetReader = new BinaryReaderUtil(packetBytes);
                
                // Skip the packet size we just read
                packetReader.Skip(4);
                
                var packetType = packetReader.ReadUInt16();
                var isZstdCompressed = (packetType & 0x8000) != 0;
                var msgTypeId = (ushort)(packetType & 0x7FFF);
                var fragmentType = (FragmentType)msgTypeId;

                _logger.LogTrace("Processing fragment type: {FragmentType}, compressed: {IsCompressed}", fragmentType, isZstdCompressed);

                switch (fragmentType)
                {
                    case FragmentType.Notify:
                        await ProcessNotifyFragment(packetReader, isZstdCompressed, writer, cancellationToken);
                        break;

                    case FragmentType.FrameDown:
                        await ProcessFrameDownFragment(packetReader, isZstdCompressed, writer, cancellationToken);
                        break;

                    default:
                        _logger.LogTrace("Skipping fragment type: {FragmentType}", fragmentType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing packet");
                break;
            }
        }
    }

    private async Task ProcessNotifyFragment(BinaryReaderUtil reader, bool isCompressed, ChannelWriter<(PacketOpcode, byte[])> writer, CancellationToken cancellationToken)
    {
        try
        {
            var serviceUuid = reader.ReadUInt64();
            _ = reader.ReadUInt32(); // Skip stub_id
            var methodIdRaw = reader.ReadUInt32();

            if (serviceUuid != ServiceUuid)
            {
                _logger.LogDebug("Notify: service_uuid mismatch: {ServiceUuid:X}", serviceUuid);
                return;
            }

            var payload = reader.ReadRemaining();

            if (isCompressed)
            {
                try
                {
                    payload = DecompressZstd(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Notify: zstd decompression failed");
                    return;
                }
            }

            if (Enum.IsDefined(typeof(PacketOpcode), methodIdRaw))
            {
                var opcode = (PacketOpcode)methodIdRaw;
                _logger.LogTrace("Sending packet: {Opcode}", opcode);
                await writer.WriteAsync((opcode, payload), cancellationToken);
            }
            else
            {
                _logger.LogTrace("Notify: Skipping unknown methodId: {MethodId:X}", methodIdRaw);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing Notify fragment");
        }
    }

    private async Task ProcessFrameDownFragment(BinaryReaderUtil reader, bool isCompressed, ChannelWriter<(PacketOpcode, byte[])> writer, CancellationToken cancellationToken)
    {
        try
        {
            _ = reader.ReadUInt32(); // Skip server_sequence_id
            
            if (reader.Remaining == 0)
            {
                _logger.LogDebug("FrameDown: empty payload");
                return;
            }

            var nestedPacket = reader.ReadRemaining();

            if (isCompressed)
            {
                try
                {
                    nestedPacket = DecompressZstd(nestedPacket);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "FrameDown: zstd decompression failed");
                    return;
                }
            }

            // Recursively process the nested packet
            var nestedReader = new BinaryReaderUtil(nestedPacket);
            await ProcessPacketAsync(nestedReader, writer, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing FrameDown fragment");
        }
    }
}

