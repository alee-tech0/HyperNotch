using System.Windows;
using System.Windows.Media;

namespace HyperNotch;

public partial class TrayMenuWindow : Window
{
    private readonly Action _onToggle;
    private readonly Action _onGameMode;
    private readonly Action _onSettings;
    private readonly Action _onExit;

    public TrayMenuWindow(string lang, Action onToggle, Action onGameMode, Action onSettings, Action onExit)
    {
        InitializeComponent();
        _onToggle = onToggle;
        _onGameMode = onGameMode;
        _onSettings = onSettings;
        _onExit = onExit;

        ToggleText.Text = Services.LocalizationService.Get(lang, "TrayShowHide");
        GameModeText.Text = Services.LocalizationService.Get(lang, "TrayGameMode");
        SettingsText.Text = Services.LocalizationService.Get(lang, "TraySettings");
        ExitText.Text = Services.LocalizationService.Get(lang, "TrayExit");

        Deactivated += (_, _) =>
        {
            try { Close(); } catch { }
        };
    }

    public void ShowAtCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        Opacity = 0;
        Loaded += (_, _) =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double x = cursor.X / dpi.DpiScaleX;
            double y = cursor.Y / dpi.DpiScaleY;

            var area = SystemParameters.WorkArea;
            double left = x - ActualWidth / 2.0;
            double top = y - ActualHeight + 4;

            if (left + ActualWidth > area.Right) left = area.Right - ActualWidth;
            if (left < area.Left) left = area.Left;
            if (top < area.Top) top = y;
            if (top + ActualHeight > area.Bottom) top = area.Bottom - ActualHeight;

            Left = left;
            Top = top;

            var ease = new System.Windows.Media.Animation.ExponentialEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Exponent = 4.5 };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fade, 240);
            BeginAnimation(OpacityProperty, fade);
        };
        Show();
        Activate();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onToggle();
    }

    private void GameMode_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onGameMode();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onSettings();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _onExit();
    }
}
