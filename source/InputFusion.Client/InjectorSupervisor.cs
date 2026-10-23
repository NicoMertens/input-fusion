using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;

namespace InputFusion.Client;

/// <summary>
/// Hält den Injector am Leben. Im Dienst läuft er als SYSTEM in der aktiven Konsolensitzung (siehe
/// <see cref="SessionLauncher"/>); beim Entwickeln als schlichter Kindprozess in derselben Sitzung.
/// Statt auf OnSessionChange zu reagieren, prüft er jede Sekunde die aktive Sitzung und ob der Injector noch
/// lebt. Das deckt Anmelden, Abmelden, Benutzerwechsel und Abstürze gleichermaßen ab.
/// </summary>
internal sealed class InjectorSupervisor(bool asService, Action<string> log)
{
    private const uint NoSession = 0xFFFFFFFF;

    public async Task RunAsync(CancellationToken ct)
    {
        Process? injector = null;
        uint session = NoSession;
        string? lastError = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Zwischen zwei Sitzungen meldet Windows kurz keine aktive; dann abwarten.
                uint target = asService ? PInvoke.WTSGetActiveConsoleSessionId() : (uint)Environment.ProcessId;
                bool alive = injector is { HasExited: false };
                if (target != NoSession && (!alive || target != session))
                {
                    Stop(injector);
                    injector = null;
                    try
                    {
                        injector = Launch(target);
                        session = target;
                        lastError = null;
                        log(asService
                            ? $"Injector in Sitzung {target} gestartet (PID {injector.Id})."
                            : $"Injector als Kindprozess gestartet (PID {injector.Id}).");
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        // Einmal melden, nicht jede Sekunde.
                        if (ex.Message != lastError)
                            log($"Injector ließ sich nicht starten: {ex.Message}. Neuer Versuch jede Sekunde.");
                        lastError = ex.Message;
                    }
                }

                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            Stop(injector);
        }
    }

    private Process Launch(uint session)
    {
        string exe = Environment.ProcessPath!;
        if (asService)
            return SessionLauncher.Start(session, exe, Injector.Argument);

        ProcessStartInfo info = new ProcessStartInfo(exe, Injector.Argument) { UseShellExecute = false };
        return Process.Start(info) ?? throw new InvalidOperationException("Injector-Prozess ließ sich nicht starten.");
    }

    private static void Stop(Process? injector)
    {
        if (injector is null)
            return;
        try
        {
            if (!injector.HasExited)
                injector.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
        injector.Dispose();
    }
}
