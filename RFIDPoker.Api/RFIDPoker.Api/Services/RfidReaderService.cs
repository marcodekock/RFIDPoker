using System.Collections.Concurrent;
using System.IO.Ports;
using Microsoft.Extensions.Options;
using RFIDPoker.Api.Models;

namespace RFIDPoker.Api.Services;

/// <summary>
/// Background service that reads RFID tags from Eccel C1 readers using their binary protocol.
/// Protocol: [0xF5] [lenL] [lenH] [~lenL] [~lenH] [payload...] [crcL] [crcH]
/// </summary>
public class RfidReaderService(
    IOptions<RfidConfig> options,
    ITableStateManager tableState,
    ICardTagMapper cardMapper,
    ILogger<RfidReaderService> logger) : BackgroundService
{
    private const byte STX = 0xF5;
    private const byte CMD_ACK = 0x00;
    private const byte CMD_DUMMY_COMMAND = 0x01;
    private const byte CMD_GET_TAG_COUNT = 0x02;
    private const byte CMD_GET_UID = 0x03;
    private const byte CMD_SET_POLLING = 0x06;
    private const byte CMD_ERROR = 0xFF;

    private readonly RfidConfig _config = options.Value;
    private readonly List<MuxConnection> _connections = [];
    private readonly ConcurrentQueue<(string PortName, byte[] Payload)> _responseQueue = new();
    private readonly Dictionary<string, HashSet<string>> _lastKnownTags = [];

    // Receive buffer per port for assembling frames
    private readonly ConcurrentDictionary<string, FrameParser> _frameParsers = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var mux in _config.Muxes)
        {
            try
            {
                var port = new SerialPort(mux.PortName, mux.BaudRate)
                {
                    ReadTimeout = 1000,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true,
                    Handshake = Handshake.None,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One
                };

                var parser = new FrameParser(mux.PortName, _responseQueue, logger);
                _frameParsers[mux.PortName] = parser;

                port.DataReceived += (sender, args) =>
                {
                    try
                    {
                        var sp = (SerialPort)sender;
                        var count = sp.BytesToRead;
                        if (count > 0)
                        {
                            var buffer = new byte[count];
                            sp.Read(buffer, 0, count);
                            parser.Feed(buffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error in DataReceived for {Port}", mux.PortName);
                    }
                };

                port.Open();
                _connections.Add(new MuxConnection(port, mux));
                logger.LogInformation("Opened Eccel C1 on {Port} at {Baud} baud", mux.PortName, mux.BaudRate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to open {Port}", mux.PortName);
            }
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var conn in _connections)
        {
            try { conn.Port.Close(); } catch { }
            conn.Port.Dispose();
        }
        _connections.Clear();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(500, stoppingToken);

        // Initialize each reader: send dummy command, then enable polling
        foreach (var conn in _connections)
        {
            SendCommand(conn.Port, [CMD_DUMMY_COMMAND]);
            logger.LogInformation("Sent DUMMY_COMMAND to {Port}", conn.Config.PortName);
        }

        await Task.Delay(200, stoppingToken);

        // Enable polling on each reader
        foreach (var conn in _connections)
        {
            SendCommand(conn.Port, [CMD_SET_POLLING, 0x01]);
            logger.LogInformation("Enabled polling on {Port}", conn.Config.PortName);
        }

        await Task.Delay(200, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Poll each reader for tag count
            foreach (var conn in _connections)
            {
                try
                {
                    await PollReaderAsync(conn, stoppingToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error polling {Port}", conn.Config.PortName);
                }
            }

            await Task.Delay(_config.PollingIntervalMs, stoppingToken);
        }
    }

    private async Task PollReaderAsync(MuxConnection conn, CancellationToken ct)
    {
        // Clear any stale responses
        while (_responseQueue.TryDequeue(out _)) { }

        // Get tag count
        SendCommand(conn.Port, [CMD_GET_TAG_COUNT]);
        var response = await WaitForResponseAsync(conn.Config.PortName, 300, ct);
        if (response is null) return;

        if (response[0] != CMD_ACK || response[1] != CMD_GET_TAG_COUNT) return;

        var tagCount = response[2];
        logger.LogDebug("C1 on {Port}: {Count} tags detected", conn.Config.PortName, tagCount);

        var currentTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (tagCount > 0)
        {
            for (byte i = 0; i < tagCount; i++)
            {
                SendCommand(conn.Port, [CMD_GET_UID, i]);
                var uidResponse = await WaitForResponseAsync(conn.Config.PortName, 300, ct);

                if (uidResponse is not null && uidResponse.Length > 4 &&
                    uidResponse[0] == CMD_ACK && uidResponse[1] == CMD_GET_UID)
                {
                    var uid = Convert.ToHexString(uidResponse[4..]);
                    logger.LogInformation("UID from {Port}: {Uid} (type=0x{Type:X2})",
                        conn.Config.PortName, uid, uidResponse[2]);
                    currentTags.Add(uid);
                }
            }
        }

        // Update state for the first antenna on this port
        if (conn.Config.Antennas.Count > 0)
        {
            var antenna = conn.Config.Antennas[0];
            var key = $"{conn.Config.PortName}:{antenna.AntennaIndex}";

            if (HasChanged(key, currentTags))
            {
                _lastKnownTags[key] = currentTags;
                ProcessAntennaReading(antenna, currentTags);
            }
        }
    }

    private async Task<byte[]?> WaitForResponseAsync(string portName, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (_responseQueue.TryDequeue(out var item) && item.PortName == portName)
            {
                if (item.Payload.Length > 0 && item.Payload[0] == CMD_ERROR)
                {
                    logger.LogWarning("C1 error on {Port}: cmd=0x{Cmd:X2}", portName,
                        item.Payload.Length > 1 ? item.Payload[1] : 0);
                    return null;
                }
                return item.Payload;
            }
            await Task.Delay(10, ct);
        }
        return null;
    }

    private void SendCommand(SerialPort port, byte[] payload)
    {
        var frame = BuildFrame(payload);
        port.Write(frame, 0, frame.Length);
        logger.LogDebug("Sent to {Port}: {Hex}", port.PortName, Convert.ToHexString(frame));
    }

    private static byte[] BuildFrame(byte[] payload)
    {
        ushort len = (ushort)(payload.Length + 2); // +2 for CRC
        var frame = new byte[5 + payload.Length + 2]; // STX + 4 len bytes + payload + CRC

        frame[0] = STX;
        frame[1] = (byte)(len & 0xFF);
        frame[2] = (byte)((len >> 8) & 0xFF);
        frame[3] = (byte)(frame[1] ^ 0xFF);
        frame[4] = (byte)(frame[2] ^ 0xFF);

        Array.Copy(payload, 0, frame, 5, payload.Length);

        var crc = CcittCrc(payload);
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)((crc >> 8) & 0xFF);

        return frame;
    }

    private static ushort CcittCrc(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            var temp = (ushort)(((crc >> 8) ^ b) & 0xFF);
            crc = (ushort)(CrcTable[temp] ^ (crc << 8));
        }
        return crc;
    }

    private bool HasChanged(string antennaKey, HashSet<string> currentTags)
    {
        if (!_lastKnownTags.TryGetValue(antennaKey, out var previous))
            return currentTags.Count > 0;
        return !previous.SetEquals(currentTags);
    }

    private void ProcessAntennaReading(AntennaConfig antenna, HashSet<string> tagIds)
    {
        var cards = tagIds
            .Select(t => cardMapper.GetCard(t))
            .Where(c => c is not null)
            .Cast<Card>()
            .ToList();

        switch (antenna.Function)
        {
            case AntennaFunction.PlayerSeat when antenna.SeatNumber.HasValue:
                tableState.SetPlayerHoleCards(antenna.SeatNumber.Value, cards);
                break;

            case AntennaFunction.Flop:
            case AntennaFunction.TurnRiver:
                UpdateCommunityCards();
                break;

            case AntennaFunction.Muck:
                ProcessMuck(cards);
                break;
        }
    }

    private void UpdateCommunityCards()
    {
        var communityCards = new List<Card>();
        foreach (var conn in _connections)
        {
            foreach (var antenna in conn.Config.Antennas)
            {
                if (antenna.Function is not (AntennaFunction.Flop or AntennaFunction.TurnRiver))
                    continue;
                var key = $"{conn.Config.PortName}:{antenna.AntennaIndex}";
                if (_lastKnownTags.TryGetValue(key, out var tags))
                {
                    foreach (var tagId in tags)
                    {
                        var card = cardMapper.GetCard(tagId);
                        if (card is not null)
                            communityCards.Add(card);
                    }
                }
            }
        }
        tableState.SetCommunityCards(communityCards);
    }

    private void ProcessMuck(List<Card> muckedCards)
    {
        foreach (var player in tableState.Players)
        {
            if (player.IsFolded) continue;
            if (player.HoleCards.Any(hc => muckedCards.Contains(hc)))
                tableState.FoldPlayer(player.SeatNumber);
        }
    }

    /// <summary>
    /// Parses incoming bytes into complete Eccel C1 binary frames.
    /// </summary>
    private class FrameParser(string portName, ConcurrentQueue<(string, byte[])> responseQueue, ILogger logger)
    {
        private enum State { WaitStx, WaitLen, Receiving }

        private State _state = State.WaitStx;
        private readonly byte[] _lenBuf = new byte[4];
        private int _lenIdx;
        private ushort _expectedLen;
        private byte[] _payloadBuf = [];
        private int _payloadIdx;

        public void Feed(byte[] data)
        {
            foreach (var b in data)
            {
                switch (_state)
                {
                    case State.WaitStx:
                        if (b == STX)
                        {
                            _state = State.WaitLen;
                            _lenIdx = 0;
                        }
                        break;

                    case State.WaitLen:
                        _lenBuf[_lenIdx++] = b;
                        if (_lenIdx == 4)
                        {
                            // Validate length bytes (bytes 2,3 should be complement of 0,1)
                            if (_lenBuf[0] == (byte)(_lenBuf[2] ^ 0xFF) &&
                                _lenBuf[1] == (byte)(_lenBuf[3] ^ 0xFF))
                            {
                                _expectedLen = (ushort)(_lenBuf[0] | (_lenBuf[1] << 8));
                                _payloadBuf = new byte[_expectedLen];
                                _payloadIdx = 0;
                                _state = State.Receiving;
                            }
                            else
                            {
                                logger.LogWarning("C1 frame length validation failed on {Port}", portName);
                                _state = State.WaitStx;
                            }
                        }
                        break;

                    case State.Receiving:
                        _payloadBuf[_payloadIdx++] = b;
                        if (_payloadIdx == _expectedLen)
                        {
                            // Last 2 bytes are CRC, payload is everything before
                            var payloadLen = _expectedLen - 2;
                            var payload = _payloadBuf[..payloadLen];
                            var receivedCrc = (ushort)(_payloadBuf[payloadLen] | (_payloadBuf[payloadLen + 1] << 8));
                            var calcCrc = CcittCrc(payload);

                            logger.LogInformation("Raw frame on {Port}: len={Len} payload={Hex} rcvCrc=0x{RCrc:X4} calcCrc=0x{CCrc:X4}",
                                portName, _expectedLen, Convert.ToHexString(_payloadBuf[.._expectedLen]), receivedCrc, calcCrc);

                            // Accept frame regardless of CRC for now to diagnose
                            responseQueue.Enqueue((portName, payload));

                            _state = State.WaitStx;
                        }
                        break;
                }
            }
        }
    }

    private static readonly ushort[] CrcTable =
    [
        0x0000, 0x1021, 0x2042, 0x3063, 0x4084, 0x50A5,
        0x60C6, 0x70E7, 0x8108, 0x9129, 0xA14A, 0xB16B,
        0xC18C, 0xD1AD, 0xE1CE, 0xF1EF, 0x1231, 0x0210,
        0x3273, 0x2252, 0x52B5, 0x4294, 0x72F7, 0x62D6,
        0x9339, 0x8318, 0xB37B, 0xA35A, 0xD3BD, 0xC39C,
        0xF3FF, 0xE3DE, 0x2462, 0x3443, 0x0420, 0x1401,
        0x64E6, 0x74C7, 0x44A4, 0x5485, 0xA56A, 0xB54B,
        0x8528, 0x9509, 0xE5EE, 0xF5CF, 0xC5AC, 0xD58D,
        0x3653, 0x2672, 0x1611, 0x0630, 0x76D7, 0x66F6,
        0x5695, 0x46B4, 0xB75B, 0xA77A, 0x9719, 0x8738,
        0xF7DF, 0xE7FE, 0xD79D, 0xC7BC, 0x48C4, 0x58E5,
        0x6886, 0x78A7, 0x0840, 0x1861, 0x2802, 0x3823,
        0xC9CC, 0xD9ED, 0xE98E, 0xF9AF, 0x8948, 0x9969,
        0xA90A, 0xB92B, 0x5AF5, 0x4AD4, 0x7AB7, 0x6A96,
        0x1A71, 0x0A50, 0x3A33, 0x2A12, 0xDBFD, 0xCBDC,
        0xFBBF, 0xEB9E, 0x9B79, 0x8B58, 0xBB3B, 0xAB1A,
        0x6CA6, 0x7C87, 0x4CE4, 0x5CC5, 0x2C22, 0x3C03,
        0x0C60, 0x1C41, 0xEDAE, 0xFD8F, 0xCDEC, 0xDDCD,
        0xAD2A, 0xBD0B, 0x8D68, 0x9D49, 0x7E97, 0x6EB6,
        0x5ED5, 0x4EF4, 0x3E13, 0x2E32, 0x1E51, 0x0E70,
        0xFF9F, 0xEFBE, 0xDFDD, 0xCFFC, 0xBF1B, 0xAF3A,
        0x9F59, 0x8F78, 0x9188, 0x81A9, 0xB1CA, 0xA1EB,
        0xD10C, 0xC12D, 0xF14E, 0xE16F, 0x1080, 0x00A1,
        0x30C2, 0x20E3, 0x5004, 0x4025, 0x7046, 0x6067,
        0x83B9, 0x9398, 0xA3FB, 0xB3DA, 0xC33D, 0xD31C,
        0xE37F, 0xF35E, 0x02B1, 0x1290, 0x22F3, 0x32D2,
        0x4235, 0x5214, 0x6277, 0x7256, 0xB5EA, 0xA5CB,
        0x95A8, 0x8589, 0xF56E, 0xE54F, 0xD52C, 0xC50D,
        0x34E2, 0x24C3, 0x14A0, 0x0481, 0x7466, 0x6447,
        0x5424, 0x4405, 0xA7DB, 0xB7FA, 0x8799, 0x97B8,
        0xE75F, 0xF77E, 0xC71D, 0xD73C, 0x26D3, 0x36F2,
        0x0691, 0x16B0, 0x6657, 0x7676, 0x4615, 0x5634,
        0xD94C, 0xC96D, 0xF90E, 0xE92F, 0x99C8, 0x89E9,
        0xB98A, 0xA9AB, 0x5844, 0x4865, 0x7806, 0x6827,
        0x18C0, 0x08E1, 0x3882, 0x28A3, 0xCB7D, 0xDB5C,
        0xEB3F, 0xFB1E, 0x8BF9, 0x9BD8, 0xABBB, 0xBB9A,
        0x4A75, 0x5A54, 0x6A37, 0x7A16, 0x0AF1, 0x1AD0,
        0x2AB3, 0x3A92, 0xFD2E, 0xED0F, 0xDD6C, 0xCD4D,
        0xBDAA, 0xAD8B, 0x9DE8, 0x8DC9, 0x7C26, 0x6C07,
        0x5C64, 0x4C45, 0x3CA2, 0x2C83, 0x1CE0, 0x0CC1,
        0xEF1F, 0xFF3E, 0xCF5D, 0xDF7C, 0xAF9B, 0xBFBA,
        0x8FD9, 0x9FF8, 0x6E17, 0x7E36, 0x4E55, 0x5E74,
        0x2E93, 0x3EB2, 0x0ED1, 0x1EF0
    ];

    private record MuxConnection(SerialPort Port, MuxConfig Config);
}
