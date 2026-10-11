using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace SensorPanelPin.Localization;

/// <summary>
/// Every text the user sees, in English (Strings.resx) and Polish (Strings.pl.resx), picked by the
/// Windows display language. Written out rather than generated at build time: WPF compiles XAML
/// against a temporary assembly that does not see build-generated code, and the pages bind to these
/// members with x:Static. A test checks that every member has a key in both files with the same
/// placeholders. To add a text, add the key to both .resx files and a member here.
///
/// The log stays English whatever the language, so anyone can read a log pasted into an issue.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("SensorPanelPin.Localization.Strings", typeof(Strings).Assembly);

    internal static ResourceManager ResourceManager => Manager;

    private static string Get([CallerMemberName] string key = "") =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key, args);

    // ------------------------------------------------------------------ Settings window
    public static string NavSections => Get();
    public static string ScreensTitle => Get();
    public static string OtherWindowsTitle => Get();
    public static string StartupTitle => Get();
    public static string VersionFormat(object? version) => Format(nameof(VersionFormat), version);
    public static string ToggleOn => Get();
    public static string ToggleOff => Get();

    // ------------------------------------------------------------------ Screens page
    public static string ScreensMapName => Get();
    public static string ScreenRolesHeader => Get();
    public static string PanelScreenHeader => Get();
    public static string PanelScreenDescription => Get();
    public static string HostScreenHeader => Get();
    public static string HostScreenDescription => Get();
    public static string ScreenNumbersHeader => Get();
    public static string ScreenNumbersDescription => Get();
    public static string IdentifyButton => Get();
    public static string MainWindowsScreen => Get();
    public static string RememberedScreenDisconnected => Get();
    public static string StatusInPlaceFormat(object? screen) => Format(nameof(StatusInPlaceFormat), screen);
    public static string StatusReturning => Get();
    public static string StatusElevation => Get();
    public static string StatusElevationNotInstalled => Get();
    public static string RunAsAdministratorButton => Get();
    public static string StatusChooseScreen => Get();
    public static string StatusWaitingForScreenFormat(object? reason) => Format(nameof(StatusWaitingForScreenFormat), reason);
    public static string StatusNoPanel => Get();
    public static string StatusPaused => Get();
    public static string ResumeButton => Get();
    public static string StatusStarting => Get();
    public static string SaveFailed => Get();

    // ------------------------------------------------------------------ SensorPanel page
    public static string PositionHeader => Get();
    public static string KeepPositionHeader => Get();
    public static string KeepPositionDescription => Get();
    public static string RestoreSizeHeader => Get();
    public static string RestoreSizeDescription => Get();
    public static string RememberedPositionHeader => Get();
    public static string RememberCurrentButton => Get();
    public static string GeometryFormat(object? x, object? y, object? width, object? height) => Format(nameof(GeometryFormat), x, y, width, height);
    public static string GeometryNotSet => Get();
    public static string PositionRememberedFormat(object? screen) => Format(nameof(PositionRememberedFormat), screen);
    public static string WindowBehaviourHeader => Get();
    public static string TopmostHeader => Get();
    public static string TopmostDescription => Get();
    public static string HideFromAltTabHeader => Get();
    public static string HideFromAltTabDescription => Get();
    public static string AdvancedHeader => Get();
    public static string PanelRuleHeader => Get();
    public static string PanelRuleDescription => Get();
    public static string RuleProcessHeader => Get();
    public static string RuleProcessDescription => Get();
    public static string RuleClassHeader => Get();
    public static string RuleClassDescription => Get();
    public static string RuleTitleHeader => Get();
    public static string RuleTitleDescription => Get();
    public static string CheckMatchHeader => Get();
    public static string CheckMatchDescription => Get();
    public static string ShowAidaWindowsButton => Get();

    // ------------------------------------------------------------------ Other windows page
    public static string RelocateHeader => Get();
    public static string RelocateDescription => Get();
    public static string ExceptionsHeader => Get();
    public static string ExceptionsDescription => Get();
    public static string NoExceptions => Get();
    public static string RemoveException => Get();
    public static string ExceptionProcessName => Get();
    public static string ExceptionTitleLabel => Get();
    public static string ExceptionTitleName => Get();
    public static string AddExceptionButton => Get();
    public static string ExceptionAnyProgram => Get();
    public static string ExceptionAllWindows => Get();
    public static string ExceptionTitleFormat(object? title) => Format(nameof(ExceptionTitleFormat), title);
    public static string ProblemsHeader => Get();
    public static string ProblemsDescription => Get();
    public static string NoProblems => Get();
    public static string ForgiveButton => Get();
    public static string ProblemDescriptionFormat(object? process, object? title, object? windowClass) => Format(nameof(ProblemDescriptionFormat), process, title, windowClass);

    // ------------------------------------------------------------------ Startup page
    public static string WithWindowsHeader => Get();
    public static string AutoStartOff => Get();
    public static string AutoStartRegistry => Get();
    public static string AutoStartRegistryDetail => Get();
    public static string AutoStartElevated => Get();
    public static string AutoStartElevatedDetail => Get();
    public static string StartPausedHeader => Get();
    public static string StartPausedDescription => Get();
    public static string DiagnosticsHeader => Get();
    public static string VerboseLogHeader => Get();
    public static string VerboseLogDescription => Get();
    public static string LogAndSettingsHeader => Get();
    public static string OpenLogButton => Get();
    public static string OpenSettingsFolderButton => Get();
    public static string TimingsHeader => Get();
    public static string TimingsDescription => Get();
    public static string SafetyPollHeader => Get();
    public static string SafetyPollDescription => Get();
    public static string SafetyPollName => Get();
    public static string DebounceHeader => Get();
    public static string DebounceDescription => Get();
    public static string DebounceName => Get();
    public static string DisplayDelayHeader => Get();
    public static string DisplayDelayDescription => Get();
    public static string DisplayDelayName => Get();
    public static string PingPongLimitHeader => Get();
    public static string PingPongLimitDescription => Get();
    public static string PingPongLimitName => Get();
    public static string PingPongLimitUnit => Get();
    public static string PingPongWindowHeader => Get();
    public static string PingPongWindowDescription => Get();
    public static string PingPongWindowName => Get();
    public static string LocationWarningFormat(object? reason) => Format(nameof(LocationWarningFormat), reason);

    // ------------------------------------------------------------------ Autostart
    public static string AutoStartNoExecutableFormat(object? executable) => Format(nameof(AutoStartNoExecutableFormat), executable);
    public static string AutoStartUnknownMode => Get();
    public static string AutoStartRegistryOpenFailed => Get();
    public static string AutoStartRegistryWriteFailed => Get();
    public static string AutoStartNeedsElevation => Get();
    public static string AutoStartUntrustedFormat(object? reason) => Format(nameof(AutoStartUntrustedFormat), reason);
    public static string AutoStartTaskNotVisible => Get();
    public static string AutoStartTaskCreateFailed => Get();
    public static string AutoStartRemoveNeedsElevation => Get();
    public static string AutoStartTaskRemoveFailed => Get();

    // ------------------------------------------------------------------ Protected location check
    public static string TrustFileMissingFormat(object? path) => Format(nameof(TrustFileMissingFormat), path);
    public static string TrustUnreadableFormat(object? error) => Format(nameof(TrustUnreadableFormat), error);
    public static string TrustOwnerFormat(object? path, object? owner) => Format(nameof(TrustOwnerFormat), path, owner);
    public static string TrustWritableFormat(object? path, object? account, object? rights) => Format(nameof(TrustWritableFormat), path, account, rights);

    // ------------------------------------------------------------------ Elevation
    public static string ElevationNoExecutable => Get();
    public static string ElevationCancelled => Get();
    public static string ElevationFailed => Get();

    // ------------------------------------------------------------------ Engine status
    public static string ServiceNotStarted => Get();
    public static string ServicePausedByUser => Get();
    public static string MeasureNoPanel => Get();
    public static string MeasureNoMonitor => Get();
    public static string MeasuredFormat(object? screen) => Format(nameof(MeasuredFormat), screen);
    public static string StatusPanelNotFound => Get();
    public static string StatusNoScreenChosen => Get();
    public static string StatusTargetUnavailable => Get();
    public static string StatusLayoutChanging => Get();
    public static string StatusCoordinatesUnreliable => Get();
    public static string StatusBackoff => Get();
    public static string StatusNeedsElevation => Get();
    public static string StatusPinnedFormat(object? screen) => Format(nameof(StatusPinnedFormat), screen);
    public static string StateStopped => Get();
    public static string StatePaused => Get();
    public static string StateWaitingForMonitor => Get();
    public static string StateWaitingForPanel => Get();
    public static string StateElevationRequired => Get();
    public static string StateActive => Get();

    // ------------------------------------------------------------------ Tray
    public static string TrayOpenSettings => Get();
    public static string TrayFixNow => Get();
    public static string TrayPause => Get();
    public static string TrayResume => Get();
    public static string TrayRestartElevated => Get();
    public static string TrayExit => Get();
    public static string TrayLogDisabledTitle => Get();
    public static string TrayLogDisabledFormat(object? reason) => Format(nameof(TrayLogDisabledFormat), reason);
    public static string TrayElevationTitle => Get();
    public static string TrayElevationText => Get();

    // ------------------------------------------------------------------ AIDA64 windows dialog
    public static string DiagnosticsWindowTitle => Get();
    public static string DiagnosticsIntro => Get();
    public static string CopyButton => Get();
    public static string CloseButton => Get();

    // ------------------------------------------------------------------ Startup messages
    public static string AlreadyRunning => Get();
    public static string LegacyRunning => Get();
    public static string FatalErrorFormat(object? logPath) => Format(nameof(FatalErrorFormat), logPath);

    // ------------------------------------------------------------------ Install and uninstall
    public static string InstallButton => Get();
    public static string TrayInstall => Get();
    public static string InstallNeedsAdministrator => Get();
    public static string InstallDoneFormat(object? folder) => Format(nameof(InstallDoneFormat), folder);
    public static string InstallFailedFormat(object? reason) => Format(nameof(InstallFailedFormat), reason);
    public static string InstallUntrustedFormat(object? reason) => Format(nameof(InstallUntrustedFormat), reason);
    public static string UninstallHeader => Get();
    public static string UninstallDescription => Get();
    public static string UninstallButton => Get();
    public static string UninstallAskSettings => Get();
    public static string UninstallDone => Get();
    public static string UninstallDoneAfterRestart => Get();
    public static string UninstallFailedFormat(object? reason) => Format(nameof(UninstallFailedFormat), reason);
    public static string OfferInstallInPlaceFormat(object? version) => Format(nameof(OfferInstallInPlaceFormat), version);
}
