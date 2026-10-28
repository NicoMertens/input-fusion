using InputFusion.Core.Config;
using InputFusion.Core.Net;

namespace InputFusion.Tests;

public class PortsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(15111)]
    [InlineData(65535)]
    public void Validate_AcceptsPortsInRange(Int32 port)
    {
        Assert.Null(Ports.Validate(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_RejectsPortsOutOfRange(Int32 port)
    {
        Assert.NotNull(Ports.Validate(port));
    }

    [Fact]
    public void ValidateText_ParsesPort()
    {
        Assert.Null(Ports.Validate("15200", out Int32 port));
        Assert.Equal(15200, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData(" 15111")]
    [InlineData("70000")]
    public void ValidateText_RejectsNonPorts(String text)
    {
        Assert.NotNull(Ports.Validate(text, out _));
    }

    [Fact]
    public void NewHostConfig_UsesDefaultPort()
    {
        Assert.Equal(Ports.Input, new HostConfig().Port);
    }
}
