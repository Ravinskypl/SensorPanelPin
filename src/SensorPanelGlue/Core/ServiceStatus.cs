namespace SensorPanelGlue.Core;

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
        ServiceState.Stopped => "Zatrzymany",
        ServiceState.Paused => "Wstrzymany",
        ServiceState.WaitingForMonitor => "Czekam na monitor",
        ServiceState.WaitingForPanel => "Czekam na SensorPanel",
        ServiceState.ElevationRequired => "Brak uprawnień",
        ServiceState.Active => "Pilnuję",
        _ => "?",
    };
}
