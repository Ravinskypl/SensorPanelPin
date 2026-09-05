using System.Windows.Forms;
using SensorPanelGlue.Config;
using SensorPanelGlue.Diagnostics;
using SensorPanelGlue.UI;

namespace SensorPanelGlue;

internal static class Program
{
    private const string MutexName = @"Local\SensorPanelGlue.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "SensorPanelGlue już działa — ikona znajduje się w zasobniku systemowym.",
                "SensorPanelGlue",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // DPI awareness is declared in app.manifest; only visual styles are configured here.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var log = new Logger(Path.Combine(ConfigStore.DefaultAppDataDirectory, "logs"));
        var store = new ConfigStore(log);
        var config = store.Load();

        log.MinimumLevel = config.VerboseLogging || args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)
            ? LogLevel.Debug
            : LogLevel.Info;

        log.Info("=== SensorPanelGlue start ===");
        log.Info("Wersja: " + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "?"));

        Application.ThreadException += (_, e) =>
            log.Error("Nieobsłużony wyjątek w wątku UI.", e.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                log.Error("Nieobsłużony wyjątek.", ex);
            }
        };

        try
        {
            using var context = new TrayApplicationContext(log, store, config);
            Application.Run(context);
        }
        catch (Exception ex)
        {
            log.Error("Aplikacja zakończyła się błędem.", ex);
            MessageBox.Show(
                "SensorPanelGlue napotkał błąd i zostanie zamknięty.\n\nSzczegóły w logu:\n" + log.LogFilePath,
                "SensorPanelGlue",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        log.Info("=== SensorPanelGlue stop ===");
        GC.KeepAlive(mutex);
    }
}
