using System.Windows.Controls;

namespace SensorPanelGlue.UI.Pages;

// The pages are pure XAML bound to SettingsViewModel; the code-behind only loads the markup.

public partial class ScreensPage : UserControl
{
    public ScreensPage() => InitializeComponent();
}

public partial class PanelPage : UserControl
{
    public PanelPage() => InitializeComponent();
}

public partial class OtherWindowsPage : UserControl
{
    public OtherWindowsPage() => InitializeComponent();
}

public partial class StartupPage : UserControl
{
    public StartupPage() => InitializeComponent();
}
