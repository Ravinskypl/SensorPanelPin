using System.Text;
using SensorPanelPin.Config;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.Windows;

/// <summary>Reads window metadata and enumerates top-level windows.</summary>
public static class WindowQuery
{
    /// <summary>
    /// Titles are set by the program that owns the window, and GetWindowTextLength happily reports
    /// megabytes. Nothing here needs more than the start of a title.
    /// </summary>
    internal const int MaxTitleLength = 512;

    /// <summary>PIDs are recycled; a cached name older than this may belong to a different process.</summary>
    private static readonly TimeSpan ProcessNameTtl = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<uint, (string Name, DateTime CachedAt)> ProcessNameCache = new();
    private static readonly object CacheGate = new();

    public static WindowInfo? Get(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return null;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        var classBuffer = new StringBuilder(256);
        GetClassName(hwnd, classBuffer, classBuffer.Capacity);

        var titleLength = Math.Min(GetWindowTextLength(hwnd), MaxTitleLength);
        var title = string.Empty;
        if (titleLength > 0)
        {
            var titleBuffer = new StringBuilder(titleLength + 1);
            GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
            title = titleBuffer.ToString();
        }

        return new WindowInfo
        {
            Handle = hwnd,
            ClassName = classBuffer.ToString(),
            Title = title,
            ProcessId = pid,
            ProcessName = GetProcessName(pid),
            Style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64(),
            ExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64(),
            Rect = rect.ToRectangle(),
            IsVisible = IsWindowVisible(hwnd),
            IsMinimized = IsIconic(hwnd),
            IsMaximized = IsZoomed(hwnd),
            Owner = GetWindow(hwnd, GW_OWNER),
        };
    }

    public static List<WindowInfo> EnumerateTopLevel(Func<WindowInfo, bool>? predicate = null)
    {
        var list = new List<WindowInfo>();

        EnumWindows(
            (hwnd, _) =>
            {
                var info = Get(hwnd);
                if (info is not null && (predicate is null || predicate(info)))
                {
                    list.Add(info);
                }

                return true;
            },
            IntPtr.Zero);

        return list;
    }

    /// <summary>
    /// Uses QueryFullProcessImageName with limited rights so elevated processes are still named
    /// where possible; returns an empty string when the process cannot be opened at all.
    /// </summary>
    public static string GetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        lock (CacheGate)
        {
            if (ProcessNameCache.TryGetValue(pid, out var cached)
                && DateTime.UtcNow - cached.CachedAt < ProcessNameTtl)
            {
                return cached.Name;
            }
        }

        var name = string.Empty;
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                var size = (uint)buffer.Capacity;
                if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                {
                    name = Path.GetFileNameWithoutExtension(buffer.ToString()).ToLowerInvariant();
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        lock (CacheGate)
        {
            // Bounded cache: PIDs are recycled, so drop everything once it grows.
            if (ProcessNameCache.Count > 512)
            {
                ProcessNameCache.Clear();
            }

            ProcessNameCache[pid] = (name, DateTime.UtcNow);
        }

        return name;
    }

    public static void InvalidateProcessCache()
    {
        lock (CacheGate)
        {
            ProcessNameCache.Clear();
        }
    }

    public static bool Matches(WindowInfo info, WindowRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.ProcessName)
            && !Wildcard.IsMatch(info.ProcessName, rule.ProcessName.Trim()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ClassPattern)
            && !Wildcard.IsMatch(info.ClassName, rule.ClassPattern.Trim()))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.TitlePattern)
            && !Wildcard.IsMatch(info.Title, rule.TitlePattern.Trim()))
        {
            return false;
        }

        // An empty rule must not match everything.
        return !string.IsNullOrWhiteSpace(rule.ProcessName)
               || !string.IsNullOrWhiteSpace(rule.ClassPattern)
               || !string.IsNullOrWhiteSpace(rule.TitlePattern);
    }
}
