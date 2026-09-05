using SensorPanelGlue.Diagnostics;
using static SensorPanelGlue.Interop.NativeMethods;

namespace SensorPanelGlue.Core;

/// <summary>
/// Out-of-context WinEvent hooks. The callback does nothing but hand the handle to the caller;
/// all real work happens later on the UI thread, after debouncing.
/// </summary>
public sealed class WinEventListener : IDisposable
{
    private readonly Logger _log;
    private readonly WinEventProc _callback;
    private readonly List<IntPtr> _hooks = new();
    private readonly Action<uint, IntPtr> _onEvent;
    private bool _disposed;

    public WinEventListener(Logger log, Action<uint, IntPtr> onEvent)
    {
        _log = log;
        _onEvent = onEvent;
        _callback = HandleEvent;
    }

    public bool IsRunning => _hooks.Count > 0;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        // Two ranges keep the hook count low; EVENT_OBJECT_LOCATIONCHANGE is the noisy one and is
        // filtered hard inside the callback.
        Hook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZEEND);
        Hook(EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND);
        Hook(EVENT_OBJECT_SHOW, EVENT_OBJECT_LOCATIONCHANGE);

        _log.Info($"Zarejestrowano {_hooks.Count} hooków zdarzeń okien.");
    }

    public void Stop()
    {
        foreach (var hook in _hooks)
        {
            UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(
            min,
            max,
            IntPtr.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (hook == IntPtr.Zero)
        {
            _log.Error($"SetWinEventHook nie powiodło się dla zakresu 0x{min:X}-0x{max:X}.");
            return;
        }

        _hooks.Add(hook);
    }

    private void HandleEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only real top-level windows; OBJID_CURSOR traffic alone would flood this callback.
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        try
        {
            _onEvent(eventType, hwnd);
        }
        catch (Exception ex)
        {
            _log.Error("Błąd w obsłudze zdarzenia okna.", ex);
        }
    }
}
