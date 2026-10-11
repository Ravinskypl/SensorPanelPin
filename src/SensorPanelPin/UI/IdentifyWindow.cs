using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SensorPanelPin.Monitors;
using static SensorPanelPin.Interop.NativeMethods;

namespace SensorPanelPin.UI;

/// <summary>
/// The "identify" badge Windows shows in Display settings: a large number in the lower-left corner
/// of each screen, matching the numbers on the map.
/// </summary>
public sealed class IdentifyWindow : Window
{
    private const double BadgeWidth = 200;
    private const double BadgeHeight = 150;
    private const double EdgeMargin = 32;

    private readonly MonitorInfo _monitor;

    private IdentifyWindow(MonitorInfo monitor, int number)
    {
        _monitor = monitor;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        Width = BadgeWidth;
        Height = BadgeHeight;

        var numberText = new TextBlock
        {
            Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 72,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            Margin = new Thickness(24, 6, 24, 0),
        };
        numberText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var nameText = new TextBlock
        {
            Text = monitor.FriendlyName,
            FontSize = 12,
            Margin = new Thickness(26, 0, 24, 18),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        stack.Children.Add(numberText);
        stack.Children.Add(nameText);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        badge.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        Content = badge;
        SourceInitialized += (_, _) => PlaceOnMonitor();
    }

    /// <summary>Shows a badge on every screen for three seconds.</summary>
    public static void ShowOnAll(IReadOnlyList<MonitorInfo> monitors)
    {
        var windows = monitors.Select((m, i) => new IdentifyWindow(m, i + 1)).ToList();
        foreach (var window in windows)
        {
            window.Show();
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var window in windows)
            {
                window.Close();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// Positioned in physical pixels through the handle: WPF's own Left/Top are in the DPI of the
    /// screen the window starts on, which is wrong for every other screen in a mixed-DPI setup.
    /// </summary>
    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = _monitor.Dpi / 96.0;
        var width = (int)(BadgeWidth * scale);
        var height = (int)(BadgeHeight * scale);
        var x = _monitor.WorkArea.Left + (int)(EdgeMargin * scale);
        var y = _monitor.WorkArea.Bottom - height - (int)(EdgeMargin * scale);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
    }
}
