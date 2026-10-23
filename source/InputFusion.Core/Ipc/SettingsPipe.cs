using System.ComponentModel;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Named Pipe zwischen den Einstellungen und dem Agent auf dem PC. Der Agent hostet, die Einstellungen
/// verbinden sich. Der Name enthält die SID des Benutzers, damit bei mehreren angemeldeten Benutzern jeder
/// seinen eigenen Agent erreicht.
///
/// <para>Der Agent läuft erhöht, die Einstellungen aus dem Startmenü nicht. Deshalb trägt die Pipe ausdrücklich
/// die Integritätsstufe Mittel; die ACL lässt nur den Benutzer selbst und SYSTEM zu.</para>
/// </summary>
public static unsafe class SettingsPipe
{
    private const uint BufferSize = 4096;

    public static string Name { get; } = "InputFusion.Settings." + CurrentUser().Value;

    /// <summary>
    /// Direkt über <c>CreateNamedPipe</c> statt über <see cref="PipeSecurity"/>: Ein Integritäts-Label in dessen
    /// SACL verlangt beim Anlegen SeSecurityPrivilege (gemessen), der Security-Descriptor aus SDDL nicht.
    /// </summary>
    public static NamedPipeServerStream CreateServer()
    {
        // D: nur Benutzer und SYSTEM, ohne Vererbung; S: Mandatory Label Mittel, No-Write-Up.
        string sddl = $"D:P(A;;GA;;;{CurrentUser().Value})(A;;GA;;;SY)S:(ML;;NW;;;ME)";
        if (!PInvoke.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, PInvoke.SDDL_REVISION_1, out PSECURITY_DESCRIPTOR descriptor))
        {
            throw new Win32Exception();
        }

        try
        {
            SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = descriptor.Value,
            };
            HANDLE handle;
            fixed (char* name = @"\\.\pipe\" + Name)
            {
                handle = PInvoke.CreateNamedPipe(
                    name,
                    FILE_FLAGS_AND_ATTRIBUTES.PIPE_ACCESS_DUPLEX | FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                    NAMED_PIPE_MODE.PIPE_TYPE_BYTE | NAMED_PIPE_MODE.PIPE_READMODE_BYTE | NAMED_PIPE_MODE.PIPE_WAIT
                        | NAMED_PIPE_MODE.PIPE_REJECT_REMOTE_CLIENTS,
                    PInvoke.PIPE_UNLIMITED_INSTANCES,
                    BufferSize,
                    BufferSize,
                    0,
                    &attributes);
            }
            if ((nint)handle.Value == -1)
            {
                throw new Win32Exception();
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, new SafePipeHandle((nint)handle.Value, ownsHandle: true));
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)descriptor.Value);
        }
    }

    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);

    private static SecurityIdentifier CurrentUser() =>
        WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Prozess ohne Benutzer-SID");
}
