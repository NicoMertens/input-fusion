using System.Text;
using InputFusion.Core.Config;

namespace InputFusion.Tests;

public class ClientConfigTests
{
    private const String Key = "abcdefghijkmnpqr";

    private static ClientConfig? Parse(String json, out String? error) => ClientConfig.Parse(Encoding.UTF8.GetBytes(json), out error);

    [Fact]
    public void Parses_HostList()
    {
        ClientConfig? config = Parse("""
            {
              // zu Hause und im Büro
              "hosts": [
                { "host": "PC", "key": "abcdefghijkmnpqr", "ip": "192.168.1.10" },
                { "host": "BUERO-PC", "key": "zyxwvutsrqpnmkjh", "port": 15112 },
              ]
            }
            """, out String? error);

        Assert.Null(error);
        Assert.Equal([new HostEntry("PC", Key, "192.168.1.10"), new HostEntry("BUERO-PC", "zyxwvutsrqpnmkjh", Port: 15112)], config!.Hosts);
    }

    [Fact]
    public void Parses_SingleHostUpTo011()
    {
        ClientConfig? config = Parse("""{"host":"PC","key":"abcdefghijkmnpqr","ip":"192.168.1.10","port":15112}""", out String? error);

        Assert.Null(error);
        Assert.Equal(new HostEntry("PC", Key, "192.168.1.10", 15112), Assert.Single(config!.Hosts));
    }

    [Theory]
    [InlineData("""{}""", "\"hosts\" fehlt oder ist leer")]
    [InlineData("""{"hosts":[]}""", "\"hosts\" fehlt oder ist leer")]
    [InlineData("""{"key":"abcdefghijkmnpqr"}""", "\"hosts\" fehlt oder ist leer")]
    [InlineData("""{"host":"PC"}""", "hosts[0]: \"key\" fehlt")]
    [InlineData("""{"hosts":[{"host":"PC","key":"abcdefghijkmnpqr"},{"host":" ","key":"abcdefghijkmnpqr"}]}""", "hosts[1]: \"host\" fehlt")]
    [InlineData("""{"hosts":[null]}""", "hosts[0]: \"host\" fehlt")]
    [InlineData("""{"hosts":[{"host":"PC","key":"abcdefghijkmnpqr","port":0}]}""", "hosts[0]: \"port\": Der Port muss zwischen 1 und 65535 liegen, nicht 0.")]
    public void Rejects(String json, String expected)
    {
        Assert.Null(Parse(json, out String? error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Rejects_BrokenJson()
    {
        Assert.Null(Parse("{\"hosts\":", out String? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RoundTrip_WritesHostListWithoutNulls()
    {
        ClientConfig config = new ClientConfig([new HostEntry("PC", Key), new HostEntry("BUERO-PC", Key, "10.0.0.5", 15112)]);

        String json = Encoding.UTF8.GetString(config.ToJson());

        Assert.Contains("\"hosts\"", json);
        Assert.DoesNotContain("null", json);
        Assert.Equal(config, Parse(json, out _));
    }

    [Fact]
    public void Equal_ByEntries()
    {
        ClientConfig a = new ClientConfig([new HostEntry("PC", Key), new HostEntry("BUERO-PC", Key)]);
        ClientConfig b = new ClientConfig(new List<HostEntry> { new HostEntry("PC", Key), new HostEntry("BUERO-PC", Key) });

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, new ClientConfig([new HostEntry("BUERO-PC", Key), new HostEntry("PC", Key)]));
    }
}
