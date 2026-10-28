using System.Reflection;

namespace NC2.InputFusion.Core.Protocol;

/// <summary>
/// Version des Netzwerkprotokolls zwischen Host und Client. Bei jeder Änderung, die eine ältere Gegenseite nicht
/// versteht (neue Pflichtnachricht, geänderte Nutzlast), um eins erhöhen; sonst weisen sich Host und Client nach
/// einem Update der einen Seite nicht ab, sondern missverstehen sich.
/// </summary>
public static class ProtocolVersion
{
    // 2: Anmeldung mit ECDH, danach alles verschlüsselt (siehe Handshake).
    public const UInt16 Current = 2;
}

/// <summary>Die Version der laufenden App aus <c>Directory.Build.props</c>, ohne den Commit-Anhang.</summary>
public static class AppVersion
{
    public static String Current { get; } = Read();

    private static String Read()
    {
        String? informational = (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (String.IsNullOrEmpty(informational))
        {
            return "0.0.0";
        }

        Int32 plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
