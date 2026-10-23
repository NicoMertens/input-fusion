using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputFusion.Core.Config;

/// <summary>
/// Konfiguration des Agents in <c>%LOCALAPPDATA%\InputFusion\config.json</c>. Der Agent ist ihr einziger Besitzer;
/// die Einstellungen ändern sie nur über die Pipe. Liegt in Core, damit der Simulator beim Entwickeln auf
/// demselben Rechner den Schlüssel von dort nehmen kann.
/// </summary>
public sealed class HostConfig
{
    public const int MinKeyLength = 16;
    public const int GeneratedKeyLength = 24;

    // Ohne leicht verwechselbare Zeichen (0/O, 1/l/I), weil der Schlüssel von Hand aufs Notebook übertragen wird.
    private const string KeyAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InputFusion", "config.json");

    public string Key { get; set; } = "";

    /// <summary>Gespeicherte Anordnungen je Monitor-Kombination, siehe <c>MonitorLayout.CombinationKey</c>.</summary>
    public List<SavedLayout> Layouts { get; set; } = [];

    public static string GenerateKey() => new(RandomNumberGenerator.GetItems<char>(KeyAlphabet, GeneratedKeyLength));

    /// <summary>Null, wenn der Schlüssel taugt, sonst der Grund.</summary>
    public static string? ValidateKey(string key)
    {
        if (key.Length < MinKeyLength)
            return $"Der Schlüssel braucht mindestens {MinKeyLength} Zeichen.";
        if (key.Trim().Length != key.Length)
            return "Der Schlüssel darf nicht mit Leerzeichen beginnen oder enden.";
        if (key.Any(char.IsControl))
            return "Der Schlüssel darf keine Steuerzeichen enthalten.";
        return null;
    }

    /// <summary>Liest die Datei, ohne sie anzulegen. Null, wenn es sie nicht gibt oder sie unlesbar ist.</summary>
    public static HostConfig? TryLoad()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, HostConfigJson.Default.HostConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Liest die Datei. Fehlt sie oder taugt der Schlüssel nicht, wird ein neuer erzeugt und gespeichert.</summary>
    public static HostConfig LoadOrCreate()
    {
        var config = TryLoad() ?? new HostConfig();
        if (ValidateKey(config.Key) is not null)
        {
            config.Key = GenerateKey();
            config.Save();
        }
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Erst in eine Nachbardatei, dann ersetzen: Ein Absturz mitten im Schreiben ließe sonst eine halbe Datei zurück.
        string temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, this, HostConfigJson.Default.HostConfig);
        File.Move(temp, FilePath, overwrite: true);
    }
}

public sealed record SavedLayout(string Combination, int OffsetX, int OffsetY);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(HostConfig))]
internal sealed partial class HostConfigJson : JsonSerializerContext;
