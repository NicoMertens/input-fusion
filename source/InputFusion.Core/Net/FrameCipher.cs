using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using NC2.InputFusion.Core.Protocol;

namespace NC2.InputFusion.Core.Net;

/// <summary>
/// Verschlüsselt die Frames einer Richtung mit AES-GCM. Der Typ wandert mit in den verschlüsselten Teil, außen
/// steht nur noch <see cref="MessageType.Encrypted"/>. Die Nonce ist ein Zähler, der nicht übertragen wird: Beide
/// Seiten zählen mit, ein fehlender, doppelter oder umsortierter Frame fällt deshalb wie ein veränderter an der
/// Prüfsumme auf. Jede Richtung hat einen eigenen Schlüssel, so kann sich eine Nonce nie wiederholen.
/// Nicht threadsicher: Senden unter dem Sende-Lock, Empfangen nur aus einer Schleife.
/// </summary>
public sealed class FrameCipher : IDisposable
{
    public const Int32 KeySize = 32;
    public const Int32 TagSize = 16;

    /// <summary>Größte Nutzlast, die verschlüsselt samt Typ und Prüfsumme noch in einen Frame passt.</summary>
    public const Int32 MaxPayload = FrameCodec.MaxPayload - 1 - TagSize;

    private const Int32 NonceSize = 12;

    private readonly AesGcm _aes;
    private UInt64 _counter;

    public FrameCipher(ReadOnlySpan<Byte> key) => _aes = new AesGcm(key, TagSize);

    /// <summary>Typ und Nutzlast verschlüsselt, dahinter die Prüfsumme.</summary>
    public Byte[] Seal(MessageType type, ReadOnlySpan<Byte> payload)
    {
        if (payload.Length > MaxPayload)
        {
            throw new ArgumentException($"Nutzlast über {MaxPayload} Bytes", nameof(payload));
        }

        Byte[] plain = [(Byte)type, .. payload];
        Byte[] sealedFrame = new Byte[plain.Length + TagSize];
        _aes.Encrypt(NextNonce(), plain, sealedFrame.AsSpan(0, plain.Length), sealedFrame.AsSpan(plain.Length));
        return sealedFrame;
    }

    /// <summary>Entschlüsselt und prüft. Passt die Prüfsumme nicht, ist die Verbindung nicht mehr zu gebrauchen:
    /// <see cref="IOException"/>, wie bei jedem anderen Abbruch.</summary>
    public (MessageType Type, Byte[] Payload) Open(ReadOnlySpan<Byte> sealedFrame)
    {
        if (sealedFrame.Length < 1 + TagSize)
        {
            throw new IOException("Verschlüsselter Frame zu kurz.");
        }

        Int32 length = sealedFrame.Length - TagSize;
        Byte[] plain = new Byte[length];
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

    private Byte[] NextNonce()
    {
        Byte[] nonce = new Byte[NonceSize];
        BinaryPrimitives.WriteUInt64LittleEndian(nonce, _counter++);
        return nonce;
    }
}
