using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using InputFusion.Core.Layout;

namespace InputFusion.Core.Display;

/// <summary>
/// Misst die Monitore der eigenen Sitzung: Rechteck in physischen Pixeln (der Prozess ist per Manifest
/// Per-Monitor-V2-aware), DPI und eine Geräte-ID. Der Host misst seine eigenen, auf dem Notebook der Injector,
/// weil nur er in der Benutzersitzung die echten Werte sieht.
/// </summary>
public static unsafe class MonitorEnumerator
{
    [ThreadStatic]
    private static List<MonitorRect>? _collecting;

    public static IReadOnlyList<MonitorRect> GetMonitors()
    {
        List<MonitorRect> result = new List<MonitorRect>();
        _collecting = result;
        try
        {
            PInvoke.EnumDisplayMonitors(HDC.Null, (RECT*)null, &Collect, default);
        }
        finally
        {
            _collecting = null;
        }
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HMONITOR monitor, HDC hdc, RECT* clip, LPARAM data)
    {
        MONITORINFOEXW info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            return true;
        }

        uint dpi = 96;
        if (OperatingSystem.IsWindowsVersionAtLeast(10)
            && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _).Succeeded)
        {
            dpi = dpiX;
        }

        RECT r = info.monitorInfo.rcMonitor;
        string device = info.szDevice.ToString();
        _collecting?.Add(new MonitorRect(r.left, r.top, r.right - r.left, r.bottom - r.top, (int)dpi, StableId(device)));
        return true;
    }

    /// <summary>
    /// Der Pfad des Monitors (Hersteller, Modell, Anschluss) statt des Namens <c>\\.\DISPLAYn</c>: Die Nummer
    /// wechselt beim An- und Abdocken, das Layout soll aber am selben Monitor hängen bleiben.
    /// </summary>
    private static string StableId(string device)
    {
        DISPLAY_DEVICEW display = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (PInvoke.EnumDisplayDevices(device, 0, ref display, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            string id = display.DeviceID.ToString();
            if (id.Length > 0)
            {
                return id;
            }
        }
        return device;
    }
}
