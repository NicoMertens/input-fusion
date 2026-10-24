using InputFusion.Core.Config;

namespace InputFusion.Tests;

public class ClientConfigRulesTests
{
    private const string Key = "abcdefghijkmnpqr";

    [Theory]
    [InlineData("PC", null, 15111)]
    [InlineData("PC", "", 15111)]
    [InlineData("pc.fritz.box", "192.168.1.10", 1)]
    [InlineData("PC", "fe80::1", 65535)]
    public void Accepts(string host, string? ip, int port) =>
        Assert.Null(ClientConfigRules.Validate(host, ip, port, Key));

    [Theory]
    [InlineData("", null, 15111, Key)]
    [InlineData(" PC", null, 15111, Key)]
    [InlineData("P C", null, 15111, Key)]
    [InlineData("PC", "192.168.1", 15111, Key)]
    [InlineData("PC", null, 0, Key)]
    [InlineData("PC", null, 65536, Key)]
    [InlineData("PC", null, 15111, "zu kurz")]
    public void Rejects(string host, string? ip, int port, string key) =>
        Assert.NotNull(ClientConfigRules.Validate(host, ip, port, key));
}
