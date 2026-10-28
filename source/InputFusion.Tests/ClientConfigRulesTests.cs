using NC2.InputFusion.Core.Config;

namespace NC2.InputFusion.Tests;

public class ClientConfigRulesTests
{
    private const String Key = "abcdefghijkmnpqr";

    [Theory]
    [InlineData("PC", null, 15111)]
    [InlineData("PC", "", 15111)]
    [InlineData("pc.fritz.box", "192.168.1.10", 1)]
    [InlineData("PC", "fe80::1", 65535)]
    public void Accepts(String host, String? ip, Int32 port) =>
        Assert.Null(ClientConfigRules.Validate(host, ip, port, Key));

    [Theory]
    [InlineData("", null, 15111, Key)]
    [InlineData(" PC", null, 15111, Key)]
    [InlineData("P C", null, 15111, Key)]
    [InlineData("PC", "192.168.1", 15111, Key)]
    [InlineData("PC", null, 0, Key)]
    [InlineData("PC", null, 65536, Key)]
    [InlineData("PC", null, 15111, "zu kurz")]
    public void Rejects(String host, String? ip, Int32 port, String key) =>
        Assert.NotNull(ClientConfigRules.Validate(host, ip, port, key));

    [Fact]
    public void List_NeedsOneHost() =>
        Assert.Equal("Bitte mindestens einen PC eintragen.", ClientConfigRules.Validate([]));

    [Fact]
    public void List_NamesFaultyHost()
    {
        Assert.Null(ClientConfigRules.Validate([new HostEntry("PC", Key), new HostEntry("BUERO-PC", Key)]));
        Assert.Equal("Bitte den PC-Namen des Hosts angeben.", ClientConfigRules.Validate([new HostEntry("", Key)]));
        Assert.Equal("2. PC: Bitte den PC-Namen des Hosts angeben.", ClientConfigRules.Validate([new HostEntry("PC", Key), new HostEntry("", Key)]));
    }
}
