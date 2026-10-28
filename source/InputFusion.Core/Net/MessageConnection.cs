using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using InputFusion.Core.Protocol;

namespace InputFusion.Core.Net;

/// <summary>
/// Eine Verbindung, die Nachrichten über einen beliebigen Strom rahmt und entrahmt. Über TCP steht sie
/// zwischen PC und Notebook (siehe <see cref="FromTcp"/>), über eine Named Pipe zwischen Dienst und
/// Injector (siehe <c>InputFusion.Core.Ipc.InputPipe</c>). Zwischen PC und Notebook schaltet die
/// <see cref="Handshake"/> danach die Verschlüsselung ein (<see cref="EnableEncryption"/>); die Pipes bleiben
/// im Klartext, dort spricht nur der eigene Rechner.
/// </summary>
public sealed class MessageConnection : IDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private readonly FrameReader _reader = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private FrameCipher? _sendCipher;
    private FrameCipher? _receiveCipher;
    private Int64 _lastReceived = Environment.TickCount64;
    private Int64 _sentFrames, _receivedFrames;
    // Stopwatch-Ticks der letzten Laufzeit eines Heartbeats hin und zurück; -1, solange keine Antwort kam.
    private Int64 _roundTrip = -1;
    private volatile Boolean _monitoring;
    private Boolean _disposed;

    /// <summary>Abstand, in dem <see cref="MonitorAsync"/> einen Heartbeat schickt.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    /// <summary>So lange darf die Gegenseite schweigen, bevor sie als weg gilt.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(3);

    // Nutzlast eines Heartbeats: leer bei älteren Gegenseiten, sonst Kennung und Stopwatch-Zeitstempel des Absenders.
    private const Byte HeartbeatRequest = 0;
    private const Byte HeartbeatEcho = 1;
    private const Int32 HeartbeatPayloadLength = 1 + sizeof(Int64);

    /// <summary>Zähler für die Diagnose: Frames in beide Richtungen und die letzte Laufzeit hin und zurück.</summary>
    public ConnectionStats Stats
    {
        get
        {
            Int64 roundTrip = Interlocked.Read(ref _roundTrip);
            return new ConnectionStats(Interlocked.Read(ref _sentFrames), Interlocked.Read(ref _receivedFrames),
                roundTrip < 0 ? null : Stopwatch.GetElapsedTime(0, roundTrip));
        }
    }

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
        Socket socket = client.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        return new MessageConnection(client.GetStream(), client);
    }

    /// <summary>
    /// Ab jetzt geht jeder Frame verschlüsselt hinaus und muss verschlüsselt ankommen. Frames, die schon im
    /// Empfangspuffer liegen, werden erst beim Ausliefern entschlüsselt; deshalb darf die Gegenseite direkt nach
    /// ihrem letzten Anmeldeschritt verschlüsselt senden. Die Schlüssel kommen aus der <see cref="Handshake"/>.
    /// </summary>
    public void EnableEncryption(ReadOnlySpan<Byte> sendKey, ReadOnlySpan<Byte> receiveKey)
    {
        _sendCipher = new FrameCipher(sendKey);
        _receiveCipher = new FrameCipher(receiveKey);
    }

    public async Task SendAsync(MessageType type, ReadOnlyMemory<Byte> payload, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            // Unter dem Lock, weil der Zähler der Nonce in derselben Reihenfolge laufen muss wie die Frames.
            Byte[] frame = _sendCipher is null
                ? FrameCodec.Write(type, payload.Span)
                : FrameCodec.Write(MessageType.Encrypted, _sendCipher.Seal(type, payload.Span));
            await _stream.WriteAsync(frame, ct);
            Interlocked.Increment(ref _sentFrames);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async IAsyncEnumerable<(MessageType Type, Byte[] Payload)> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Frames, die beim vorigen Lesen schon mitkamen (etwa direkt nach der Anmeldung), zuerst ausliefern.
        while (_reader.TryRead(out MessageType bufferedType, out Byte[] bufferedPayload))
        {
            yield return Received(Open(bufferedType, bufferedPayload), ct);
        }

        Byte[] buffer = new Byte[4096];
        while (!ct.IsCancellationRequested)
        {
            Int32 n = await _stream.ReadAsync(buffer, ct);
            if (n == 0)
            {
                yield break;
            }

            Volatile.Write(ref _lastReceived, Environment.TickCount64);

            _reader.Append(buffer.AsSpan(0, n));
            while (_reader.TryRead(out MessageType type, out Byte[] payload))
            {
                yield return Received(Open(type, payload), ct);
            }
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
    ///
    /// <para>Jeder Heartbeat trägt einen Zeitstempel; läuft auf der Gegenseite ebenfalls <see cref="MonitorAsync"/>,
    /// schickt sie ihn zurück, und daraus ergibt sich die Laufzeit in <see cref="Stats"/>.</para>
    /// </summary>
    public async Task<Boolean> MonitorAsync(CancellationToken ct)
    {
        _monitoring = true;
        Volatile.Write(ref _lastReceived, Environment.TickCount64);
        Task? pending = null;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return false; }

            if (Environment.TickCount64 - Volatile.Read(ref _lastReceived) > HeartbeatTimeout.TotalMilliseconds)
            {
                return true;
            }

            // Nicht auf das Senden warten: Bei totem Gegenüber kann es im vollen Puffer hängen, und die
            // Prüfung oben muss trotzdem laufen. Höchstens ein Heartbeat zugleich unterwegs.
            if (pending is null || pending.IsCompleted)
            {
                pending = SendHeartbeatAsync(HeartbeatRequest, Stopwatch.GetTimestamp(), ct);
            }
        }
        return false;
    }

    /// <summary>
    /// Zählt den Frame und wertet Heartbeats mit Zeitstempel aus: eine Anfrage geht zurück, eine Antwort ergibt die
    /// Laufzeit. Der Heartbeat selbst kommt trotzdem beim Aufrufer an. Nur wer selbst überwacht, antwortet; eine Seite,
    /// die bloß liest, soll für die Gegenseite weiter schweigen.
    /// </summary>
    private (MessageType Type, Byte[] Payload) Received((MessageType Type, Byte[] Payload) frame, CancellationToken ct)
    {
        Interlocked.Increment(ref _receivedFrames);
        if (frame.Type == MessageType.Heartbeat && frame.Payload.Length == HeartbeatPayloadLength)
        {
            Int64 sent = BinaryPrimitives.ReadInt64LittleEndian(frame.Payload.AsSpan(1));
            if (frame.Payload[0] == HeartbeatRequest && _monitoring)
            {
                _ = SendHeartbeatAsync(HeartbeatEcho, sent, ct);
            }
            else if (frame.Payload[0] == HeartbeatEcho)
            {
                Interlocked.Exchange(ref _roundTrip, Stopwatch.GetTimestamp() - sent);
            }
        }
        return frame;
    }

    /// <summary>Ohne Verschlüsselung unverändert; mit ihr wird ein Klartext-Frame nicht angenommen.</summary>
    private (MessageType Type, Byte[] Payload) Open(MessageType type, Byte[] payload)
    {
        if (_receiveCipher is null)
        {
            return (type, payload);
        }

        if (type != MessageType.Encrypted)
        {
            throw new IOException($"Unverschlüsselter Frame ({type}) nach der Anmeldung.");
        }

        return _receiveCipher.Open(payload);
    }

    private async Task SendHeartbeatAsync(Byte kind, Int64 timestamp, CancellationToken ct)
    {
        Byte[] payload = new Byte[HeartbeatPayloadLength];
        payload[0] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), timestamp);
        try
        {
            await SendAsync(MessageType.Heartbeat, payload, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Ein Sendefehler zeigt sich auch auf dem Empfangsweg; das Schweigen erkennt MonitorAsync.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        if (!ReferenceEquals(_owner, _stream))
        {
            _owner?.Dispose();
        }

        _sendLock.Dispose();
        _sendCipher?.Dispose();
        _receiveCipher?.Dispose();
    }
}
