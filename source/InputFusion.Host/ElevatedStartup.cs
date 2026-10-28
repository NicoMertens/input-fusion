using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace NC2.InputFusion.Host;

/// <summary>
/// Erhöhter Start einer installierten Kopie. Velopack installiert ohne Adminrechte nach <c>%LOCALAPPDATA%</c> und
/// startet nicht erhöht; ohne Erhöhung bekommen die Hooks aber nichts, solange ein erhöhtes Fenster im Vordergrund
/// ist (UIPI). Deshalb legt der Host einmalig, mit einer UAC-Abfrage, eine geplante Aufgabe „bei Anmeldung, mit
/// höchsten Rechten“ an und dazu die Firewall-Regel für eingehende Verbindungen. Danach startet er sich über die
/// Aufgabe, ohne weitere Abfrage.
///
/// <para>Bewusster Kompromiss: Die Aufgabe startet erhöht eine exe aus einem Ordner, den der Benutzer beschreiben
/// kann. Ein Programm, das unter diesem Benutzer läuft, könnte sie austauschen und käme bei der nächsten Anmeldung
/// ohne Abfrage zu Adminrechten. Siehe <c>docs/updates.md</c>.</para>
/// </summary>
internal static class ElevatedStartup
{
    private const String TaskName = "InputFusion Host";
    private const String FirewallRuleName = "InputFusion Host";
    // ShellExecute meldet so, dass die UAC-Abfrage abgelehnt wurde.
    private const Int32 ErrorCancelled = 1223;

    public static Boolean IsElevated
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static Boolean TaskExists() => Run("schtasks.exe", $"/Query /TN \"{TaskName}\"") == 0;

    public static Boolean StartTask() => Run("schtasks.exe", $"/Run /TN \"{TaskName}\"") == 0;

    /// <summary>Legt Aufgabe und Firewall-Regel an, mit einer UAC-Abfrage. False, wenn abgelehnt oder fehlgeschlagen.</summary>
    public static Boolean Install(String exePath, Int32 port)
    {
        // Der Benutzer vor der Erhöhung: Meldet sich ein Standardbenutzer mit Admin-Kennwort an, liefe die Aufgabe
        // sonst unter dem Admin-Konto. Als SID, weil der Name bei Microsoft-Konten je nach Zusammenhang anders lautet.
        String user;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            user = identity.User?.Value ?? identity.Name;
        }

        String file = Path.Combine(Path.GetTempPath(), "InputFusion.Host.task.xml");
        // schtasks erwartet die Aufgabe als UTF-16 mit BOM, passend zur Kopfzeile.
        File.WriteAllText(file, TaskXml(user, exePath), Encoding.Unicode);
        try
        {
            String script = $"schtasks /Create /TN \"{TaskName}\" /XML \"{file}\" /F"
                + $" && (netsh advfirewall firewall delete rule name=\"{FirewallRuleName}\" >nul"
                + $" & netsh {AddFirewallRule(port)})";
            Int32 exitCode = RunElevated("cmd.exe", $"/c {script}");
            if (exitCode != 0)
            {
                Log.Write(exitCode == -ErrorCancelled
                    ? "Erhöhter Start abgelehnt; der Host läuft ohne Adminrechte."
                    : $"Geplante Aufgabe ließ sich nicht anlegen (Exitcode {exitCode}); der Host läuft ohne Adminrechte.");
                return false;
            }
            Log.Write($"Geplante Aufgabe „{TaskName}“ und Firewall-Regel für Port {port} angelegt.");
            return true;
        }
        finally
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Stellt die Firewall-Regel auf <paramref name="port"/>, legt sie an, falls sie fehlt. Nur erhöht aufrufen, also
    /// aus der Aufgabe gestartet; ein von Hand geänderter Port greift so beim nächsten Start ohne UAC-Abfrage.
    /// </summary>
    public static void UpdateFirewall(Int32 port)
    {
        if (Run("netsh.exe", $"advfirewall firewall set rule name=\"{FirewallRuleName}\" new localport={port}") == 0)
        {
            return;
        }

        if (Run("netsh.exe", AddFirewallRule(port)) == 0)
        {
            Log.Write($"Firewall-Regel „{FirewallRuleName}“ für Port {port} neu angelegt.");
        }
        else
        {
            Log.Write($"Firewall-Regel „{FirewallRuleName}“ ließ sich nicht auf Port {port} stellen.");
        }
    }

    private static String AddFirewallRule(Int32 port) =>
        $"advfirewall firewall add rule name=\"{FirewallRuleName}\" dir=in action=allow protocol=TCP localport={port} profile=any";

    /// <summary>
    /// Beendet den laufenden Host und entfernt Aufgabe und Firewall-Regel, mit einer UAC-Abfrage. Läuft im
    /// Deinstallations-Hook von Velopack: Dessen Updater läuft nicht erhöht und könnte den erhöhten Host selbst
    /// nicht beenden, dann blieben dessen Dateien gesperrt. <c>schtasks /End</c> allein genügt nicht: Nach einem
    /// Update hat der Updater den Host direkt gestartet, nicht über die Aufgabe. Der Hook selbst ist ebenfalls
    /// <c>InputFusion.Host.exe</c> und wird deshalb ausgenommen.
    /// </summary>
    public static void Uninstall() =>
        RunElevated("cmd.exe", $"/c schtasks /End /TN \"{TaskName}\""
            + $" & taskkill /F /IM InputFusion.Host.exe /FI \"PID ne {Environment.ProcessId}\""
            + $" & schtasks /Delete /TN \"{TaskName}\" /F"
            + $" & netsh advfirewall firewall delete rule name=\"{FirewallRuleName}\"");

    /// <summary>
    /// Als XML statt über Schalter, weil sich nur so alles Nötige setzen lässt: keine Laufzeitgrenze (Standard sind
    /// 72 Stunden, danach beendet Windows den Host), normale Priorität (Standard ist „unter normal“, schlecht für die
    /// Hooks), kein Abbruch im Akkubetrieb, keine zweite Instanz, und der Auslöser nur für diesen Benutzer.
    /// </summary>
    private static String TaskXml(String user, String exePath)
    {
        String u = SecurityElement.Escape(user);
        String exe = SecurityElement.Escape(exePath);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Startet den InputFusion-Host bei der Anmeldung mit höchsten Rechten.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{u}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{u}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{exe}"</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static Int32 Run(String file, String arguments)
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process process = Process.Start(info) ?? throw new InvalidOperationException($"{file} ließ sich nicht starten.");
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"{file} {arguments}: {ex.Message}");
            return -1;
        }
    }

    /// <summary>Startet erhöht (UAC-Abfrage) und wartet. Bei abgelehnter Abfrage −1223.</summary>
    private static Int32 RunElevated(String file, String arguments)
    {
        try
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process process = Process.Start(info) ?? throw new InvalidOperationException($"{file} ließ sich nicht starten.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return -ErrorCancelled;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"{file}: {ex.Message}");
            return -1;
        }
    }
}
