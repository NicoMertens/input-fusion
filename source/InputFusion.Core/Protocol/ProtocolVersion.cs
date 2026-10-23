using System.Reflection;

namespace InputFusion.Core.Protocol;

/// <summary>
/// Version des Netzwerkprotokolls zwischen Host und Client. Bei jeder Änderung, die eine ältere Gegenseite nicht
/// versteht (neue Pflichtnachricht, geänderte Nutzlast), um eins erhöhen; sonst weisen sich Host und Client nach
/// einem Update der einen Seite nicht ab, sondern missverstehen sich.
/// </summary>
public static class ProtocolVersion
{
    public const ushort Current = 1;
}

/// <summary>Die Version der laufenden App aus <c>Directory.Build.props</c>, ohne den Commit-Anhang.</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        string? informational = (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0";
        }

        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
