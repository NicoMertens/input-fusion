using System.Net.Sockets;
using System.Runtime.CompilerServices;
using InputFusion.Core.Protocol;

namespace InputFusion.Core.Net;

/// <summary>
/// Eine Verbindung über TCP, die Nachrichten rahmt und entrahmt. Phase 1 nutzt Klartext;
/// TLS kommt in Phase 4 als austauschbarer Stream darunter.
/// </summary>
public sealed class MessageConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly FrameReader _reader = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public MessageConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
    }

    public async Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        byte[] frame = FrameCodec.Write(type, payload.Span);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<(MessageType Type, byte[] Payload)> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            int n = await _stream.ReadAsync(buffer, ct);
            if (n == 0)
                yield break;

            _reader.Append(buffer.AsSpan(0, n));
            while (_reader.TryRead(out var type, out var payload))
                yield return (type, payload);
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
        _sendLock.Dispose();
    }
}
