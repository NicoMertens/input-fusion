using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;

namespace NC2.InputFusion.Client;

/// <summary>
/// Hält einen Prozess der eigenen exe in der aktiven Konsolensitzung am Leben: den Injector als SYSTEM, das
/// Tray-Symbol als angemeldeter Benutzer (siehe <see cref="SessionLauncher"/>); beim Entwickeln als schlichten
/// Kindprozess in derselben Sitzung. Statt auf OnSessionChange zu reagieren, prüft er jede Sekunde die aktive
/// Sitzung und ob der Prozess noch lebt. Das deckt Anmelden, Abmelden, Benutzerwechsel und Abstürze gleichermaßen ab.
/// </summary>
internal sealed class SessionSupervisor(String name, String argument, Boolean asUser, Boolean asService, Action<String> log)
{
    private const UInt32 NoSession = 0xFFFFFFFF;

    public async Task RunAsync(CancellationToken ct)
    {
        Process? process = null;
        UInt32 session = NoSession;
        String? lastError = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Zwischen zwei Sitzungen meldet Windows kurz keine aktive; dann abwarten.
                UInt32 target = asService ? PInvoke.WTSGetActiveConsoleSessionId() : (UInt32)Environment.ProcessId;
                Boolean alive = process is { HasExited: false };
                if (target != NoSession && (!alive || target != session))
                {
                    Stop(process);
                    process = null;
                    try
                    {
                        process = Launch(target);
                        session = target;
                        lastError = null;
                        log(asService
                            ? $"{name} in Sitzung {target} gestartet (PID {process.Id})."
                            : $"{name} als Kindprozess gestartet (PID {process.Id}).");
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
                    {
                        // Einmal melden, nicht jede Sekunde.
                        if (ex.Message != lastError)
                        {
                            log($"{name} ließ sich nicht starten: {ex.Message}. Neuer Versuch jede Sekunde.");
                        }

                        lastError = ex.Message;
                    }
                }

                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            Stop(process);
        }
    }

    private Process Launch(UInt32 session)
    {
        String exe = Environment.ProcessPath!;
        if (asService)
        {
            return asUser ? SessionLauncher.StartAsUser(session, exe, argument) : SessionLauncher.StartAsSystem(session, exe, argument);
        }

        ProcessStartInfo info = new ProcessStartInfo(exe, argument) { UseShellExecute = false };
        return Process.Start(info) ?? throw new InvalidOperationException($"{name} ließ sich nicht starten.");
    }

    private static void Stop(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
        process.Dispose();
    }
}
