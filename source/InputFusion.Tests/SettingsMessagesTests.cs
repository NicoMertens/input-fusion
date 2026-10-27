using System.Text;
using InputFusion.Core.Config;
using InputFusion.Core.Ipc;
using InputFusion.Core.Layout;
using InputFusion.Core.Protocol;

namespace InputFusion.Tests;

public class SettingsMessagesTests
{
    [Fact]
    public void HostState_RoundTrip()
    {
        MonitorRect monitor = new MonitorRect(-1920, 0, 1920, 1080, 96, @"\\?\DISPLAY#GSM5B7F#5&1");
        SettingsMessage message = new HostStateMessage("PC", "0.2.0", ["192.168.1.10"], 15111, "abcdefghijkmnpqr",
            [monitor], new ClientStateInfo("192.168.1.20", "0.2.0", [monitor with { DeviceId = "N1" }], 0, -1080,
                "NOTEBOOK", "Windows 11 Pro 24H2 (Build 26100.4061)", null));

        HostStateMessage back = Assert.IsType<HostStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal("PC", back.MachineName);
        Assert.Equal(monitor, Assert.Single(back.HostMonitors));
        Assert.Equal(-1080, back.Client!.OffsetY);
        Assert.Equal("N1", Assert.Single(back.Client.Monitors).DeviceId);
        Assert.Equal("NOTEBOOK", back.Client.MachineName);
        Assert.Null(back.Client.UserName);
        Assert.Null(back.PendingKey);
    }

    [Fact]
    public void HostState_WithKeyTransfer_RoundTrip()
    {
        SettingsMessage message = new HostStateMessage("PC", "0.8.0", [], 15111, "abcdefghijkmnpqr", [], null,
            PendingKey: "zyxwvutsrqpnmkjh", KeyStatus: "Das Notebook hat den neuen Schlüssel übernommen.");

        HostStateMessage back = Assert.IsType<HostStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal("zyxwvutsrqpnmkjh", back.PendingKey);
        Assert.Equal("Das Notebook hat den neuen Schlüssel übernommen.", back.KeyStatus);
    }

    [Fact]
    public void ClientState_RoundTrip()
    {
        HostEntry home = new HostEntry("PC", "abcdefghijkmnpqr", "192.168.1.10");
        HostEntry office = new HostEntry("BUERO-PC", "zyxwvutsrqpnmkjh", Port: 15112);
        ClientStateMessage message = new ClientStateMessage("0.5.0", [home, office], true, true, "PC", "192.168.1.10", "0.5.0", null);

        ClientStateMessage back = Assert.IsType<ClientStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal([home, office], back.Hosts);
        // Die Liste vergleicht der Record nur als Verweis.
        Assert.Equal(message, back with { Hosts = message.Hosts });
    }

    [Fact]
    public void ClientState_WithArrangement_RoundTrip()
    {
        MonitorRect host = new MonitorRect(0, 0, 2560, 1440, 96, "H1");
        MonitorRect client = new MonitorRect(0, 0, 1920, 1200, 144, "N1");
        SettingsMessage message = new ClientStateMessage("0.8.0", [new HostEntry("PC", "")], false, true, "PC", "192.168.1.10", "0.8.0", null,
            new ClientArrangement([host], [client], 2560, 0));

        ClientStateMessage back = Assert.IsType<ClientStateMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal(host, Assert.Single(back.Arrangement!.HostMonitors));
        Assert.Equal(client, Assert.Single(back.Arrangement.ClientMonitors));
        Assert.Equal(2560, back.Arrangement.OffsetX);
    }

    [Fact]
    public void Diagnostics_RoundTrip()
    {
        DateTimeOffset since = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.FromHours(1));
        SettingsMessage message = new DiagnosticsMessage(true, since, 2.5, 120, 40, "Der PC hat die Verbindung getrennt.", since,
            ["09:30:00  verbunden"], null);

        DiagnosticsMessage back = Assert.IsType<DiagnosticsMessage>(SettingsMessage.FromPayload(message.ToPayload()));

        Assert.Equal(since, back.ConnectedSince);
        Assert.Equal(2.5, back.RoundTripMs);
        Assert.Equal(120, back.SentFrames);
        Assert.Equal("09:30:00  verbunden", Assert.Single(back.Log));
        Assert.Null(back.LogPath);
        Assert.IsType<GetDiagnosticsRequest>(SettingsMessage.FromPayload(new GetDiagnosticsRequest().ToPayload()));
    }

    [Theory]
    [InlineData(true, "PC", null, "Verbunden mit PC (192.168.1.10), Host-Version 0.5.0.")]
    [InlineData(true, "PC,BUERO", null, "Verbunden mit PC (192.168.1.10), Host-Version 0.5.0.")]
    [InlineData(false, "PC", "Host PC:15111 nicht erreichbar (TimedOut).", "Nicht verbunden mit PC. Host PC:15111 nicht erreichbar (TimedOut).")]
    [InlineData(false, "PC,BUERO", null, "Nicht verbunden mit PC oder BUERO.")]
    [InlineData(false, "PC,BUERO,LABOR", null, "Nicht verbunden mit PC, BUERO oder LABOR.")]
    [InlineData(false, "", null, "Nicht eingerichtet.")]
    public void ClientState_Summary(bool connected, string hosts, string? problem, string expected)
    {
        HostEntry[] entries = hosts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(h => new HostEntry(h, "")).ToArray();
        ClientStateMessage state = new ClientStateMessage("0.5.0", entries, false, connected,
            connected ? entries[0].Host : null, connected ? "192.168.1.10" : null, connected ? "0.5.0" : null, problem);

        Assert.Equal(expected, state.Summary());
    }

    [Fact]
    public void Requests_CarryTypeDiscriminator()
    {
        string json = Encoding.UTF8.GetString(new SetKeyRequest("abcdefghijkmnpqr").ToPayload());
        Assert.Contains("\"type\":\"setKey\"", json);
        Assert.Equal(new SetPortRequest(15112), SettingsMessage.FromPayload(new SetPortRequest(15112).ToPayload()));
        Assert.IsType<SetClientOffsetRequest>(SettingsMessage.FromPayload(new SetClientOffsetRequest(1, 2).ToPayload()));
        SetClientConfigRequest config = new SetClientConfigRequest([new HostEntry("PC", "abcdefghijkmnpqr"), new HostEntry("BUERO", "zyxwvutsrqpnmkjh", "10.0.0.5")]);
        Assert.Equal(config.Hosts, Assert.IsType<SetClientConfigRequest>(SettingsMessage.FromPayload(config.ToPayload())).Hosts);
        Assert.IsType<CheckForUpdatesRequest>(SettingsMessage.FromPayload(new CheckForUpdatesRequest().ToPayload()));
        UpdateCheckResult result = new UpdateCheckResult("InputFusion Client 0.6.0 ist aktuell.");
        Assert.Equal(result, SettingsMessage.FromPayload(result.ToPayload()));
    }

    [Fact]
    public void Garbage_IsNull()
    {
        Assert.Null(SettingsMessage.FromPayload(Encoding.UTF8.GetBytes("{\"type\":\"unbekannt\"}")));
        Assert.Null(SettingsMessage.FromPayload([1, 2, 3]));
    }
}
