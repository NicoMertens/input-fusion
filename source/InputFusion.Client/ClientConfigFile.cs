using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using InputFusion.Core.Config;

namespace InputFusion.Client;

/// <summary>
/// Die <see cref="ClientConfig"/> des Dienstes in <c>%ProgramData%\InputFusion\client.json</c>, ACL nur für SYSTEM und
/// Administratoren. Geschrieben wird sie vom Setup und vom Dienst, wenn ein Administrator sie in den Einstellungen
/// ändert oder ein Host einen neuen Schlüssel schickt.
/// </summary>
internal static class ClientConfigFile
{
    public static String FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InputFusion", "client.json");

    /// <summary>
    /// Schreibt die Datei. Sie entsteht gleich mit ihrer ACL (nur SYSTEM und Administratoren, ohne Vererbung);
    /// nachträglich gesetzt wäre sie kurz für alle Benutzer lesbar, weil ProgramData Lesen erbt. Sie enthält die Schlüssel.
    /// Bei Fehler die Meldung, sonst null.
    /// </summary>
    public static String? Save(ClientConfig config)
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
            stream.Write(config.ToJson());
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{FilePath}: {ex.Message}";
        }
    }

    /// <summary>Liest die Datei. Der Dienst ruft das vor jedem Verbindungsversuch auf, damit eine Änderung
    /// ohne Neustart greift. Bei Fehler null und eine Meldung in <paramref name="error"/>.</summary>
    public static ClientConfig? Load(out String? error)
    {
        try
        {
            ClientConfig? config = ClientConfig.Parse(File.ReadAllBytes(FilePath), out error);
            error = error is null ? null : $"{FilePath}: {error}";
            return config;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"{FilePath}: {ex.Message}";
            return null;
        }
    }
}
