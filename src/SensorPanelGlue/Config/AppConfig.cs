using System.Text.Json.Serialization;

namespace SensorPanelGlue.Config;

/// <summary>Window matching rule. Patterns support * and ? wildcards; empty means "any".</summary>
public sealed class WindowRule
{
    public string ProcessName { get; set; } = string.Empty;

    public string ClassPattern { get; set; } = string.Empty;

    public string TitlePattern { get; set; } = string.Empty;

    public override string ToString()
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(ProcessName))
        {
            parts.Add("proces=" + ProcessName);
        }

        if (!string.IsNullOrWhiteSpace(ClassPattern))
        {
            parts.Add("klasa=" + ClassPattern);
        }

        if (!string.IsNullOrWhiteSpace(TitlePattern))
        {
            parts.Add("tytuł=" + TitlePattern);
        }

        return parts.Count == 0 ? "(pusta reguła)" : string.Join("; ", parts);
    }
}

/// <summary>Stored geometry of the SensorPanel, relative to its target monitor.</summary>
public sealed class PanelGeometry
{
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [JsonIgnore]
    public bool IsSet => Width > 0 && Height > 0;
}

public sealed class AppConfig
{
    /// <summary>Stable monitor id (device path) the SensorPanel belongs to.</summary>
    public string PanelMonitorId { get; set; } = string.Empty;

    /// <summary>Stable monitor id foreign windows are pushed to. Empty means "system primary".</summary>
    public string HostMonitorId { get; set; } = string.Empty;

    /// <summary>Match rule for the AIDA64 SensorPanel window. Editable without recompiling.</summary>
    public WindowRule PanelRule { get; set; } = new()
    {
        ProcessName = "aida64",
        ClassPattern = "*SensorPanel*",
        TitlePattern = string.Empty,
    };

    public PanelGeometry PanelGeometry { get; set; } = new();

    public bool KeepPanelPosition { get; set; } = true;

    public bool RestorePanelSize { get; set; }

    public bool KeepPanelTopmost { get; set; }

    public bool HidePanelFromAltTab { get; set; }

    public bool RelocateForeignWindows { get; set; } = true;

    /// <summary>Windows matching any of these are never moved.</summary>
    public List<WindowRule> Exceptions { get; set; } = new();

    public int SafetyPollSeconds { get; set; } = 5;

    public int EventDebounceMs { get; set; } = 150;

    public int DisplayChangeDelayMs { get; set; } = 2000;

    /// <summary>
    /// Applications commonly place their window several times while starting up; a limit of 3 was
    /// low enough to abandon Steam 1.4 s into its launch.
    /// </summary>
    public int PingPongLimit { get; set; } = 5;

    public int PingPongWindowSeconds { get; set; } = 10;

    public bool VerboseLogging { get; set; }

    public bool StartPaused { get; set; }

    public AppConfig Clone()
    {
        return new AppConfig
        {
            PanelMonitorId = PanelMonitorId,
            HostMonitorId = HostMonitorId,
            PanelRule = new WindowRule
            {
                ProcessName = PanelRule.ProcessName,
                ClassPattern = PanelRule.ClassPattern,
                TitlePattern = PanelRule.TitlePattern,
            },
            PanelGeometry = new PanelGeometry
            {
                OffsetX = PanelGeometry.OffsetX,
                OffsetY = PanelGeometry.OffsetY,
                Width = PanelGeometry.Width,
                Height = PanelGeometry.Height,
            },
            KeepPanelPosition = KeepPanelPosition,
            RestorePanelSize = RestorePanelSize,
            KeepPanelTopmost = KeepPanelTopmost,
            HidePanelFromAltTab = HidePanelFromAltTab,
            RelocateForeignWindows = RelocateForeignWindows,
            Exceptions = Exceptions
                .Select(e => new WindowRule
                {
                    ProcessName = e.ProcessName,
                    ClassPattern = e.ClassPattern,
                    TitlePattern = e.TitlePattern,
                })
                .ToList(),
            SafetyPollSeconds = SafetyPollSeconds,
            EventDebounceMs = EventDebounceMs,
            DisplayChangeDelayMs = DisplayChangeDelayMs,
            PingPongLimit = PingPongLimit,
            PingPongWindowSeconds = PingPongWindowSeconds,
            VerboseLogging = VerboseLogging,
            StartPaused = StartPaused,
        };
    }

    /// <summary>Clamps values that would make the service misbehave (busy loops, zero timers).</summary>
    public void Normalize()
    {
        SafetyPollSeconds = Math.Clamp(SafetyPollSeconds, 1, 600);
        EventDebounceMs = Math.Clamp(EventDebounceMs, 50, 5000);
        DisplayChangeDelayMs = Math.Clamp(DisplayChangeDelayMs, 0, 60000);
        PingPongLimit = Math.Clamp(PingPongLimit, 1, 50);
        PingPongWindowSeconds = Math.Clamp(PingPongWindowSeconds, 1, 600);
        PanelRule ??= new WindowRule();
        PanelGeometry ??= new PanelGeometry();
        Exceptions ??= new List<WindowRule>();
    }
}
