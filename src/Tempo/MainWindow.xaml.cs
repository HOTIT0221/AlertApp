using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tempo.Common;
using Tempo.Services;
using Tempo.Views;

namespace Tempo;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, (Button Button, FontIcon Icon, Type Page)> _nav;
    private string _selectedTag = "";

    public MainWindow()
    {
        InitializeComponent();

        Title = "Tempo";
        AppWindow.SetIcon(AppPaths.AppIconPath);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 920;
            presenter.PreferredMinimumHeight = 640;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1480, 920));

        _nav = new()
        {
            ["Alerts"] = (NavAlerts, NavAlertsIcon, typeof(AlertsPage)),
            ["Upcoming"] = (NavUpcoming, NavUpcomingIcon, typeof(UpcomingPage)),
            ["Completed"] = (NavCompleted, NavCompletedIcon, typeof(CompletedPage)),
            ["Settings"] = (NavSettings, NavSettingsIcon, typeof(SettingsPage)),
        };

        foreach (var (_, entry) in _nav)
        {
            entry.Button.PointerEntered += NavPointerEntered;
            entry.Button.PointerExited += NavPointerExited;
        }

        Select("Alerts");
    }

    public bool IsForeground => NativeWindow.IsForeground(this);

    public void BringToFront() => NativeWindow.BringToFront(this);

    private void OnNavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag)
            Select(tag);
    }

    public void Select(string tag)
    {
        if (!_nav.ContainsKey(tag)) return;
        _selectedTag = tag;

        foreach (var (key, entry) in _nav)
        {
            bool selected = key == tag;
            entry.Button.Background = selected ? Brush("AccentSoftBrush") : Transparent();
            entry.Button.Foreground = selected ? Brush("AccentBrush") : Brush("TextSecondaryBrush");
        }

        ContentFrame.Navigate(_nav[tag].Page, null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
    }

    private void NavPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag && tag != _selectedTag)
            b.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xF2, 0xF4, 0xF7));
    }

    private void NavPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag && tag != _selectedTag)
            b.Background = Transparent();
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static Brush Transparent() => new SolidColorBrush(Colors.Transparent);
}
