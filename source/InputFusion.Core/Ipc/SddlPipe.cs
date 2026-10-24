using System.ComponentModel;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Pipes;

namespace InputFusion.Core.Ipc;

/// <summary>
/// Legt einen Pipe-Server mit einem Security-Descriptor aus SDDL an, nur lokal erreichbar, beliebig viele Exemplare.
/// Direkt über <c>CreateNamedPipe</c> statt über <see cref="PipeSecurity"/>: Ein Integritäts-Label in dessen SACL
/// verlangt beim Anlegen SeSecurityPrivilege (gemessen), der Security-Descriptor aus SDDL nicht.
/// </summary>
internal static unsafe class SddlPipe
{
    private const uint BufferSize = 4096;

    public static NamedPipeServerStream CreateServer(string name, string sddl)
    {
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
            fixed (char* fullName = @"\\.\pipe\" + name)
            {
                handle = PInvoke.CreateNamedPipe(
                    fullName,
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
}
