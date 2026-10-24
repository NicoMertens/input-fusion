using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using InputFusion.Core.Net;

namespace InputFusion.Client;

/// <summary>
/// Konfiguration des Dienstes in <c>%ProgramData%\InputFusion\client.json</c>, ACL nur für SYSTEM und
/// Administratoren (setzt das Installationsskript). <see cref="Host"/> ist der Rechnername des PCs, per DNS
/// aufgelöst; <see cref="Ip"/> eine optionale feste Adresse, die zusätzlich versucht wird. <see cref="Key"/> ist
/// der gemeinsame Schlüssel aus den Einstellungen des Hosts, siehe <c>Handshake</c>. <see cref="Port"/> muss zu dem
/// des Hosts passen; fehlt er in der Datei, gilt der Standard. Geschrieben wird sie vom Setup und vom Dienst, wenn ein
/// Administrator sie in den Einstellungen ändert.
/// </summary>
public sealed record ClientConfig(string Host, string Key, string? Ip = null, int Port = Ports.Input)
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InputFusion", "client.json");

    /// <summary>
    /// Schreibt die Datei. Sie entsteht gleich mit ihrer ACL (nur SYSTEM und Administratoren, ohne Vererbung);
    /// nachträglich gesetzt wäre sie kurz für alle Benutzer lesbar, weil ProgramData Lesen erbt. Sie enthält den Schlüssel.
    /// Bei Fehler die Meldung, sonst null.
    /// </summary>
    public static string? Save(ClientConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            FileSecurity security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            {
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            }

            File.Delete(FilePath);
            using FileStream stream = new FileInfo(FilePath).Create(
                FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
            JsonSerializer.Serialize(stream, config, ClientConfigJson.Default.ClientConfig);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{FilePath}: {ex.Message}";
        }
    }

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
            if (Ports.Validate(config.Port) is { } portError)
            {
                error = $"{FilePath}: \"port\": {portError}";
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
