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
