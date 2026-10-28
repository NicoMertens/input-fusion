using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using InputFusion.Core.Net;

namespace InputFusion.Core.Config;

/// <summary>
/// Konfiguration des Agents in <c>%LOCALAPPDATA%\InputFusion\config.json</c>. Der Agent ist ihr einziger Besitzer;
/// die Einstellungen ändern sie nur über die Pipe. Liegt in Core, damit der Simulator beim Entwickeln auf
/// demselben Rechner den Schlüssel von dort nehmen kann.
/// </summary>
public sealed class HostConfig
{
    public const Int32 MinKeyLength = 16;
    public const Int32 GeneratedKeyLength = 24;

    // Ohne leicht verwechselbare Zeichen (0/O, 1/l/I), weil der Schlüssel von Hand aufs Notebook übertragen wird.
    private const String KeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static String FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputFusion", "config.json");

    public String Key { get; set; } = "";

    /// <summary>Port, auf dem der Agent lauscht. Zu ändern in den Einstellungen; von Hand in der Datei nur bei beendetem
    /// Agent, weil er beim nächsten Speichern seinen Stand zurückschreibt.</summary>
    public Int32 Port { get; set; } = Ports.Input;

    /// <summary>Gespeicherte Anordnungen je Monitor-Kombination, siehe <c>MonitorLayout.CombinationKey</c>.</summary>
    public List<SavedLayout> Layouts { get; set; } = [];

    public static String GenerateKey() => new(RandomNumberGenerator.GetItems<Char>(KeyAlphabet, GeneratedKeyLength));

    /// <summary>Null, wenn der Schlüssel taugt, sonst der Grund.</summary>
    public static String? ValidateKey(String key)
    {
        if (key.Length < MinKeyLength)
        {
            return $"Der Schlüssel braucht mindestens {MinKeyLength} Zeichen.";
        }

        if (key.Trim().Length != key.Length)
        {
            return "Der Schlüssel darf nicht mit Leerzeichen beginnen oder enden.";
        }

        if (key.Any(Char.IsControl))
        {
            return "Der Schlüssel darf keine Steuerzeichen enthalten.";
        }

        return null;
    }

    /// <summary>Liest die Datei, ohne sie anzulegen. Null, wenn es sie nicht gibt oder sie unlesbar ist.</summary>
    public static HostConfig? TryLoad()
    {
        try
        {
            using FileStream stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, HostConfigJson.Default.HostConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Liest die Datei. Fehlt sie oder taugt der Schlüssel nicht, wird ein neuer erzeugt und gespeichert;
    /// ein ungültiger Port fällt auf den Standard zurück. Steht in der Datei noch kein Port, kommt er beim nächsten Speichern
    /// hinzu, damit man ihn dort findet.</summary>
    public static HostConfig LoadOrCreate()
    {
        HostConfig config = TryLoad() ?? new HostConfig();
        Boolean changed = false;
        if (ValidateKey(config.Key) is not null)
        {
            config.Key = GenerateKey();
            changed = true;
        }

        if (Ports.Validate(config.Port) is not null)
        {
            config.Port = Ports.Input;
            changed = true;
        }

        if (changed)
        {
            config.Save();
        }
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Erst in eine Nachbardatei, dann ersetzen: Ein Absturz mitten im Schreiben ließe sonst eine halbe Datei zurück.
        String temp = FilePath + ".tmp";
        using (FileStream stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, this, HostConfigJson.Default.HostConfig);
        }

        File.Move(temp, FilePath, overwrite: true);
    }
}

public sealed record SavedLayout(String Combination, Int32 OffsetX, Int32 OffsetY);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(HostConfig))]
internal sealed partial class HostConfigJson : JsonSerializerContext;
