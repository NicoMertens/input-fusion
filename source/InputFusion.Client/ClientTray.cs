using System.IO;
using System.IO.Pipes;
using NC2.InputFusion.Core.Ipc;
using NC2.InputFusion.Core.Net;
using NC2.InputFusion.Core.Protocol;
using NC2.InputFusion.Core.Shell;

namespace NC2.InputFusion.Client;

/// <summary>
/// Das Tray-Symbol auf dem Notebook. Läuft als angemeldeter Benutzer, weil weder der Dienst (Session 0) noch der
/// Injector (SYSTEM, absichtlich ohne Oberfläche) ein Symbol zeigen können; der Dienst startet ihn je Sitzung.
/// Den Zustand holt er über die <see cref="ClientPipe"/> vom Dienst und zeigt ihn im Tooltip; Doppelklick öffnet die Einstellungen.
/// Außerdem liest und schreibt er die Zwischenablage für den Abgleich mit dem Host, weil sie dem Benutzer gehört.
/// </summary>
internal sealed class ClientTray
{
    // Wie der Injector mit CREATE_NO_WINDOW gestartet; Konsolenausgaben sieht man nur beim Entwickeln.
    public const String Argument = "--tray";

    private const String ServiceUnreachable = "InputFusion: Dienst nicht erreichbar.";

    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(1);
    // So lange darf die Neuverbindung zum PC dauern, ohne dass der Abbruch gemeldet wird.
    private static readonly TimeSpan _notifyDelay = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cts;
    private readonly ClipboardSync _clipboard = new ClipboardSync(Console.WriteLine);
    private readonly TrayIcon _tray;
    private readonly ConnectionNotifier _notifier;
    // Die Verbindung zum Dienst, solange eine steht, und die Suche nach Updates, deren Antwort noch aussteht.
    private MessageConnection? _current;
    private TaskCompletionSource<String>? _pendingCheck;
    // Je Verbindung zum Dienst neu: Der erste Zustand ist kein Wechsel.
    private Boolean? _hostConnected;
    // Nach dem Abbruch nennt der Zustand keinen verbundenen PC mehr, die Meldung soll ihn aber nennen.
    private String? _lastHost;

    private ClientTray(CancellationTokenSource cts)
    {
        _cts = cts;
        _tray = new TrayIcon($"InputFusion Client {AppVersion.Current}",
            [new TrayCommand("Einstellungen …", () => SettingsLauncher.Open(Console.WriteLine, SettingsLauncher.ClientArgument))], Console.WriteLine,
            checkForUpdates: CheckForUpdatesAsync);
        _notifier = new ConnectionNotifier(_tray.ShowBalloon, _notifyDelay);
    }

    public static void Run(String[] rest) => RunAsync(OwnerProcess.Parse(rest)).GetAwaiter().GetResult();

    private static async Task RunAsync(Int32? ownerPid)
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        if (ownerPid is Int32 pid)
        {
            _ = OwnerProcess.WatchAsync(pid, cts);
        }

        await new ClientTray(cts).RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        _tray.Start();
        _tray.SetTooltip(ServiceUnreachable);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await ServeConnectionAsync())
                {
                    break;
                }

                OnServiceLost();
                try { await Task.Delay(_retryDelay, _cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _tray.Stop();
        }
    }

    /// <summary>Eine Verbindung zum Dienst von Aufbau bis Abbruch; <c>false</c> heißt beenden.</summary>
    private async Task<Boolean> ServeConnectionAsync()
    {
        try
        {
            using NamedPipeClientStream pipe = ClientPipe.CreateClient();
            await pipe.ConnectAsync((Int32)_retryDelay.TotalMilliseconds, _cts.Token);
            ClientPipe.VerifyServer(pipe);
            using MessageConnection connection = new MessageConnection(pipe);
            await connection.SendAsync(MessageType.ClipboardReceiver, ReadOnlyMemory<Byte>.Empty, _cts.Token);
            _current = connection;
            _hostConnected = null;
            _lastHost = null;
            await foreach ((MessageType type, Byte[] payload) in connection.ReceiveAsync(_cts.Token))
            {
                await HandleAsync(connection, type, payload);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            // Dienst läuft nicht oder startet gerade neu.
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Pipe abgelehnt: {ex.Message}");
        }
        return true;
    }

    private async Task HandleAsync(MessageConnection connection, MessageType type, Byte[] payload)
    {
        SettingsMessage? message = type == MessageType.Settings ? SettingsMessage.FromPayload(payload) : null;
        if (message is ClientStateMessage state)
        {
            OnState(state);
        }
        else if (message is UpdateCheckResult check)
        {
            _pendingCheck?.TrySetResult(check.Message);
        }
        else if (type == MessageType.ClipboardText)
        {
            _clipboard.Receive(payload);
        }
        else if (type == MessageType.ClipboardRequest)
        {
            foreach (Byte[] chunk in _clipboard.TakeChanged())
            {
                await connection.SendAsync(MessageType.ClipboardText, chunk, _cts.Token);
            }
        }
    }

    private void OnState(ClientStateMessage state)
    {
        _tray.SetTooltip($"InputFusion: {state.Summary()}");
        if (_hostConnected == true && !state.Connected)
        {
            _ = _notifier.Lost($"Verbindung zu {_lastHost ?? "dem PC"} beendet. {state.Problem}".TrimEnd());
        }
        else if (_hostConnected == false && state.Connected)
        {
            _notifier.Restored($"Wieder mit {state.Host} verbunden.");
        }
        _hostConnected = state.Connected;
        _lastHost = state.Host ?? _lastHost;
    }

    private void OnServiceLost()
    {
        _current = null;
        // Ohne Dienst ist unbekannt, wie es mit dem PC steht; das sagt schon der Tooltip.
        _notifier.Reset();
        // Etwa weil der Dienst gerade für ein Update neu startet; die Antwort käme nie mehr.
        _pendingCheck?.TrySetResult("Die Verbindung zum Dienst ist abgebrochen, vielleicht startet er für ein Update neu.");
        _tray.SetTooltip(ServiceUnreachable);
    }

    private async Task<String> CheckForUpdatesAsync()
    {
        MessageConnection? connection = _current;
        if (connection is null)
        {
            return "Der InputFusion-Dienst läuft nicht.";
        }

        TaskCompletionSource<String> result = new TaskCompletionSource<String>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCheck = result;
        try
        {
            await connection.SendAsync(MessageType.Settings, new CheckForUpdatesRequest().ToPayload(), _cts.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return "Der InputFusion-Dienst läuft nicht.";
        }
        return await result.Task;
    }
}
