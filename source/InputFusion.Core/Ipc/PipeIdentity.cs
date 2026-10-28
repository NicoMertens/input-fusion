using System.IO.Pipes;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace NC2.InputFusion.Core.Ipc;

/// <summary>
/// Prüft die Gegenseite einer Pipe über das Token ihres Prozesses. Eine ACL schützt nur die Pipe des echten Servers;
/// legt ein anderer Prozess eine Pipe gleichen Namens an, verbände sich der Client sonst mit ihm. Umgekehrt sagt die
/// ACL dem Server nicht, ob ein Client erhöht läuft.
/// </summary>
public static unsafe class PipeIdentity
{
    public static SecurityIdentifier LocalSystem { get; } = new(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Prüft, ob der Server unter einem der erlaubten Konten läuft. Ist sein Konto nicht zu ermitteln, gilt er als fremd.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Der Server läuft unter keinem der erlaubten Konten.</exception>
    public static void VerifyServer(NamedPipeClientStream pipe, params SecurityIdentifier?[] allowed)
    {
        UInt32 pid;
        SecurityIdentifier? server = null;
        if (PInvoke.GetNamedPipeServerProcessId((HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), &pid))
        {
            using WindowsIdentity? identity = ProcessIdentity.Open(pid);
            server = identity?.User;
        }

        if (server is null || !allowed.Contains(server))
        {
            throw new UnauthorizedAccessException(
                $"Server der Pipe (PID {pid}) läuft als {server?.Value ?? "unbekannt"}, erlaubt: {String.Join(", ", allowed.Select(sid => sid?.Value))}.");
        }
    }

    /// <summary>
    /// Ob der verbundene Client als SYSTEM oder als erhöhter Administrator läuft. Ein nicht erhöhter Administrator
    /// zählt nicht: In seinem Token ist die Gruppe nur zum Verweigern gesetzt.
    /// </summary>
    public static Boolean IsClientAdministrator(NamedPipeServerStream pipe)
    {
        UInt32 pid;
        if (!PInvoke.GetNamedPipeClientProcessId((HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), &pid))
        {
            return false;
        }

        using WindowsIdentity? identity = ProcessIdentity.Open(pid);
        return identity is not null
            && (identity.User == LocalSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
    }

    /// <summary>Die Sitzung des verbundenen Clients, null, wenn sie nicht zu ermitteln ist.</summary>
    public static UInt32? ClientSessionId(NamedPipeServerStream pipe)
    {
        UInt32 session;
        return PInvoke.GetNamedPipeClientSessionId((HANDLE)pipe.SafePipeHandle.DangerousGetHandle(), &session) ? session : null;
    }

    /// <summary>Das Konto des aufrufenden Prozesses.</summary>
    public static SecurityIdentifier? CurrentUser() => WindowsIdentity.GetCurrent().User;
}
