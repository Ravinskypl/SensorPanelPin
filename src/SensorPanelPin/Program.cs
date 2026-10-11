using System.Windows;
using System.Windows.Threading;
using SensorPanelPin.Config;
using SensorPanelPin.Core;
using SensorPanelPin.Diagnostics;
using SensorPanelPin.Localization;
using SensorPanelPin.UI;

namespace SensorPanelPin;

internal static class Program
{
    private const string MutexName = @"Local\SensorPanelPin.SingleInstance";

    /// <summary>The single-instance mutex of the same program before it was renamed.</summary>
    private const string LegacyMutexName = @"Local\SensorPanelGlue.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance && !WaitForPreviousInstance(mutex, args))
        {
            MessageBox.Show(
                Strings.AlreadyRunning,
                "SensorPanelPin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (LegacyInstanceRunning())
        {
            MessageBox.Show(
                Strings.LegacyRunning,
                "SensorPanelPin",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        using var log = new Logger(Path.Combine(ConfigStore.DefaultAppDataDirectory, "logs"));
        var store = new ConfigStore(log);
        var config = store.Load();

        log.MinimumLevel = config.VerboseLogging || args.Contains("--verbose", StringComparer.OrdinalIgnoreCase)
            ? LogLevel.Debug
            : LogLevel.Info;

        log.Info("=== SensorPanelPin start ===");
        log.Info("Version: " + (typeof(Program).Assembly.GetName().Version?.ToString() ?? "?"));

        // Fluent: the Windows 11 look, following the system light/dark setting and accent colour.
        var app = new Application
        {
            ThemeMode = ThemeMode.System,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SensorPanelPin;component/UI/Theme.xaml", UriKind.Absolute),
        });

        app.DispatcherUnhandledException += (_, e) =>
        {
            log.Error("Unhandled exception on the UI thread.", e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                log.Error("Unhandled exception.", ex);
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
            log.Error("The application ended with an error.", ex);
            MessageBox.Show(
                Strings.FatalErrorFormat(log.LogFilePath),
                "SensorPanelPin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            tray?.Dispose();
        }

        log.Info("=== SensorPanelPin stop ===");
        GC.KeepAlive(mutex);
    }

    /// <summary>
    /// The renamed program does not share a mutex with its old name, so without this check an old
    /// SensorPanelGlue and a new SensorPanelPin would both enforce the layout and fight over windows.
    /// </summary>
    private static bool LegacyInstanceRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(LegacyMutexName, out var legacy))
            {
                legacy.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated old instance owns it: it exists, this process just may not open it.
            return true;
        }
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
