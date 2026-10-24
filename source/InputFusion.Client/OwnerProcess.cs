using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace InputFusion.Client;

/// <summary>
/// Lässt Injector und Tray mit dem Dienst enden, der sie gestartet hat. Sonst liefen sie nach einem Absturz des
/// Dienstes weiter, und der neu gestartete Dienst startete jeweils einen zweiten, weil er die alten nicht kennt.
/// Der Dienst gibt dazu seine PID als Argument mit.
/// </summary>
internal static class OwnerProcess
{
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Die Argumente für den Start durch den Dienst: der Modus und dessen PID.</summary>
    public static string Arguments(string mode) => $"{mode} {Environment.ProcessId}";

    /// <summary>Die PID aus den Argumenten hinter dem Modus; null, wenn der Prozess von Hand gestartet wurde.</summary>
    public static int? Parse(string[] rest) =>
        rest is [string text] && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ? pid : null;

    /// <summary>
    /// Bricht <paramref name="cts"/> ab, sobald der Prozess weg ist. Abgefragt über die Prozessliste, weil das Tray als
    /// Benutzer kein Handle auf den SYSTEM-Dienst bekommt; der Name schützt vor einer neu vergebenen PID.
    /// </summary>
    public static async Task WatchAsync(int pid, CancellationTokenSource cts)
    {
        string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "";
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using Process owner = Process.GetProcessById(pid);
                if (!string.Equals(owner.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
            catch (ArgumentException)
            {
                break;
            }

            try { await Task.Delay(_pollInterval, cts.Token); }
            catch (OperationCanceledException) { return; }
        }
        cts.Cancel();
    }
}
