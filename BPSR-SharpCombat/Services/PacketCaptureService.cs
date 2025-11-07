using System.Threading.Channels;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace BPSR_SharpCombat.Services;

/// <summary>
/// Captures network packets and identifies game server traffic
/// </summary>
public class PacketCaptureService : BackgroundService
{
    private readonly ILogger<PacketCaptureService> _logger;
    private readonly PacketProcessor _processor;
    private readonly Channel<(PacketOpcode, byte[])> _channel;
    
    // Game server identification signatures
    private static readonly byte[] Signature1 = { 0x00, 0x63, 0x33, 0x53, 0x42, 0x00 };
    private static readonly byte[] LoginSignature1 = { 0x00, 0x00, 0x00, 0x62, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01 };
    private static readonly byte[] LoginSignature2 = { 0x00, 0x00, 0x00, 0x00, 0x0a, 0x4e };
    
    private Server? _knownServer;
    private readonly TcpReassembler _reassembler = new();
    private int _packetCount = 0;
    private DateTime _lastLogTime = DateTime.UtcNow;

    public PacketCaptureService(ILogger<PacketCaptureService> logger, PacketProcessor processor)
    {
        _logger = logger;
        _processor = processor;
        _channel = Channel.CreateUnbounded<(PacketOpcode, byte[])>();
    }

    public ChannelReader<(PacketOpcode, byte[])> PacketReader => _channel.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting packet capture service...");

        try
        {
            await Task.Run(() => CapturePackets(stoppingToken), stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Packet capture service failed");
        }
    }

    private void CapturePackets(CancellationToken cancellationToken)
    {
        var devices = CaptureDeviceList.Instance;
        
        if (devices.Count == 0)
        {
            _logger.LogError("No network devices found! Make sure npcap is installed.");
            return;
        }

        _logger.LogInformation("Found {Count} network devices", devices.Count);

        var activeDevices = new List<ICaptureDevice>();

        // Start capture on all suitable devices
        foreach (var device in devices)
        {
            if (device is not LibPcapLiveDevice)
                continue;

            // Skip loopback and Bluetooth devices
            var description = device.Description?.ToLower() ?? "";
            if (description.Contains("loopback") || description.Contains("bluetooth"))
            {
                _logger.LogDebug("Skipping device: {Description}", device.Description);
                continue;
            }

            try
            {
                _logger.LogInformation("Opening device: {Description}", device.Description);
                
                device.Open(DeviceModes.Promiscuous, 1000);
                device.Filter = "tcp";
                
                device.OnPacketArrival += (_, capture) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    try
                    {
                        ProcessCapturedPacket(capture.GetPacket());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Error processing captured packet");
                    }
                };

                _logger.LogInformation("Starting capture on {Description}", device.Description);
                device.StartCapture();
                activeDevices.Add(device);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to capture on device: {Description}", device.Description);
            }
        }

        if (activeDevices.Count == 0)
        {
            _logger.LogError("No devices could be opened for capture!");
            return;
        }

        _logger.LogInformation("Successfully started capture on {Count} device(s)", activeDevices.Count);

        // Wait until cancellation
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(100);
            }
        }
        finally
        {
            // Stop and close all devices
            _logger.LogInformation("Stopping packet capture...");
            foreach (var device in activeDevices)
            {
                try
                {
                    device.StopCapture();
                    device.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing device");
                }
            }
        }
    }

    private void ProcessCapturedPacket(RawCapture rawCapture)
    {
        try
        {
            var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
            var tcpPacket = packet.Extract<TcpPacket>();
            var ipPacket = packet.Extract<IPPacket>();

            if (tcpPacket == null || ipPacket == null)
                return;

            var payload = tcpPacket.PayloadData;
            if (payload == null || payload.Length == 0)
                return;

            // Increment packet counter and log periodically
            _packetCount++;
            var now = DateTime.UtcNow;
            if ((now - _lastLogTime).TotalSeconds >= 10)
            {
                _logger.LogDebug("Captured {Count} TCP packets in last 10 seconds", _packetCount);
                _packetCount = 0;
                _lastLogTime = now;
            }

            _logger.LogTrace("TCP packet: {Src}:{SrcPort} -> {Dst}:{DstPort}, payload: {Size} bytes",
                ipPacket.SourceAddress, tcpPacket.SourcePort,
                ipPacket.DestinationAddress, tcpPacket.DestinationPort,
                payload.Length);

            var currentServer = new Server(
                ipPacket.SourceAddress.GetAddressBytes(),
                tcpPacket.SourcePort,
                ipPacket.DestinationAddress.GetAddressBytes(),
                tcpPacket.DestinationPort
            );

            // Try to identify game server if not known yet
            if (_knownServer != currentServer)
            {
                if (TryIdentifyGameServer(payload, currentServer, tcpPacket.SequenceNumber))
                {
                    return; // Server identified, skip this packet
                }

                // If we don't know the server yet, skip
                if (_knownServer == null)
                    return;
            }

            // Process packets from known server
            ReassembleAndProcess(tcpPacket.SequenceNumber, payload);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error in ProcessCapturedPacket");
        }
    }

    private bool TryIdentifyGameServer(byte[] payload, Server server, uint sequenceNumber)
    {
        // Method 1: Look for signature in fragmented packets
        if (payload.Length >= 10 && payload[4] == 0)
        {
            var reader = new BinaryReaderUtil(payload);
            reader.Skip(10); // Skip first 10 bytes

            var iterations = 0;
            while (reader.Remaining >= 4)
            {
                if (++iterations > 1000)
                {
                    _logger.LogWarning("Stuck in game server identification loop");
                    break;
                }

                try
                {
                    var fragLen = reader.ReadUInt32();
                    var fragPayloadLen = (int)(fragLen - 4);

                    if (reader.Remaining >= fragPayloadLen)
                    {
                        var frag = reader.ReadBytes(fragPayloadLen);
                        
                        if (frag.Length >= 5 + Signature1.Length)
                        {
                            var signatureMatch = true;
                            for (int i = 0; i < Signature1.Length; i++)
                            {
                                if (frag[5 + i] != Signature1[i])
                                {
                                    signatureMatch = false;
                                    break;
                                }
                            }

                            if (signatureMatch)
                            {
                                // Only log and notify if this is a new/different server
                                if (_knownServer != server)
                                {
                                    _logger.LogInformation("Identified game server (by signature): {Server}", server);
                                    _knownServer = server;
                                    _reassembler.Clear(sequenceNumber + (uint)payload.Length);
                                    _ = _channel.Writer.WriteAsync((PacketOpcode.ServerChangeInfo, Array.Empty<byte>()));
                                }
                                return true;
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        // Method 2: Login packet signature (98 bytes)
        if (payload.Length == 98)
        {
            var sig1Match = true;
            for (int i = 0; i < LoginSignature1.Length; i++)
            {
                if (payload[i] != LoginSignature1[i])
                {
                    sig1Match = false;
                    break;
                }
            }

            var sig2Match = true;
            for (int i = 0; i < LoginSignature2.Length; i++)
            {
                if (payload[14 + i] != LoginSignature2[i])
                {
                    sig2Match = false;
                    break;
                }
            }

            if (sig1Match && sig2Match)
            {
                // Only log and notify if this is a new/different server
                if (_knownServer != server)
                {
                    _logger.LogInformation("Identified game server (by login packet): {Server}", server);
                    _knownServer = server;
                    _reassembler.Clear(sequenceNumber + (uint)payload.Length);
                    _ = _channel.Writer.WriteAsync((PacketOpcode.ServerChangeInfo, Array.Empty<byte>()));
                }
                return true;
            }
        }

        return false;
    }

    private void ReassembleAndProcess(uint sequenceNumber, byte[] payload)
    {
        // Initialize next sequence if not set
        if (_reassembler.NextSeq == null)
        {
            _reassembler.Clear(sequenceNumber);
        }

        // Only cache packets with expected sequence
        if (_reassembler.NextSeq.HasValue && 
            _reassembler.NextSeq.Value == sequenceNumber)
        {
            _reassembler.Cache[sequenceNumber] = payload;
        }

        // Process cached packets in order
        var iterations = 0;
        while (_reassembler.NextSeq.HasValue && 
               _reassembler.Cache.TryGetValue(_reassembler.NextSeq.Value, out var cachedData))
        {
            if (++iterations % 1000 == 0)
            {
                _logger.LogWarning("Potential infinite loop in cache processing: iteration={Iterations}", iterations);
            }

            var seq = _reassembler.NextSeq.Value;
            _reassembler.Data.AddRange(cachedData);
            _reassembler.Cache.Remove(seq);
            
            // Update next expected sequence (handle wrap-around)
            var nextSeq = seq + (uint)cachedData.Length;
            _reassembler.Clear(nextSeq);
            _reassembler.Data.AddRange(cachedData);
        }

        // Process complete packets from reassembled data
        iterations = 0;
        while (_reassembler.Data.Count > 4)
        {
            if (++iterations % 1000 == 0)
            {
                _logger.LogWarning("Potential infinite loop in data processing: iteration={Iterations}", iterations);
                break;
            }

            try
            {
                var reader = new BinaryReaderUtil(_reassembler.Data.ToArray());
                var packetSize = reader.PeekUInt32();

                if (_reassembler.Data.Count < packetSize)
                    break;

                var packetBytes = _reassembler.Data.Take((int)packetSize).ToArray();
                _reassembler.Data.RemoveRange(0, (int)packetSize);

                var packetReader = new BinaryReaderUtil(packetBytes);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _processor.ProcessPacketAsync(packetReader, _channel.Writer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error processing packet");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error reading packet size");
                break;
            }
        }
    }
}

