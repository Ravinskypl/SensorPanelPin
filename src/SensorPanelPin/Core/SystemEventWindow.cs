using System.Windows.Forms;
using SensorPanelPin.Diagnostics;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.Core;

/// <summary>
/// A hidden top-level window. It must be top-level (not message-only) because WM_DISPLAYCHANGE and
/// WM_POWERBROADCAST are broadcast only to top-level windows.
/// </summary>
public sealed class SystemEventWindow : NativeWindow, IDisposable
{
    private readonly Logger _log;
    private readonly Action<string> _onLayoutEvent;
    private bool _sessionRegistered;

    public SystemEventWindow(Logger log, Action<string> onLayoutEvent)
    {
        _log = log;
        _onLayoutEvent = onLayoutEvent;

        var cp = new CreateParams
        {
            Caption = "SensorPanelPin.SystemEvents",
            ClassName = null,
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,          // not WS_VISIBLE: created but never shown
            ExStyle = (int)WS_EX_TOOLWINDOW,
            Parent = IntPtr.Zero,
        };

        CreateHandle(cp);

        // Session notifications are a nice-to-have: display, DPI and power events arrive regardless,
        // so a failure here must never take startup down.
        try
        {
            if (WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION))
            {
                _sessionRegistered = true;
            }
            else
            {
                _log.Warn("WTSRegisterSessionNotification nie powiodło się; brak reakcji na odblokowanie sesji.");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Rejestracja powiadomień o sesji nie powiodła się; kontynuuję bez nich.", ex);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DISPLAYCHANGE:
                _onLayoutEvent("zmiana konfiguracji ekranów");
                break;

            case WM_DPICHANGED:
                _onLayoutEvent("zmiana DPI");
                break;

            case WM_POWERBROADCAST:
                var powerEvent = m.WParam.ToInt32();
                if (powerEvent is PBT_APMRESUMESUSPEND or PBT_APMRESUMEAUTOMATIC)
                {
                    _onLayoutEvent("wybudzenie systemu");
                }

                break;

            case WM_WTSSESSION_CHANGE:
                var sessionEvent = m.WParam.ToInt32();
                if (sessionEvent is WTS_SESSION_UNLOCK or WTS_SESSION_LOGON)
                {
                    _onLayoutEvent("odblokowanie sesji");
                }

                break;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_sessionRegistered && Handle != IntPtr.Zero)
        {
            try
            {
                WTSUnRegisterSessionNotification(Handle);
            }
            catch (Exception ex)
            {
                _log.Error("Wyrejestrowanie powiadomień o sesji nie powiodło się.", ex);
            }

            _sessionRegistered = false;
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }
}
