using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using InputFusion.Core.Protocol;

namespace InputFusion.Core.Net;

/// <summary>
/// Eine Verbindung, die Nachrichten über einen beliebigen Strom rahmt und entrahmt. Über TCP steht sie
/// zwischen PC und Notebook (siehe <see cref="FromTcp"/>), über eine Named Pipe zwischen Dienst und
/// Injector (siehe <c>InputFusion.Core.Ipc.InputPipe</c>). Phase 1 nutzt Klartext; TLS kommt in Phase 4
/// als austauschbarer Strom darunter, ohne diese Klasse zu ändern.
/// </summary>
public sealed class MessageConnection : IDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private readonly FrameReader _reader = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    /// <summary>Über einem offenen Strom. <paramref name="owner"/> wird beim Dispose mit geschlossen,
    /// etwa der zugrunde liegende <see cref="TcpClient"/>; ist er identisch mit dem Strom, genügt null.</summary>
    public MessageConnection(Stream stream, IDisposable? owner = null)
    {
        _stream = stream;
        _owner = owner;
    }

    /// <summary>
    /// Über einer TCP-Verbindung mit abgeschaltetem Nagle-Algorithmus (kleine Pakete sofort senden) und
    /// Keepalive. Ohne Keepalive bemerkt eine Seite, die nur liest, nie, dass die andere weg ist, etwa nach
    /// Standby oder Netzwechsel des Notebooks: Der Client verbände sich nie neu. So fällt eine stille
    /// Verbindung nach rund 11 s auf (5 s Ruhe, dann 3 Proben im Abstand von 2 s).
    /// </summary>
    public static MessageConnection FromTcp(TcpClient client)
    {
        client.NoDelay = true;
        var socket = client.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        return new MessageConnection(client.GetStream(), client);
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
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
        if (!ReferenceEquals(_owner, _stream))
            _owner?.Dispose();
        _sendLock.Dispose();
    }
}
