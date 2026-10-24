using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using InputFusion.Core.Protocol;

namespace InputFusion.Core.Net;

/// <summary>
/// Verschlüsselt die Frames einer Richtung mit AES-GCM. Der Typ wandert mit in den verschlüsselten Teil, außen
/// steht nur noch <see cref="MessageType.Encrypted"/>. Die Nonce ist ein Zähler, der nicht übertragen wird: Beide
/// Seiten zählen mit, ein fehlender, doppelter oder umsortierter Frame fällt deshalb wie ein veränderter an der
/// Prüfsumme auf. Jede Richtung hat einen eigenen Schlüssel, so kann sich eine Nonce nie wiederholen.
/// Nicht threadsicher: Senden unter dem Sende-Lock, Empfangen nur aus einer Schleife.
/// </summary>
public sealed class FrameCipher : IDisposable
{
    public const int KeySize = 32;
    public const int TagSize = 16;

    /// <summary>Größte Nutzlast, die verschlüsselt samt Typ und Prüfsumme noch in einen Frame passt.</summary>
    public const int MaxPayload = FrameCodec.MaxPayload - 1 - TagSize;

    private const int NonceSize = 12;

    private readonly AesGcm _aes;
    private ulong _counter;

    public FrameCipher(ReadOnlySpan<byte> key) => _aes = new AesGcm(key, TagSize);

    /// <summary>Typ und Nutzlast verschlüsselt, dahinter die Prüfsumme.</summary>
    public byte[] Seal(MessageType type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"Nutzlast über {MaxPayload} Bytes", nameof(payload));
        }

        byte[] plain = [(byte)type, .. payload];
        byte[] sealedFrame = new byte[plain.Length + TagSize];
        _aes.Encrypt(NextNonce(), plain, sealedFrame.AsSpan(0, plain.Length), sealedFrame.AsSpan(plain.Length));
        return sealedFrame;
    }

    /// <summary>Entschlüsselt und prüft. Passt die Prüfsumme nicht, ist die Verbindung nicht mehr zu gebrauchen:
    /// <see cref="IOException"/>, wie bei jedem anderen Abbruch.</summary>
    public (MessageType Type, byte[] Payload) Open(ReadOnlySpan<byte> sealedFrame)
    {
        if (sealedFrame.Length < 1 + TagSize)
        {
            throw new IOException("Verschlüsselter Frame zu kurz.");
        }

        int length = sealedFrame.Length - TagSize;
        byte[] plain = new byte[length];
        try
        {
            _aes.Decrypt(NextNonce(), sealedFrame[..length], sealedFrame[length..], plain);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new IOException("Verschlüsselter Frame verändert, wiederholt oder ausgelassen.", ex);
        }

        return ((MessageType)plain[0], plain[1..]);
    }

    public void Dispose() => _aes.Dispose();

    private byte[] NextNonce()
    {
        byte[] nonce = new byte[NonceSize];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, _counter++);
        return nonce;
    }
}
