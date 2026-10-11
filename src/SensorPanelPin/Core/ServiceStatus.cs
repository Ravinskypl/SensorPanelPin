using SensorPanelPin.Localization;

namespace SensorPanelPin.Core;

public enum ServiceState
{
    Stopped,
    Paused,
    WaitingForMonitor,
    WaitingForPanel,
    ElevationRequired,
    Active,
}

/// <summary>The panel's actual and intended rectangles, in physical screen pixels.</summary>
public sealed record PanelSnapshot(System.Drawing.Rectangle? Actual, System.Drawing.Rectangle? Desired, string? TargetMonitorId);

public sealed record ServiceStatus(ServiceState State, string Message)
{
    public string ShortText => State switch
    {
        ServiceState.Stopped => Strings.StateStopped,
        ServiceState.Paused => Strings.StatePaused,
        ServiceState.WaitingForMonitor => Strings.StateWaitingForMonitor,
        ServiceState.WaitingForPanel => Strings.StateWaitingForPanel,
        ServiceState.ElevationRequired => Strings.StateElevationRequired,
        ServiceState.Active => Strings.StateActive,
        _ => "?",
    };
}
