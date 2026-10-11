using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SensorPanelPin.UI.Pages;

namespace SensorPanelPin.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _model;
    private readonly DispatcherTimer _live;
    private readonly UserControl[] _pages;

    public SettingsWindow(SettingsViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;

        Icon = AppIcon.WindowSource;

        _pages = new UserControl[] { new ScreensPage(), new PanelPage(), new OtherWindowsPage(), new StartupPage() };
        Page.Content = _pages[0];

        // The map is live only while someone is looking at it: one cheap read a second, no enumeration.
        _live = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _model.Refresh(), Dispatcher);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _model.Refresh();
                _live.Start();
            }
            else
            {
                _live.Stop();
            }
        };
    }

    public void ShowPage(int index)
    {
        Navigation.SelectedIndex = Math.Clamp(index, 0, _pages.Length - 1);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _live.Stop();
        base.OnClosing(e);
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (_pages is null || Navigation.SelectedIndex < 0)
        {
            return;
        }

        Page.Content = _pages[Navigation.SelectedIndex];
        Scroller.ScrollToTop();
    }
}
