using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SensorPanelPin.UI;

/// <summary>A monitor as drawn on the map. Bounds are physical desktop pixels.</summary>
public sealed record MapMonitor(int Number, string Name, Rect Bounds, bool IsPanelScreen);

public enum MapPanelState
{
    /// <summary>The panel window was not found.</summary>
    Missing,

    /// <summary>The panel sits where it belongs.</summary>
    InPlace,

    /// <summary>The panel is somewhere else - drawn there, with its intended place outlined.</summary>
    Away,
}

/// <summary>
/// The user's desk, to scale: every monitor in its real arrangement, with the SensorPanel drawn
/// where it actually is. This is the one picture that answers "is my panel where it should be?" -
/// when it is not, the panel shows up on the wrong screen in the caution colour and its proper
/// place is outlined, so the problem is visible before any text is read.
/// </summary>
public sealed class MonitorMap : FrameworkElement
{
    private const double Gap = 3;
    private const double ScreenRadius = 6;

    public static readonly DependencyProperty MonitorsProperty = Register(nameof(Monitors), typeof(IReadOnlyList<MapMonitor>), Array.Empty<MapMonitor>());
    public static readonly DependencyProperty PanelActualProperty = Register(nameof(PanelActual), typeof(Rect?), null);
    public static readonly DependencyProperty PanelDesiredProperty = Register(nameof(PanelDesired), typeof(Rect?), null);
    public static readonly DependencyProperty PanelStateProperty = Register(nameof(PanelState), typeof(MapPanelState), MapPanelState.Missing);

    public static readonly DependencyProperty ScreenFillProperty = Register(nameof(ScreenFill), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty ScreenStrokeProperty = Register(nameof(ScreenStroke), typeof(Brush), Brushes.DarkGray);
    public static readonly DependencyProperty TextProperty = Register(nameof(Text), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty SecondaryTextProperty = Register(nameof(SecondaryText), typeof(Brush), Brushes.DimGray);
    public static readonly DependencyProperty AccentProperty = Register(nameof(Accent), typeof(Brush), Brushes.DodgerBlue);
    public static readonly DependencyProperty TextOnAccentProperty = Register(nameof(TextOnAccent), typeof(Brush), Brushes.White);
    public static readonly DependencyProperty CautionProperty = Register(nameof(Caution), typeof(Brush), Brushes.Goldenrod);

    public IReadOnlyList<MapMonitor> Monitors
    {
        get => (IReadOnlyList<MapMonitor>)GetValue(MonitorsProperty);
        set => SetValue(MonitorsProperty, value);
    }

    public Rect? PanelActual
    {
        get => (Rect?)GetValue(PanelActualProperty);
        set => SetValue(PanelActualProperty, value);
    }

    public Rect? PanelDesired
    {
        get => (Rect?)GetValue(PanelDesiredProperty);
        set => SetValue(PanelDesiredProperty, value);
    }

    public MapPanelState PanelState
    {
        get => (MapPanelState)GetValue(PanelStateProperty);
        set => SetValue(PanelStateProperty, value);
    }

    public Brush ScreenFill { get => (Brush)GetValue(ScreenFillProperty); set => SetValue(ScreenFillProperty, value); }

    public Brush ScreenStroke { get => (Brush)GetValue(ScreenStrokeProperty); set => SetValue(ScreenStrokeProperty, value); }

    public Brush Text { get => (Brush)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public Brush SecondaryText { get => (Brush)GetValue(SecondaryTextProperty); set => SetValue(SecondaryTextProperty, value); }

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public Brush TextOnAccent { get => (Brush)GetValue(TextOnAccentProperty); set => SetValue(TextOnAccentProperty, value); }

    public Brush Caution { get => (Brush)GetValue(CautionProperty); set => SetValue(CautionProperty, value); }

    // Every property repaints: brushes arrive through DynamicResource, so a switch between light and
    // dark lands here as a property change and redraws the map in the new theme.
    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(MonitorMap),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override void OnRender(DrawingContext dc)
    {
        var monitors = Monitors;
        if (monitors.Count == 0 || ActualWidth < 40 || ActualHeight < 40)
        {
            return;
        }

        var union = monitors.Select(m => m.Bounds).Aggregate(Rect.Union);
        var scale = Math.Min((ActualWidth - 2) / union.Width, (ActualHeight - 2) / union.Height);
        var offset = new Vector(
            ((ActualWidth - union.Width * scale) / 2) - union.X * scale,
            ((ActualHeight - union.Height * scale) / 2) - union.Y * scale);

        Rect ToMap(Rect desk) => new(desk.X * scale + offset.X, desk.Y * scale + offset.Y, desk.Width * scale, desk.Height * scale);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var strokePen = new Pen(ScreenStroke, 1);
        var accentPen = new Pen(Accent, 2);

        foreach (var monitor in monitors)
        {
            var box = Deflate(ToMap(monitor.Bounds), Gap);
            dc.DrawRoundedRectangle(ScreenFill, monitor.IsPanelScreen ? accentPen : strokePen, box, ScreenRadius, ScreenRadius);
        }

        // The panel, clipped to the desk so a few pixels hanging off a screen edge do not smear
        // across the gap between monitors.
        var panelScreen = monitors.FirstOrDefault(m => m.IsPanelScreen);
        switch (PanelState)
        {
            case MapPanelState.InPlace when PanelActual is { } actual && panelScreen is not null:
                DrawPanel(dc, ToMap(Rect.Intersect(actual, panelScreen.Bounds)), Accent);
                break;

            case MapPanelState.Away when PanelActual is { } actual:
                if (PanelDesired is { } desired && panelScreen is not null)
                {
                    var outline = Deflate(ToMap(Rect.Intersect(desired, panelScreen.Bounds)), Gap + 3);
                    var dashed = new Pen(Accent, 1.5) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
                    dc.DrawRoundedRectangle(null, dashed, outline, 3, 3);
                }

                var where = Rect.Intersect(actual, union);
                if (!where.IsEmpty)
                {
                    // Translucent with a firm outline: clearly a warning, without a solid block of
                    // yellow shouting from the dark theme.
                    var stray = Deflate(ToMap(where), Gap + 3);
                    dc.PushOpacity(0.28);
                    dc.DrawRoundedRectangle(Caution, null, stray, 3, 3);
                    dc.Pop();
                    dc.DrawRoundedRectangle(null, new Pen(Caution, 2), stray, 3, 3);
                    DrawBars(dc, stray, Caution);
                }

                break;
        }

        // Labels last, so they stay readable on top of the panel.
        foreach (var monitor in monitors)
        {
            var box = Deflate(ToMap(monitor.Bounds), Gap);
            var onPanel = monitor.IsPanelScreen && PanelState == MapPanelState.InPlace;
            DrawLabels(dc, monitor, box, onPanel ? TextOnAccent : Text, onPanel ? TextOnAccent : SecondaryText, dpi);
        }
    }

    private void DrawPanel(DrawingContext dc, Rect area, Brush fill)
    {
        var panel = Deflate(area, Gap + 3);
        if (panel.Width < 8 || panel.Height < 8)
        {
            return;
        }

        dc.DrawRoundedRectangle(fill, null, panel, 3, 3);
        DrawBars(dc, panel, fill == Accent ? TextOnAccent : Text);
    }

    /// <summary>The sensor bars from the app icon: they make the shape read as "a panel of gauges".</summary>
    private static void DrawBars(DrawingContext dc, Rect panel, Brush onFill)
    {
        var barWidth = Math.Clamp(panel.Width / 16, 3, 7);
        var spacing = barWidth * 0.8;
        double[] heights = { 0.35, 0.6, 0.85, 0.5 };
        var totalWidth = heights.Length * barWidth + (heights.Length - 1) * spacing;
        var baseY = panel.Bottom - Math.Clamp(panel.Height * 0.16, 6, 16);
        var maxHeight = Math.Min(panel.Height * 0.36, 40);
        var x = panel.Right - totalWidth - Math.Clamp(panel.Width * 0.08, 6, 16);

        if (x < panel.Left + 4 || maxHeight < 6)
        {
            return;
        }

        foreach (var h in heights)
        {
            var bar = new Rect(x, baseY - maxHeight * h, barWidth, maxHeight * h);
            dc.PushOpacity(0.85);
            dc.DrawRoundedRectangle(onFill, null, bar, 1, 1);
            dc.Pop();
            x += barWidth + spacing;
        }
    }

    private static void DrawLabels(DrawingContext dc, MapMonitor monitor, Rect box, Brush primary, Brush secondary, double dpi)
    {
        var numberSize = Math.Clamp(box.Height * 0.26, 14, 40);
        var number = MakeText(monitor.Number.ToString(CultureInfo.InvariantCulture), numberSize, FontWeights.SemiBold, primary, dpi, display: true);
        var name = MakeText(monitor.Name, 12, FontWeights.Normal, primary, dpi);
        var size = MakeText($"{monitor.Bounds.Width:0} × {monitor.Bounds.Height:0}", 12, FontWeights.Normal, secondary, dpi);

        var left = box.Left + 12;
        var top = box.Top + 8;
        dc.DrawText(number, new Point(left, top));

        // Name and resolution only where they fit; the number alone still identifies the screen.
        var nameTop = box.Bottom - 10 - size.Height - name.Height;
        var widest = Math.Max(name.Width, size.Width);
        if (nameTop > top + number.Height && widest < box.Width - 20)
        {
            dc.DrawText(name, new Point(left, nameTop));
            dc.DrawText(size, new Point(left, nameTop + name.Height));
        }
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, double dpi, bool display = false) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(display ? "Segoe UI Variable Display, Segoe UI" : "Segoe UI Variable Text, Segoe UI"),
                FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, dpi);

    private static Rect Deflate(Rect rect, double by)
    {
        var width = Math.Max(0, rect.Width - 2 * by);
        var height = Math.Max(0, rect.Height - 2 * by);
        return new Rect(rect.X + by, rect.Y + by, width, height);
    }
}
