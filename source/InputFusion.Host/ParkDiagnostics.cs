using System.Drawing;
using System.IO;
using Windows.Win32;

namespace NC2.InputFusion.Host;

/// <summary>
/// Misst mit <c>--diag</c>, wie oft SetCursorPos wirklich hält: wie weit der Cursor vor dem Zurücksetzen aus der Mitte
/// gelaufen ist (Drift) und ob er direkt nach dem Zurücksetzen tatsächlich in der Mitte steht.
/// Bei eingefrorener Maus bleibt die Drift klein; zieht RDP den Cursor weg, wird sie groß.
/// </summary>
internal sealed class ParkDiagnostics
{
    private Int32 _recenters, _held, _maxDrift;

    public String FilePath { get; } = Path.Combine(Path.GetTempPath(), "inputfusion-diag.log");

    /// <summary>Setzt den Cursor zurück in den Parkpunkt und schreibt alle 60 Mal eine Zeile Statistik.</summary>
    public void Recenter(MouseMoveEvent move, Int32 parkX, Int32 parkY)
    {
        Int32 drift = Math.Max(Math.Abs(move.X - parkX), Math.Abs(move.Y - parkY));
        _maxDrift = Math.Max(_maxDrift, drift);

        PInvoke.SetCursorPos(parkX, parkY);
        PInvoke.GetCursorPos(out Point after);
        Boolean held = Math.Abs(after.X - parkX) <= 2 && Math.Abs(after.Y - parkY) <= 2;
        if (held)
        {
            _held++;
        }

        _recenters++;

        if (_recenters % 60 == 0)
        {
            String line = $"{DateTime.Now:HH:mm:ss}  SetCursorPos direkt gehalten: {_held}/{_recenters} "
                + $"({100 * _held / _recenters} %), groesste Drift aus der Mitte: {_maxDrift}px";
            Log.Write(line);
            try { File.AppendAllText(FilePath, line + Environment.NewLine); } catch (IOException) { }
            _maxDrift = 0;
        }
    }
}
