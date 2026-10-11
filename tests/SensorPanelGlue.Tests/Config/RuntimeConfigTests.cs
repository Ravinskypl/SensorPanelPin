using System.Text.Json;

namespace SensorPanelGlue.Tests.Config;

/// <summary>
/// Runtime switches the app is built with, read from the runtimeconfig.json that ships next to the exe.
/// </summary>
public sealed class RuntimeConfigTests
{
    [Fact]
    public void The_app_does_not_run_in_invariant_globalization_mode()
    {
        // WPF resolves a specific culture for every binding inside a control template. In invariant mode
        // that lookup throws, so every templated control fails to load and the settings window is empty.
        var path = Path.Combine(AppContext.BaseDirectory, "SensorPanelGlue.runtimeconfig.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var options = document.RootElement.GetProperty("runtimeOptions");
        var invariant = options.TryGetProperty("configProperties", out var properties)
            && properties.TryGetProperty("System.Globalization.Invariant", out var value)
            && value.GetBoolean();

        Assert.False(invariant);
    }
}
