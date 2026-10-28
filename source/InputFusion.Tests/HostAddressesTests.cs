using System.Net;
using NC2.InputFusion.Core.Net;

namespace NC2.InputFusion.Tests;

public class HostAddressesTests
{
    [Fact]
    public void Order_IPv4ThenFixedThenIPv6_WithoutDuplicates()
    {
        IPAddress[] resolved =
        [
            IPAddress.Parse("fe80::1"),
            IPAddress.Parse("2a0c:d242::1"),
            IPAddress.Parse("192.168.1.10"),
        ];

        IReadOnlyList<IPAddress> order = HostAddresses.Order(resolved, [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("192.168.1.10")]);

        Assert.Equal(["192.168.1.10", "192.168.1.20", "fe80::1", "2a0c:d242::1"], order.Select(a => a.ToString()));
    }

    [Fact]
    public void Order_MapsIPv4MappedAddresses()
    {
        IReadOnlyList<IPAddress> order = HostAddresses.Order([IPAddress.Parse("::ffff:192.168.1.10")], []);

        Assert.Equal(IPAddress.Parse("192.168.1.10"), Assert.Single(order));
    }

    [Theory]
    [InlineData("::ffff:192.168.1.10", "192.168.1.10")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("fdb8::1", "fdb8::1")]
    public void Display_ShowsIPv4WithoutMapping(String address, String expected) =>
        Assert.Equal(expected, HostAddresses.Display(new IPEndPoint(IPAddress.Parse(address), 15111)));

    [Fact]
    public void Display_WithoutEndPoint() => Assert.Equal("?", HostAddresses.Display(null));
}
