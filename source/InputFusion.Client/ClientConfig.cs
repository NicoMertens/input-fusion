using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputFusion.Client;

/// <summary>
/// Konfiguration des Dienstes in <c>%ProgramData%\InputFusion\client.json</c>, ACL nur für SYSTEM und
/// Administratoren (setzt das Installationsskript). <see cref="Host"/> ist der Rechnername des PCs, per DNS
/// aufgelöst; <see cref="Ip"/> eine optionale feste Adresse, die zusätzlich versucht wird. <see cref="Key"/> ist
/// der gemeinsame Schlüssel aus den Einstellungen des Hosts, siehe <c>Handshake</c>.
/// </summary>
public sealed record ClientConfig(string Host, string Key, string? Ip = null)
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InputFusion", "client.json");

    /// <summary>Liest die Datei. Der Dienst ruft das vor jedem Verbindungsversuch auf, damit eine Änderung
    /// ohne Neustart greift. Bei Fehler null und eine Meldung in <paramref name="error"/>.</summary>
    public static ClientConfig? Load(out string? error)
    {
        try
        {
            using FileStream stream = File.OpenRead(FilePath);
            ClientConfig? config = JsonSerializer.Deserialize(stream, ClientConfigJson.Default.ClientConfig);
            if (config is null || string.IsNullOrWhiteSpace(config.Host))
            {
                error = $"{FilePath}: \"host\" fehlt";
                return null;
            }
            if (string.IsNullOrEmpty(config.Key))
            {
                error = $"{FilePath}: \"key\" fehlt";
                return null;
            }
            error = null;
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"{FilePath}: {ex.Message}";
            return null;
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ClientConfig))]
internal sealed partial class ClientConfigJson : JsonSerializerContext;
