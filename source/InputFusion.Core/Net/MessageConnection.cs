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
    private long _lastReceived = Environment.TickCount64;
    private bool _disposed;

    /// <summary>Abstand, in dem <see cref="MonitorAsync"/> einen Heartbeat schickt.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    /// <summary>So lange darf die Gegenseite schweigen, bevor sie als weg gilt.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3);

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
        // Frames, die beim vorigen Lesen schon mitkamen (etwa direkt nach der Anmeldung), zuerst ausliefern.
        while (_reader.TryRead(out var bufferedType, out var bufferedPayload))
            yield return (bufferedType, bufferedPayload);

        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            int n = await _stream.ReadAsync(buffer, ct);
            if (n == 0)
                yield break;
            Volatile.Write(ref _lastReceived, Environment.TickCount64);

            _reader.Append(buffer.AsSpan(0, n));
            while (_reader.TryRead(out var type, out var payload))
                yield return (type, payload);
        }
    }

    /// <summary>
    /// Schickt jede <see cref="HeartbeatInterval"/> einen Heartbeat und wacht, ob von der Gegenseite etwas
    /// ankommt; jede Nachricht zählt, nicht nur Heartbeats. Kehrt mit true zurück, sobald sie länger als
    /// <see cref="HeartbeatTimeout"/> schweigt, mit false bei Abbruch über <paramref name="ct"/>. Läuft
    /// neben <see cref="ReceiveAsync"/>, das die Zeit des letzten Empfangs setzt.
    ///
    /// <para>TCP-Keepalive reicht nicht: Es greift nur, solange keine Daten unterwegs sind. Schickt der Host
    /// gerade Mausbewegungen an ein Notebook im Standby, füllen sie erst den Sendepuffer, und bis die
    /// TCP-Wiederholung aufgibt, verschluckt der Host weiter alle Eingaben.</para>
    /// </summary>
    public async Task<bool> MonitorAsync(CancellationToken ct)
    {
        Volatile.Write(ref _lastReceived, Environment.TickCount64);
        Task? pending = null;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return false; }

            if (Environment.TickCount64 - Volatile.Read(ref _lastReceived) > HeartbeatTimeout.TotalMilliseconds)
                return true;

            // Nicht auf das Senden warten: Bei totem Gegenüber kann es im vollen Puffer hängen, und die
            // Prüfung oben muss trotzdem laufen. Höchstens ein Heartbeat zugleich unterwegs.
            if (pending is null || pending.IsCompleted)
                pending = SendHeartbeatAsync(ct);
        }
        return false;
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            await SendAsync(MessageType.Heartbeat, ReadOnlyMemory<byte>.Empty, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Ein Sendefehler zeigt sich auch auf dem Empfangsweg; das Schweigen erkennt MonitorAsync.
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
