using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using InputFusion.Core.Config;
using InputFusion.Core.Net;
using InputFusion.Core.Protocol;

namespace InputFusion.Client;

/// <summary>
/// Die exe installiert sich selbst, wenn sie per Doppelklick außerhalb des Installationsordners startet: erst eine
/// Nachfrage, dann per UAC erhöht neu starten, PC-Name, Schlüssel, IP und Port abfragen und dasselbe tun wie
/// <c>build/install-client.ps1</c>. Dateien nach <see cref="ClientUpdater.InstallDirectory"/> (nur Administratoren
/// schreiben dort, wichtig, weil der Dienst die exe als SYSTEM ausführt), <c>client.json</c> mit ACL nur für SYSTEM
/// und Administratoren, Dienst anlegen und starten.
/// </summary>
internal static class ClientSetup
{
    public const string Argument = "--setup";

    private static readonly TimeSpan _serviceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Doppelklick: kein Argument, kein Dienst, nicht aus dem Installationsordner.</summary>
    public static bool IsDoubleClick(string[] args, bool asService) =>
        args.Length == 0 && !asService
        && !string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), ClientUpdater.InstallDirectory, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fragt nach und startet sich dann erhöht mit <see cref="Argument"/> neu.</summary>
    public static int Start()
    {
        Console.WriteLine($"InputFusion Client {AppVersion.Current}");
        Console.WriteLine();
        if (!AskYes("Den InputFusion-Client auf diesem Rechner als Dienst installieren?"))
        {
            return 1;
        }

        if (IsElevated())
        {
            return Run();
        }

        try
        {
            ProcessStartInfo info = new ProcessStartInfo(Environment.ProcessPath!, Argument) { UseShellExecute = true, Verb = "runas" };
            Process.Start(info)?.Dispose();
            return 0;
        }
        catch (Win32Exception)
        {
            // Die UAC-Abfrage wurde abgelehnt.
            Console.WriteLine("Ohne Adminrechte lässt sich der Dienst nicht installieren.");
            WaitForKey();
            return 1;
        }
    }

    /// <summary>Der erhöhte Teil: abfragen und installieren. Das Konsolenfenster bleibt am Ende offen.</summary>
    public static int Run()
    {
        try
        {
            ClientConfig? existing = ClientConfig.Load(out _);
            if (existing is not null)
            {
                Console.WriteLine($"Vorhandene Konfiguration: {ClientConfig.FilePath}. Enter übernimmt den Wert in Klammern.");
            }

            Console.WriteLine();
            string host = Ask("PC-Name des Hosts (in dessen Einstellungen, Reiter Verbindung)", existing?.Host, value => value.Length > 0 ? null : "Bitte einen Namen angeben.");
            string key = Ask("Schlüssel", existing?.Key, HostConfig.ValidateKey);
            string ip = Ask("Feste IP des Hosts, optional", existing?.Ip ?? "", _ => null);
            string portText = Ask("Port (wie beim Host, Reiter Verbindung)", (existing?.Port ?? Ports.Input).ToString(CultureInfo.InvariantCulture),
                value => Ports.Validate(value, out _));
            Ports.Validate(portText, out int port);
            Console.WriteLine();

            Install(new ClientConfig(host, key, ip.Length == 0 ? null : ip, port));
            Console.WriteLine();
            Console.WriteLine("Fertig. Der Dienst läuft und verbindet sich mit dem Host; Updates holt er sich selbst.");
            Console.WriteLine("Protokoll: Ereignisanzeige > Windows-Protokolle > Anwendung, Quelle InputFusion.Client.");
            WaitForKey();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or Win32Exception or System.ServiceProcess.TimeoutException)
        {
            Console.WriteLine();
            Console.WriteLine($"Installation fehlgeschlagen: {ex.Message}");
            WaitForKey();
            return 1;
        }
    }

    private static void Install(ClientConfig config)
    {
        string target = ClientUpdater.InstallDirectory;
        bool exists = ServiceExists();
        using ServiceController service = new ServiceController(ClientService.ServiceName);
        if (exists)
        {
            Console.WriteLine($"Stoppe den vorhandenen Dienst {ClientService.ServiceName} …");
            service.Refresh();
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, _serviceTimeout);
            }
        }
        ClientProcesses.StopOthers();

        // Aus dem Installationsordner selbst gestartet, ist nichts zu kopieren; nur neu konfigurieren.
        if (!string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Kopiere nach {target} …");
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
            // Nur die exe selbst, unter ihrem festen Namen: Als Setup liegt sie meist umbenannt im Download-Ordner,
            // neben fremden Dateien. Das geht, weil der Release sie als einzelne Datei veröffentlicht.
            Directory.CreateDirectory(target);
            File.Copy(Environment.ProcessPath!, Path.Combine(target, UpdateApplier.ExeName));
        }

        Console.WriteLine($"Schreibe {ClientConfig.FilePath} …");
        if (ClientConfig.Save(config) is { } saveError)
        {
            throw new IOException(saveError);
        }

        string binary = $"\"{Path.Combine(target, UpdateApplier.ExeName)}\"";
        if (exists)
        {
            Sc("config", ClientService.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "LocalSystem");
        }
        else
        {
            Console.WriteLine($"Lege den Dienst {ClientService.ServiceName} an …");
            Sc("create", ClientService.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "InputFusion Client");
            Sc("description", ClientService.ServiceName,
                "Empfängt Maus und Tastatur vom InputFusion-Host und spielt sie in der aktiven Sitzung ab.");
        }
        // Nach Absturz neu starten: 5 s, 5 s, dann 30 s; Zähler nach einem Tag zurücksetzen.
        Sc("failure", ClientService.ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");

        Console.WriteLine("Starte den Dienst …");
        service.Refresh();
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, _serviceTimeout);
    }

    private static bool ServiceExists() =>
        ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, ClientService.ServiceName, StringComparison.OrdinalIgnoreCase));

    private static void Sc(params string[] arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo("sc.exe") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("sc.exe ließ sich nicht starten.");
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc {arguments[0]} fehlgeschlagen ({process.ExitCode}): {output.Trim()}");
        }
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool AskYes(string question)
    {
        Console.Write($"{question} [J/n] ");
        string answer = (Console.ReadLine() ?? "").Trim();
        return answer.Length == 0 || answer.StartsWith('j') || answer.StartsWith('J') || answer.StartsWith('y') || answer.StartsWith('Y');
    }

    /// <summary>Fragt, bis <paramref name="validate"/> nichts mehr auszusetzen hat. Enter übernimmt den Vorschlag.</summary>
    private static string Ask(string prompt, string? suggestion, Func<string, string?> validate)
    {
        while (true)
        {
            Console.Write(string.IsNullOrEmpty(suggestion) ? $"{prompt}: " : $"{prompt} [{suggestion}]: ");
            string value = (Console.ReadLine() ?? "").Trim();
            if (value.Length == 0 && suggestion is not null)
            {
                value = suggestion;
            }

            string? error = validate(value);
            if (error is null)
            {
                return value;
            }

            Console.WriteLine($"  {error}");
        }
    }

    private static void WaitForKey()
    {
        Console.WriteLine();
        Console.Write("Eine Taste drücken zum Schließen.");
        Console.ReadKey(intercept: true);
    }
}
