using System.Drawing;
using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.Interop;
using static SensorPanelGlue.Interop.DisplayConfigInterop;
using static SensorPanelGlue.Interop.NativeMethods;

namespace SensorPanelGlue.Monitors;

/// <summary>
/// Enumerates monitors and pairs each with a stable device path obtained from QueryDisplayConfig.
/// </summary>
public sealed class MonitorRegistry
{
    private readonly Logger _log;
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();

    public MonitorRegistry(Logger log)
    {
        _log = log;
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>
    /// False while Windows reports a layout that cannot be real. During a display transition -
    /// a monitor powered off and back on, a mode change, a game switching resolution - the
    /// enumeration can briefly place a monitor at the origin, overlapping another one. Acting on
    /// those coordinates throws windows onto the wrong screen, so callers must wait instead.
    /// </summary>
    public bool LayoutIsConsistent { get; private set; } = true;

    public void Refresh()
    {
        var identities = ReadDisplayConfigIdentities();
        var result = new List<MonitorInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var mi = new MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };

            if (!GetMonitorInfo(hMonitor, ref mi))
            {
                return true;
            }

            var gdiName = mi.szDevice ?? string.Empty;
            identities.TryGetValue(gdiName, out var identity);

            uint dpi = 96;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                dpi = dpiX;
            }

            var stableId = !string.IsNullOrEmpty(identity.DevicePath)
                ? identity.DevicePath
                : "GDI:" + gdiName;

            var friendly = !string.IsNullOrWhiteSpace(identity.FriendlyName)
                ? identity.FriendlyName
                : gdiName;

            result.Add(new MonitorInfo
            {
                StableId = stableId,
                FriendlyName = friendly,
                GdiDeviceName = gdiName,
                Bounds = mi.rcMonitor.ToRectangle(),
                WorkArea = mi.rcWork.ToRectangle(),
                IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                Dpi = dpi,
                Handle = hMonitor,
            });

            return true;
        }

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            _log.Warn("EnumDisplayMonitors zwróciło błąd; lista monitorów może być niekompletna.");
        }

        result.Sort((a, b) => a.Bounds.X != b.Bounds.X
            ? a.Bounds.X.CompareTo(b.Bounds.X)
            : a.Bounds.Y.CompareTo(b.Bounds.Y));

        var previous = _monitors;
        _monitors = result;
        LayoutIsConsistent = CheckConsistency(result);

        LogIfChanged(previous, result);
    }

    private static bool CheckConsistency(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return false;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Width <= 0 || monitor.Bounds.Height <= 0)
            {
                return false;
            }
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            for (var j = i + 1; j < monitors.Count; j++)
            {
                var a = monitors[i].Bounds;
                var b = monitors[j].Bounds;

                // Cloned displays legitimately share one rectangle; a partial overlap cannot happen
                // on a settled desktop and means the reading caught a transition mid-flight.
                if (a != b && a.IntersectsWith(b))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Records every change of the monitor set. Display transitions are exactly the moments when
    /// things go wrong, and without this the log cannot show what the layout looked like.
    /// </summary>
    private void LogIfChanged(IReadOnlyList<MonitorInfo> previous, IReadOnlyList<MonitorInfo> current)
    {
        static string Signature(IReadOnlyList<MonitorInfo> list) =>
            string.Join(" | ", list.Select(m => $"{m.StableId}@{m.Bounds}{(m.IsPrimary ? "*" : string.Empty)}"));

        if (Signature(previous) == Signature(current))
        {
            return;
        }

        _log.Info($"Układ ekranów: {current.Count} monitorów, spójny={LayoutIsConsistent}");
        foreach (var monitor in current)
        {
            _log.Info($"  {monitor.FriendlyName} {monitor.Bounds} work={monitor.WorkArea} "
                      + $"dpi={monitor.Dpi} primary={monitor.IsPrimary}");
        }
    }

    public MonitorInfo? FindById(string? stableId)
    {
        if (string.IsNullOrEmpty(stableId))
        {
            return null;
        }

        return _monitors.FirstOrDefault(m =>
            string.Equals(m.StableId, stableId, StringComparison.OrdinalIgnoreCase));
    }

    public MonitorInfo? Primary => _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();

    /// <summary>Resolves the monitor foreign windows are pushed to; falls back to the system primary.</summary>
    public MonitorInfo? ResolveHost(string? configuredId) => FindById(configuredId) ?? Primary;

    public MonitorInfo? FromWindow(IntPtr hwnd)
    {
        var handle = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var match = _monitors.FirstOrDefault(m => m.Handle == handle);
        if (match is not null)
        {
            return match;
        }

        // Monitor handles are only valid for the current display arrangement; fall back to geometry.
        if (GetWindowRect(hwnd, out var rect))
        {
            return FromRect(rect.ToRectangle());
        }

        return null;
    }

    /// <summary>Monitor with the largest intersection with <paramref name="rect"/>.</summary>
    public MonitorInfo? FromRect(Rectangle rect)
    {
        MonitorInfo? best = null;
        var bestArea = 0L;

        foreach (var monitor in _monitors)
        {
            var inter = Rectangle.Intersect(monitor.Bounds, rect);
            long area = (long)inter.Width * inter.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        if (best is not null)
        {
            return best;
        }

        // Off-screen window: pick the monitor whose centre is nearest.
        var center = new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        double bestDistance = double.MaxValue;
        foreach (var monitor in _monitors)
        {
            var mc = new Point(
                monitor.Bounds.X + (monitor.Bounds.Width / 2),
                monitor.Bounds.Y + (monitor.Bounds.Height / 2));
            var dx = (double)mc.X - center.X;
            var dy = (double)mc.Y - center.Y;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = monitor;
            }
        }

        return best;
    }

    private readonly record struct DisplayIdentity(string FriendlyName, string DevicePath);

    private Dictionary<string, DisplayIdentity> ReadDisplayConfigIdentities()
    {
        var map = new Dictionary<string, DisplayIdentity>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var status = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
            if (status != ERROR_SUCCESS)
            {
                _log.Warn($"GetDisplayConfigBufferSizes zwróciło {status}; używam nazw GDI jako identyfikatorów.");
                return map;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status != ERROR_SUCCESS)
            {
                _log.Warn($"QueryDisplayConfig zwróciło {status}; używam nazw GDI jako identyfikatorów.");
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                    viewGdiDeviceName = string.Empty,
                };

                if (DisplayConfigGetDeviceInfo(ref source) != ERROR_SUCCESS)
                {
                    continue;
                }

                var gdiName = source.viewGdiDeviceName ?? string.Empty;
                if (gdiName.Length == 0 || map.ContainsKey(gdiName))
                {
                    // First active path wins; clones share one source.
                    continue;
                }

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    },
                    monitorFriendlyDeviceName = string.Empty,
                    monitorDevicePath = string.Empty,
                };

                if (DisplayConfigGetDeviceInfo(ref target) != ERROR_SUCCESS)
                {
                    continue;
                }

                map[gdiName] = new DisplayIdentity(
                    target.monitorFriendlyDeviceName ?? string.Empty,
                    target.monitorDevicePath ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Odczyt konfiguracji wyświetlaczy (CCD) nie powiódł się.", ex);
        }

        return map;
    }
}
