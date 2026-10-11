using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SensorPanelPin.UI;

/// <summary>The list of AIDA64 windows, with the one the panel rule matches marked.</summary>
public sealed class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(string report)
    {
        Title = "Okna AIDA64";
        Icon = AppIcon.WindowSource;
        Width = 980;
        Height = 560;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var intro = new TextBlock
        {
            Text = "Okno oznaczone „PASUJE DO REGUŁY” jest traktowane jako SensorPanel. Jeśli nic nie jest "
                   + "oznaczone, popraw pola „Jak rozpoznać okno panelu”.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

        var text = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
        };

        var copy = new Button { Content = "Kopiuj", MinWidth = 110 };
        copy.Click += (_, _) => Clipboard.SetText(report);
        var close = new Button { Content = "Zamknij", MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        if (TryFindResource("AccentButtonStyle") is Style accent)
        {
            close.Style = accent;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);

        var layout = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(intro);
        layout.Children.Add(buttons);
        layout.Children.Add(text);
        Content = layout;
    }
}
