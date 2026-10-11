using System.Windows;
using System.Windows.Threading;
using SensorPanelGlue.Config;
using SensorPanelGlue.Core;
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
        if (!isFirstInstance && !WaitForPreviousInstance(mutex, args))
        {
            MessageBox.Show(
                "SensorPanelGlue już działa — ikona znajduje się w zasobniku systemowym.",
                "SensorPanelGlue",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        using var log = new Logger(Path.Combine(ConfigStore.DefaultAppDataDirectory, "logs"));
        var store = new ConfigStore(log);
        var config = store.Load();

        log.MinimumLevel = config.VerboseLogging || args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)
            ? LogLevel.Debug
            : LogLevel.Info;

        log.Info("=== SensorPanelGlue start ===");
        log.Info("Wersja: " + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "?"));

        // Fluent: the Windows 11 look, following the system light/dark setting and accent colour.
        var app = new Application
        {
            ThemeMode = ThemeMode.System,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SensorPanelGlue;component/UI/Theme.xaml", UriKind.Absolute),
        });

        app.DispatcherUnhandledException += (_, e) =>
        {
            log.Error("Nieobsłużony wyjątek w wątku UI.", e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                log.Error("Nieobsłużony wyjątek.", ex);
            }
        };

        TrayController? tray = null;
        try
        {
            // Created inside the running loop: the service needs the dispatcher's context to post to.
            app.Startup += (_, _) => tray = new TrayController(log, store, config);
            app.Run();
        }
        catch (Exception ex)
        {
            log.Error("Aplikacja zakończyła się błędem.", ex);
            MessageBox.Show(
                "SensorPanelGlue napotkał błąd i zostanie zamknięty.\n\nSzczegóły w logu:\n" + log.LogFilePath,
                "SensorPanelGlue",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            tray?.Dispose();
        }

        log.Info("=== SensorPanelGlue stop ===");
        GC.KeepAlive(mutex);
    }

    /// <summary>
    /// After "restart as administrator" the old instance is still exiting when the new one starts;
    /// without waiting, the new one would see the mutex taken and quit.
    /// </summary>
    private static bool WaitForPreviousInstance(Mutex mutex, string[] args)
    {
        if (!args.Contains(Elevation.AfterRestartArgument, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(15));
        }
        catch (AbandonedMutexException)
        {
            // The previous instance ended without releasing it: ownership passes to us.
            return true;
        }
    }
}
