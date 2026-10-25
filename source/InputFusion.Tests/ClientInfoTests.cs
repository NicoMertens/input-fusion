using System.Text;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class ClientInfoTests
{
    [Fact]
    public void RoundTrip()
    {
        ClientInfo info = new ClientInfo("NOTEBOOK", "Windows 11 Pro 24H2 (Build 26100.4061)", "nico");

        Assert.Equal(info, ClientInfo.FromPayload(info.ToPayload()));
    }

    [Fact]
    public void MissingAndUnknownFields_AreTolerated()
    {
        // Eine spätere Version darf Felder ergänzen, eine frühere welche weglassen.
        ClientInfo? info = ClientInfo.FromPayload(Encoding.UTF8.GetBytes("""{"machineName":"NOTEBOOK","battery":80}"""));

        Assert.Equal(new ClientInfo("NOTEBOOK", null, null), info);
    }

    [Fact]
    public void Garbage_IsNull()
    {
        Assert.Null(ClientInfo.FromPayload([0xFF, 0x00]));
    }
}
