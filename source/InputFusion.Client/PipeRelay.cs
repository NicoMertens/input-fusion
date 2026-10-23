using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using InputFusion.Core.Ipc;
using InputFusion.Core.Net;

namespace InputFusion.Client;

/// <summary>
/// Reicht die vom Host empfangenen Eingabe-Frames über die Pipe an den <see cref="Injector"/> weiter —
/// die Aufgabe des Dienstes aus Session 0, der selbst nicht abspielen kann. Zum Entwickeln läuft derselbe
/// Weg als Konsole (<c>--relay &lt;host&gt;</c>), und der Injector wird als Kindprozess in derselben Sitzung
/// gestartet.
///
/// <para><b>Offen (Phase 2, Schritt 2, nur an echter Hardware testbar):</b> Im Dienst ersetzt
/// <c>CreateProcessAsUser</c> mit dupliziertem SYSTEM-Token den schlichten <see cref="StartInjector"/>,
/// damit der Injector in der aktiven Konsolensitzung und auf dem richtigen Desktop landet. Siehe
/// <c>docs/client.md</c>.</para>
/// </summary>
internal static class PipeRelay
{
    public static async Task RelayAsync(string host, bool launchInjector, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        Console.WriteLine($"Verbinde zu {host}:{Ports.Input}...");
        await tcp.ConnectAsync(host, Ports.Input, ct);
        using var hostConn = MessageConnection.FromTcp(tcp);
        Console.WriteLine("Mit dem Host verbunden.");

        var server = InputPipe.CreateServer();
        using var pipeConn = new MessageConnection(server);

        Process? injector = launchInjector ? StartInjector() : null;
        try
        {
            Console.WriteLine("Warte auf den Injector an der Pipe...");
            await server.WaitForConnectionAsync(ct);
            Console.WriteLine("Injector verbunden. Reiche Eingaben weiter.");

            // Beide Richtungen gleichzeitig: Eingaben zum Injector, dessen Meldungen (Austritt an der Kante)
            // zurück zum Host. Endet eine Richtung, wird die andere abgebrochen.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var toHost = ForwardAsync(pipeConn, hostConn, linked);
            var toInjector = ForwardAsync(hostConn, pipeConn, linked);
            await Task.WhenAll(toHost, toInjector);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            if (injector is { HasExited: false })
            {
                try { injector.Kill(); }
                catch (InvalidOperationException) { }
            }
            injector?.Dispose();
        }
        Console.WriteLine("Relay beendet.");
    }

    /// <summary>Reicht Frames unverändert von einer Verbindung zur anderen, bis eine Seite endet.</summary>
    private static async Task ForwardAsync(MessageConnection from, MessageConnection to, CancellationTokenSource linked)
    {
        try
        {
            await foreach (var (type, payload) in from.ReceiveAsync(linked.Token))
                await to.SendAsync(type, payload, linked.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
        }
        finally
        {
            linked.Cancel();
        }
    }

    /// <summary>Startet den Injector als Kindprozess in derselben Sitzung (Dev-Ersatz für CreateProcessAsUser).</summary>
    private static Process StartInjector()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!, Injector.Argument) { UseShellExecute = false };
        return Process.Start(info) ?? throw new InvalidOperationException("Injector-Prozess ließ sich nicht starten.");
    }
}
