using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HyperNotch.Models;
using HyperNotch.Services;
using Forms = System.Windows.Forms;

namespace HyperNotch;

public partial class MainWindow : Window
{
    private const double CompactWidth = 256;
    private const double CompactHeight = 40;
    private const double BannerWidth = 368;
    private const double BannerHeight = 70;
    private const double ExpandedWidth = 504;
    private const double ExpandedHeight = 226;

    private readonly MediaService _mediaService = new();
    private readonly SystemMonitorService _systemMonitor = new();
    private readonly AudioBrightnessService _audioBrightness = new();
    private readonly DeviceMonitorService _deviceMonitor = new();
    private readonly GameModeService _gameModeService = new();
    private readonly NotificationService _notificationService = new();

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _eqTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _countdownTimer;
    private readonly DispatcherTimer _hudHideTimer;

    private AppConfig _config;
    private IntPtr _hwnd;
    private bool _isExpanded;
    private bool _isBannerVisible;
    private bool _isPinned;
    private bool _isPlaying;
    private bool _hasMedia;
    private double _latestCpu;
    private double _eqPhase;

    private bool _manualGameMode;
    private bool _autoFullscreen;
    private bool _updatingVolumeSlider;
    private bool _isDraggingVolume;

    private int _timerTotalSecs;
    private int _timerRemainingSecs;
    private bool _timerRunning;

    private readonly List<string> _clipboardHistory = new(3);
    private Storyboard? _rgbStoryboard;

    public event Action? TimerFinished;
    public event Action? SettingsRequested;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            UpdateClock();
            PollClipboard();
        };
        _clockTimer.Start();

        _eqTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _eqTimer.Tick += (_, _) => AnimateEqualizer();
        _eqTimer.Start();

        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (!_isPinned && !IslandBorder.IsMouseOver && !PillHost.IsMouseOver)
            {
                Collapse();
            }
        };

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();

        _hudHideTimer = new DispatcherTimer();
        _hudHideTimer.Tick += (_, _) =>
        {
            _hudHideTimer.Stop();
            HideHudBanner();
        };

        WireServices();

        Loaded += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            GameModeService.SetNoActivate(_hwnd);
            ApplyConfig(_config);
            UpdateClock();
            PollClipboard();

            _mediaService.Start();
            _systemMonitor.Start();
            _audioBrightness.Start();
            var (initVol, initMuted) = _audioBrightness.GetCurrentVolume();
            UpdateVolumeSliderUI(initVol, initMuted);
            _deviceMonitor.Start();
            _gameModeService.Start(_hwnd);
            _notificationService.Start();
        };
    }

    private void WireServices()
    {
        _mediaService.MediaUpdated += info => Dispatcher.Invoke(() => UpdateMediaUI(info));
        _systemMonitor.StatsUpdated += stats => Dispatcher.Invoke(() => UpdateSystemUI(stats));

        _audioBrightness.VolumeChanged += (vol, muted) => Dispatcher.BeginInvoke(() =>
        {
            if (!_isDraggingVolume)
                UpdateVolumeSliderUI(vol, muted);
            string icon = muted || vol == 0 ? "🔇" : (vol < 45 ? "🔉" : "🔊");
            ShowProgressHud(icon, Services.LocalizationService.Get(_config.Language, muted ? "HudMuted" : "HudVolume"), vol, "#38BDF8", TimeSpan.FromSeconds(2.0));
        });

        _audioBrightness.BrightnessChanged += pct => Dispatcher.BeginInvoke(() =>
        {
            ShowProgressHud("☀️", Services.LocalizationService.Get(_config.Language, "HudBrightness"), pct, "#FBBF24", TimeSpan.FromSeconds(2.0));
        });

        _deviceMonitor.ChargerConnected += pct => Dispatcher.BeginInvoke(() =>
        {
            ShowProgressHud("⚡", Services.LocalizationService.Get(_config.Language, "HudCharger"), pct, "#34D399", TimeSpan.FromSeconds(2.5));
        });

        _deviceMonitor.BluetoothConnected += (name, bat) => Dispatcher.BeginInvoke(() =>
        {
            string icon = name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                          name.Contains("Controller", StringComparison.OrdinalIgnoreCase)
                ? "🎮"
                : "🎧";
            string sub = bat.HasValue
                ? string.Format(Services.LocalizationService.Get(_config.Language, "HudBtBattery"), bat.Value)
                : Services.LocalizationService.Get(_config.Language, "HudBtConnected");
            ShowNotificationHud(icon, name, sub, null, bat.HasValue ? $"{bat.Value}%" : "BT", "#60A5FA", TimeSpan.FromSeconds(3.0));
        });

        _gameModeService.FullscreenChanged += fs => Dispatcher.BeginInvoke(() =>
        {
            _autoFullscreen = fs;
            ApplyGameModeState();
        });

        _notificationService.NotificationReceived += notif => Dispatcher.BeginInvoke(() =>
        {
            ShowNotificationHud("🔔", $"{notif.AppName} • {notif.Title}", notif.Body, notif.AppIcon, "", "#A855F7", TimeSpan.FromSeconds(3.5));
        });
    }

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        ApplyTheme(config.Theme);
        ApplyLanguage(config.Language);
        ApplyGameModeState();
        double activeRadius = _isExpanded ? 26 : _isBannerVisible ? 24 : 20;
        double activeW = _isExpanded ? ExpandedWidth : _isBannerVisible ? BannerWidth : CompactWidth;
        double activeH = _isExpanded ? ExpandedHeight : _isBannerVisible ? BannerHeight : CompactHeight;
        ApplyIslandCornerRadius(activeRadius);
        double clipX = (ExpandedWidth - activeW) / 2.0;
        double clipY = _config.NotchMode ? -activeRadius : 0;
        double clipH = _config.NotchMode ? activeH + activeRadius : activeH;
        PillClipRect.BeginAnimation(RectangleGeometry.RectProperty, null);
        PillClipRect.Rect = new Rect(clipX, clipY, activeW, clipH);
        RepositionImmediate();
    }

    private void ApplyLanguage(string lang)
    {
        TabMedia.Content = Services.LocalizationService.Get(lang, "TabMedia");
        TabSystem.Content = Services.LocalizationService.Get(lang, "TabSystem");
        TabTimer.Content = Services.LocalizationService.Get(lang, "TabTimer");
        TabActions.Content = Services.LocalizationService.Get(lang, "TabActions");
        TabUtility.Content = Services.LocalizationService.Get(lang, "TabCalc");
        CalcCopyBtn.Content = Services.LocalizationService.Get(lang, "CalcCopy");
        CalcPlaceholder.Text = Services.LocalizationService.Get(lang, "CalcPlaceholder");
        GameModeQuickBtn.ToolTip = Services.LocalizationService.Get(lang, "TooltipGameMode");
        OpenSettingsBtn.ToolTip = Services.LocalizationService.Get(lang, "TooltipSettings");
        VolumeMuteBtn.ToolTip = Services.LocalizationService.Get(lang, "TooltipMute");
        if (_timerRunning)
            TimerToggleBtn.Content = Services.LocalizationService.Get(lang, "TimerPause");
        else if (_timerRemainingSecs > 0)
            TimerToggleBtn.Content = Services.LocalizationService.Get(lang, "TimerResume");
        else
            TimerToggleBtn.Content = Services.LocalizationService.Get(lang, "TimerStart");
        TimerResetBtn.Content = Services.LocalizationService.Get(lang, "TimerReset");
        RecentClipsTitle.Text = Services.LocalizationService.Get(lang, "RecentClips");
        ClickToCopyLabel.Text = Services.LocalizationService.Get(lang, "ClickToCopy");
        EmptyClipLabel.Text = Services.LocalizationService.Get(lang, "EmptyClip");
        ActMuteLabel.Text = Services.LocalizationService.Get(lang, "ActMute");
        ActSnipLabel.Text = Services.LocalizationService.Get(lang, "ActSnip");
        ActLockLabel.Text = Services.LocalizationService.Get(lang, "ActLock");
        ActAudioOutLabel.Text = Services.LocalizationService.Get(lang, "ActAudioOut");
        ActPickerLabel.Text = Services.LocalizationService.Get(lang, "ActPicker");
        ActOptionsLabel.Text = Services.LocalizationService.Get(lang, "ActOptions");
        ClearClipsBtn.ToolTip = Services.LocalizationService.Get(lang, "ActClearClips");
        if (!_hasMedia)
        {
            MediaTitleText.Text = Services.LocalizationService.Get(lang, "NoMediaTitle");
            MediaArtistText.Text = Services.LocalizationService.Get(lang, "NoMediaArtist");
        }
        var (vol, muted) = _audioBrightness.GetCurrentVolume();
        UpdateVolumeSliderUI(vol, muted);
        RefreshCompactStatus();
    }

    public void ApplyTheme(IslandTheme theme)
    {
        _rgbStoryboard?.Stop();
        _rgbStoryboard = null;
        GlossOverlay.Visibility = Visibility.Collapsed;
        CustomBannerLayer.Visibility = Visibility.Collapsed;
        CustomBannerLayer.Background = null;

        switch (theme)
        {
            case IslandTheme.OledBlack:
                IslandBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#050507"));
                IslandBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F1F23"));
                ApplyThemePalette(
                    cardBg: "#0D0D12", cardBorder: "#1E1E26",
                    tabBg: "#0E0E14", tabActive: "#22222C",
                    btnBg: "#15151C", btnHover: "#22222D", btnBorder: "#262632",
                    trackBg: "#1F1F28", accentText: "#FFFFFF", secondaryText: "#9A9AA4");
                break;

            case IslandTheme.AcrylicGlass:
                IslandBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D615151C"));
                IslandBorder.BorderBrush = new LinearGradientBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#60FFFFFF"),
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#18FFFFFF"),
                    new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
                GlossOverlay.Visibility = Visibility.Visible;
                GlossOverlay.Opacity = 0.35;
                ApplyThemePalette(
                    cardBg: "#28FFFFFF", cardBorder: "#34FFFFFF",
                    tabBg: "#1EFFFFFF", tabActive: "#3EFFFFFF",
                    btnBg: "#24FFFFFF", btnHover: "#38FFFFFF", btnBorder: "#38FFFFFF",
                    trackBg: "#30FFFFFF", accentText: "#F8FAFC", secondaryText: "#CBD5E1");
                break;

            case IslandTheme.FrutigerAero:
                var aeroBg = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1)
                };
                aeroBg.GradientStops.Add(new GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E80284C7"), 0.0));
                aeroBg.GradientStops.Add(new GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E80D9488"), 0.55));
                aeroBg.GradientStops.Add(new GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E80369A1"), 1.0));
                IslandBorder.Background = aeroBg;
                IslandBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#B0FFFFFF"));
                GlossOverlay.Visibility = Visibility.Visible;
                GlossOverlay.Opacity = 0.95;
                ApplyThemePalette(
                    cardBg: "#38FFFFFF", cardBorder: "#72FFFFFF",
                    tabBg: "#2CFFFFFF", tabActive: "#58FFFFFF",
                    btnBg: "#32FFFFFF", btnHover: "#4EFFFFFF", btnBorder: "#68FFFFFF",
                    trackBg: "#400C4A6E", accentText: "#FFFFFF", secondaryText: "#E0F2FE");
                break;

            case IslandTheme.RgbBorder:
                IslandBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#09090C"));
                var rgbBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1)
                };
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.0));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Magenta, 0.33));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.OrangeRed, 0.66));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Lime, 1.0));

                var rotate = new RotateTransform(0, 0.5, 0.5);
                rgbBrush.RelativeTransform = rotate;
                IslandBorder.BorderBrush = rgbBrush;

                var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.5))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Timeline.SetDesiredFrameRate(anim, 240);
                _rgbStoryboard = new Storyboard();
                _rgbStoryboard.Children.Add(anim);
                Storyboard.SetTarget(anim, IslandBorder);
                Storyboard.SetTargetProperty(anim, new PropertyPath("BorderBrush.RelativeTransform.Angle"));
                _rgbStoryboard.Begin();
                ApplyThemePalette(
                    cardBg: "#12121A", cardBorder: "#2A2438",
                    tabBg: "#13131C", tabActive: "#2B2440",
                    btnBg: "#171722", btnHover: "#262238", btnBorder: "#322A46",
                    trackBg: "#222030", accentText: "#F5F3FF", secondaryText: "#B8B5C8");
                break;

            case IslandTheme.Custom:
                var bgColor = ParseColorOrDefault(_config.CustomBackgroundHex, "#B91C1C");
                var borderColor = ParseColorOrDefault(_config.CustomBorderHex, "#FCA5A5");
                var accentHex = string.IsNullOrWhiteSpace(_config.CustomAccentHex) ? "#FDE047" : _config.CustomAccentHex;
                IslandBorder.Background = new SolidColorBrush(bgColor);
                IslandBorder.BorderBrush = new SolidColorBrush(borderColor);
                ApplyCustomBanner(_config.CustomBannerImage, _config.CustomBannerOpacity);
                ApplyThemePalette(
                    cardBg: "#52000000", cardBorder: "#48FFFFFF",
                    tabBg: "#44000000", tabActive: "#68FFFFFF",
                    btnBg: "#4A000000", btnHover: "#66000000", btnBorder: "#45FFFFFF",
                    trackBg: "#44000000", accentText: accentHex, secondaryText: "#F3F4F6");
                break;
        }

        RefreshCompactStatus();
    }

    private void ApplyCustomBanner(string bannerSpec, double opacity)
    {
        if (string.IsNullOrWhiteSpace(bannerSpec) || bannerSpec == "none")
        {
            CustomBannerLayer.Visibility = Visibility.Collapsed;
            CustomBannerLayer.Background = null;
            return;
        }

        try
        {
            Uri? uri = bannerSpec == "preset:gtavi"
                ? new Uri("pack://application:,,,/Assets/gtavi_banner.png", UriKind.Absolute)
                : System.IO.File.Exists(bannerSpec)
                    ? new Uri(bannerSpec, UriKind.Absolute)
                    : null;

            if (uri is null)
            {
                CustomBannerLayer.Visibility = Visibility.Collapsed;
                return;
            }

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();

            CustomBannerLayer.Background = new ImageBrush(bmp)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Top,
                Opacity = Math.Clamp(opacity, 0.15, 1.0)
            };
            CustomBannerLayer.Visibility = Visibility.Visible;
        }
        catch
        {
            CustomBannerLayer.Visibility = Visibility.Collapsed;
        }
    }

    private static System.Windows.Media.Color ParseColorOrDefault(string hex, string fallback)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback);
        }
    }

    private void ApplyThemePalette(
        string cardBg, string cardBorder,
        string tabBg, string tabActive,
        string btnBg, string btnHover, string btnBorder,
        string trackBg, string accentText, string secondaryText)
    {
        SetThemeResource("SurfaceCardBrush", cardBg);
        SetThemeResource("SurfaceCardBorderBrush", cardBorder);
        SetThemeResource("TabBarBgBrush", tabBg);
        SetThemeResource("TabActiveBgBrush", tabActive);
        SetThemeResource("ButtonBgBrush", btnBg);
        SetThemeResource("ButtonHoverBrush", btnHover);
        SetThemeResource("ButtonBorderBrush", btnBorder);
        SetThemeResource("TrackBgBrush", trackBg);
        SetThemeResource("AccentTextBrush", accentText);
        SetThemeResource("SecondaryTextBrush", secondaryText);

        var secondaryBrush = (SolidColorBrush)Resources["SecondaryTextBrush"];
        ExpandedClockText.Foreground = secondaryBrush;
        MediaArtistText.Foreground = secondaryBrush;
    }

    private void SetThemeResource(string key, string hex) =>
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private bool IsGameModeEffective => _manualGameMode || (_config.AutoGameMode && _autoFullscreen);

    private void ApplyGameModeState()
    {
        bool active = IsGameModeEffective;
        GameModeQuickBtn.Foreground = active
            ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#38BDF8"))
            : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8E8E98"));

        if (active)
        {
            if (_isExpanded) Collapse();
            if (_isBannerVisible) HideHudBanner();
            GameModeService.SetClickThrough(_hwnd, true);
            PillHost.Opacity = 0.18;
        }
        else
        {
            GameModeService.SetClickThrough(_hwnd, false);
            PillHost.Opacity = 1.0;
        }
    }

    private (double Left, double Top) GetTargetOrigin(double targetWidth)
    {
        var screens = Forms.Screen.AllScreens;
        if (screens.Length > 0)
        {
            int idx = Math.Clamp(_config.MonitorIndex, 0, screens.Length - 1);
            var bounds = screens[idx].WorkingArea;

            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is not null)
            {
                dpiScaleX = source.CompositionTarget.TransformFromDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformFromDevice.M22;
            }

            double dipLeft = bounds.Left * dpiScaleX;
            double dipTop = bounds.Top * dpiScaleY;
            double dipWidth = bounds.Width * dpiScaleX;

            double yOffset = _config.NotchMode ? 0 : _config.OffsetY;
            double x = dipLeft + (dipWidth - targetWidth) / 2.0 + _config.OffsetX;
            double y = dipTop + yOffset - 4;
            return (x, y);
        }

        var workArea = SystemParameters.WorkArea;
        return (
            workArea.Left + (workArea.Width - targetWidth) / 2.0 + _config.OffsetX,
            workArea.Top + (_config.NotchMode ? 0 : _config.OffsetY) - 4
        );
    }

    private void RepositionImmediate()
    {
        var (left, top) = GetTargetOrigin(Width);
        Left = left;
        Top = top;
    }

    // --- Temporary HUD Banners (Volume / Brightness / Battery / Bluetooth / Notifications) ---

    private void ShowProgressHud(string icon, string title, int percent, string hexColor, TimeSpan duration)
    {
        if (IsGameModeEffective || _isExpanded) return;

        HudAppIcon.Visibility = Visibility.Collapsed;
        HudIconText.Visibility = Visibility.Visible;
        HudIconText.Text = icon;

        HudTitleText.Text = title;
        HudSubtitleText.Visibility = Visibility.Collapsed;
        HudProgressBar.Visibility = Visibility.Visible;
        HudProgressBar.Value = Math.Clamp(percent, 0, 100);
        HudProgressBar.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));

        HudValueText.Text = $"{percent}%";
        HudValueText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));

        ShowBannerInternal(duration);
    }

    private void ShowNotificationHud(string fallbackIcon, string title, string subtitle, BitmapImage? appIcon, string rightBadge, string hexColor, TimeSpan duration)
    {
        if (IsGameModeEffective || _isExpanded) return;

        if (appIcon is not null)
        {
            HudAppIcon.Source = appIcon;
            HudAppIcon.Visibility = Visibility.Visible;
            HudIconText.Visibility = Visibility.Collapsed;
        }
        else
        {
            HudAppIcon.Visibility = Visibility.Collapsed;
            HudIconText.Visibility = Visibility.Visible;
            HudIconText.Text = fallbackIcon;
        }

        HudTitleText.Text = title;
        HudSubtitleText.Text = string.IsNullOrWhiteSpace(subtitle) ? Services.LocalizationService.Get(_config.Language, "HudNewNotif") : subtitle;
        HudSubtitleText.Visibility = Visibility.Visible;
        HudProgressBar.Visibility = Visibility.Collapsed;

        HudValueText.Text = rightBadge;
        HudValueText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));

        ShowBannerInternal(duration);
    }

    private void ShowBannerInternal(TimeSpan duration)
    {
        _hudHideTimer.Stop();
        _hudHideTimer.Interval = duration;
        _hudHideTimer.Start();

        if (_isBannerVisible) return;
        _isBannerVisible = true;

        CompactView.Visibility = Visibility.Collapsed;
        HudBannerView.Visibility = Visibility.Visible;
        HudBannerView.Opacity = 0;

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4.5 };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(fade, 240);
        HudBannerView.BeginAnimation(OpacityProperty, fade);

        var tt = HudBannerView.RenderTransform as TranslateTransform ?? new TranslateTransform();
        HudBannerView.RenderTransform = tt;
        var slide = new DoubleAnimation(-5, 0, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(slide, 240);
        tt.BeginAnimation(TranslateTransform.YProperty, slide);

        ApplyIslandCornerRadius(24);
        AnimatePillSize(BannerWidth, BannerHeight, isExpanding: true, cornerRadius: 24);
    }

    private void HideHudBanner()
    {
        if (!_isBannerVisible || _isExpanded) return;
        _isBannerVisible = false;

        HudBannerView.Visibility = Visibility.Collapsed;
        CompactView.Visibility = Visibility.Visible;
        CompactView.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4.5 }
        };
        Timeline.SetDesiredFrameRate(fade, 240);
        CompactView.BeginAnimation(OpacityProperty, fade);

        ApplyIslandCornerRadius(20);
        AnimatePillSize(CompactWidth, CompactHeight, isExpanding: false, cornerRadius: 20);
    }

    // --- Expand / Collapse ---

    public void Expand()
    {
        if (IsGameModeEffective) return;
        _leaveTimer.Stop();
        _hudHideTimer.Stop();
        _isBannerVisible = false;

        if (_isExpanded) return;
        _isExpanded = true;

        CompactView.BeginAnimation(OpacityProperty, null);
        CompactView.Visibility = Visibility.Collapsed;
        HudBannerView.BeginAnimation(OpacityProperty, null);
        HudBannerView.Visibility = Visibility.Collapsed;

        ExpandedView.BeginAnimation(OpacityProperty, null);
        ExpandedView.IsHitTestVisible = true;
        ExpandedView.Visibility = Visibility.Visible;
        ExpandedView.Opacity = 0;

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5.0 };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(35),
            EasingFunction = ease
        };
        Timeline.SetDesiredFrameRate(fadeIn, 240);
        ExpandedView.BeginAnimation(OpacityProperty, fadeIn);

        var tg = ExpandedView.RenderTransform as TransformGroup;
        TranslateTransform tt;
        ScaleTransform st;
        if (tg is null || tg.Children.Count < 2)
        {
            tt = new TranslateTransform();
            st = new ScaleTransform(0.96, 0.96);
            tg = new TransformGroup();
            tg.Children.Add(st);
            tg.Children.Add(tt);
            ExpandedView.RenderTransformOrigin = new System.Windows.Point(0.5, 0.0);
            ExpandedView.RenderTransform = tg;
        }
        else
        {
            st = (ScaleTransform)tg.Children[0];
            tt = (TranslateTransform)tg.Children[1];
        }

        var slideIn = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var scaleX = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        var scaleY = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(slideIn, 240);
        Timeline.SetDesiredFrameRate(scaleX, 240);
        Timeline.SetDesiredFrameRate(scaleY, 240);
        tt.BeginAnimation(TranslateTransform.YProperty, slideIn);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

        ApplyIslandCornerRadius(26);
        UpdateClock();
        AnimatePillSize(ExpandedWidth, ExpandedHeight, isExpanding: true, cornerRadius: 26);

        if (TabUtility.IsChecked == true)
        {
            GameModeService.SetAllowActivate(_hwnd, true);
            Dispatcher.BeginInvoke(() =>
            {
                Activate();
                CalcInputBox.Focus();
            });
        }
    }

    public void Collapse()
    {
        if (!_isExpanded) return;
        _isExpanded = false;
        GameModeService.SetNoActivate(_hwnd);

        ExpandedView.IsHitTestVisible = false;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3.5 }
        };
        Timeline.SetDesiredFrameRate(fadeOut, 240);
        fadeOut.Completed += (_, _) =>
        {
            if (!_isExpanded)
                ExpandedView.Visibility = Visibility.Collapsed;
        };
        ExpandedView.BeginAnimation(OpacityProperty, fadeOut);

        CompactView.Visibility = Visibility.Visible;
        CompactView.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(60),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4.5 }
        };
        Timeline.SetDesiredFrameRate(fadeIn, 240);
        CompactView.BeginAnimation(OpacityProperty, fadeIn);

        ApplyIslandCornerRadius(20);
        AnimatePillSize(CompactWidth, CompactHeight, isExpanding: false, cornerRadius: 20);
    }

    private void ApplyIslandCornerRadius(double radius)
    {
        double inner = Math.Max(0, radius - 1);
        if (_config.NotchMode)
        {
            IslandBorder.CornerRadius = new CornerRadius(0, 0, radius, radius);
            IslandBorder.BorderThickness = new Thickness(1.3, 0, 1.3, 1.3);
            CustomBannerLayer.CornerRadius = new CornerRadius(0, 0, inner, inner);
        }
        else
        {
            IslandBorder.CornerRadius = new CornerRadius(radius);
            IslandBorder.BorderThickness = new Thickness(1.3);
            CustomBannerLayer.CornerRadius = new CornerRadius(inner);
        }
    }

    private void AnimatePillSize(double targetWidth, double targetHeight, bool isExpanding, double cornerRadius)
    {
        var duration = TimeSpan.FromMilliseconds(isExpanding ? 320 : 240);
        IEasingFunction ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = isExpanding ? 5.2 : 4.5 };

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(targetHeight, duration) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(widthAnim, 240);
        Timeline.SetDesiredFrameRate(heightAnim, 240);

        IslandBorder.BeginAnimation(WidthProperty, widthAnim);
        IslandBorder.BeginAnimation(HeightProperty, heightAnim);

        double clipX = (ExpandedWidth - targetWidth) / 2.0;
        double clipY = _config.NotchMode ? -cornerRadius : 0;
        double clipH = _config.NotchMode ? targetHeight + cornerRadius : targetHeight;
        var clipRectAnim = new RectAnimation(new Rect(clipX, clipY, targetWidth, clipH), duration) { EasingFunction = ease };
        var radiusAnim = new DoubleAnimation(cornerRadius, duration) { EasingFunction = ease };
        Timeline.SetDesiredFrameRate(clipRectAnim, 240);
        Timeline.SetDesiredFrameRate(radiusAnim, 240);

        PillClipRect.BeginAnimation(RectangleGeometry.RectProperty, clipRectAnim);
        PillClipRect.BeginAnimation(RectangleGeometry.RadiusXProperty, radiusAnim);
        PillClipRect.BeginAnimation(RectangleGeometry.RadiusYProperty, radiusAnim);
    }

    private void Island_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _leaveTimer.Stop();
        Expand();
    }

    private void Island_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPinned)
        {
            _leaveTimer.Start();
        }
    }

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickInsideButton(e.OriginalSource as DependencyObject))
            return;

        if (!_isExpanded)
        {
            _isPinned = true;
            PinIndicator.Visibility = Visibility.Visible;
            Expand();
        }
        else
        {
            _isPinned = !_isPinned;
            PinIndicator.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;
            if (!_isPinned && !PillHost.IsMouseOver)
            {
                Collapse();
            }
        }
    }

    private static bool IsClickInsideButton(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.RangeBase or
                System.Windows.Controls.Primitives.Thumb)
                return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void Island_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsGameModeEffective) return;
        if (e.Delta > 0) NativeActions.VolumeUp();
        else if (e.Delta < 0) NativeActions.VolumeDown();
        e.Handled = true;
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (MediaPanel is null || SystemPanel is null || TimerPanel is null || ActionsPanel is null || UtilityPanel is null) return;

        MediaPanel.Visibility = TabMedia.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = TabSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = TabTimer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ActionsPanel.Visibility = TabActions.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UtilityPanel.Visibility = TabUtility.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        UIElement? activePanel = TabMedia.IsChecked == true ? MediaPanel
            : TabSystem.IsChecked == true ? SystemPanel
            : TabTimer.IsChecked == true ? TimerPanel
            : TabActions.IsChecked == true ? ActionsPanel
            : TabUtility.IsChecked == true ? UtilityPanel
            : null;
        if (activePanel is not null)
            AnimatePanelReveal(activePanel);

        if (TabUtility.IsChecked == true)
        {
            PollClipboard();
            GameModeService.SetAllowActivate(_hwnd, true);
            Dispatcher.BeginInvoke(() =>
            {
                Activate();
                CalcInputBox.Focus();
            });
        }
        else
        {
            GameModeService.SetNoActivate(_hwnd);
        }
    }

    private static void AnimatePanelReveal(UIElement panel)
    {
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4.5 };
        panel.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = ease
        };
        Timeline.SetDesiredFrameRate(fade, 240);
        panel.BeginAnimation(OpacityProperty, fade);

        var tt = panel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        panel.RenderTransform = tt;
        var slide = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        };
        Timeline.SetDesiredFrameRate(slide, 240);
        tt.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void UpdateClock()
    {
        string now = DateTime.Now.ToString("HH:mm");
        CompactClockText.Text = now;
        ExpandedClockText.Text = now;
    }

    private static readonly SolidColorBrush EqPlayingBrush = CreateFrozenBrush("#30D158");
    private static readonly SolidColorBrush EqIdleBrush = CreateFrozenBrush("#38BDF8");

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var b = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void AnimateEqualizer()
    {
        _eqPhase += _isPlaying ? 0.11 : 0.04;
        var accent = _isPlaying ? EqPlayingBrush : EqIdleBrush;

        Border[] bars = [EqBar1, EqBar2, EqBar3, EqBar4, EqBar5];
        for (int i = 0; i < bars.Length; i++)
        {
            if (!ReferenceEquals(bars[i].Background, accent))
                bars[i].Background = accent;

            double ratio = _isPlaying
                ? 0.24 + 0.76 * Math.Abs(Math.Sin(_eqPhase + i * 0.78))
                : 0.25 + 0.22 * (0.5 + 0.5 * Math.Sin(_eqPhase + i * 0.55));
            if (bars[i].RenderTransform is ScaleTransform st)
                st.ScaleY = ratio;
        }
    }

    private void UpdateMediaUI(MediaInfo info)
    {
        _hasMedia = info.HasMedia;
        _isPlaying = info.IsPlaying;

        MediaTitleText.Text = info.HasMedia ? info.Title : Services.LocalizationService.Get(_config.Language, "NoMediaTitle");
        MediaArtistText.Text = info.HasMedia ? info.Artist : Services.LocalizationService.Get(_config.Language, "NoMediaArtist");
        PlayIcon.Visibility = info.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = info.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(info.SourceApp))
        {
            SourceAppText.Text = info.SourceApp;
            SourceBadge.Visibility = Visibility.Visible;
        }
        else
        {
            SourceBadge.Visibility = Visibility.Collapsed;
        }

        if (info.Duration > TimeSpan.Zero)
        {
            MediaPosText.Text = FormatDuration(info.Position);
            MediaDurText.Text = FormatDuration(info.Duration);
            MediaProgressBar.Value = Math.Clamp(info.Position.TotalSeconds / info.Duration.TotalSeconds * 100.0, 0, 100);
        }
        else
        {
            MediaPosText.Text = "0:00";
            MediaDurText.Text = "0:00";
            MediaProgressBar.Value = 0;
        }

        if (info.HasMedia && info.Thumbnail is not null)
        {
            AlbumArtImage.Source = info.Thumbnail;
            AlbumArtImage.Visibility = Visibility.Visible;
            AlbumPlaceholder.Visibility = Visibility.Collapsed;

            MiniAlbumArt.Source = info.Thumbnail;
            MiniArtBorder.Visibility = Visibility.Visible;
        }
        else
        {
            AlbumArtImage.Source = null;
            AlbumArtImage.Visibility = Visibility.Collapsed;
            AlbumPlaceholder.Visibility = Visibility.Visible;

            MiniAlbumArt.Source = null;
            MiniArtBorder.Visibility = Visibility.Collapsed;
        }

        RefreshCompactStatus();
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _ramHistory = new();
    private readonly Queue<double> _gpuHistory = new();

    private void UpdateSystemUI(SystemStats stats)
    {
        _latestCpu = stats.CpuPercent;
        AnimateProgressBar(CpuProgressBar, stats.CpuPercent);
        CpuValueText.Text = $"{stats.CpuPercent:0}%";
        UpdateSparkline(CpuSparkline, _cpuHistory, stats.CpuPercent);

        AnimateProgressBar(RamProgressBar, stats.RamPercent);
        RamValueText.Text = $"{stats.RamPercent:0}%";
        RamSubText.Text = $"{stats.RamUsedGb:0.0}/{stats.RamTotalGb:0}G";
        UpdateSparkline(RamSparkline, _ramHistory, stats.RamPercent);

        AnimateProgressBar(GpuProgressBar, stats.GpuPercent);
        GpuValueText.Text = $"{stats.GpuPercent:0}%";
        UpdateSparkline(GpuSparkline, _gpuHistory, stats.GpuPercent);

        NetDownText.Text = $"{stats.DownloadMbps:0.00} MB/s";
        NetUpText.Text = $"{stats.UploadMbps:0.00} MB/s";

        if (stats.HasBattery)
        {
            BatteryText.Text = $"{stats.BatteryPercent}%";
            BatteryVectorIcon.Data = System.Windows.Media.Geometry.Parse(
                stats.IsCharging
                    ? "M13,2 L4,14 H12 L11,22 L20,10 H12 L13,2 Z"
                    : "M2,7 H19 V17 H2 Z M19,10 H21 V14 H19");
        }
        else
        {
            BatteryText.Text = "AC";
            BatteryVectorIcon.Data = System.Windows.Media.Geometry.Parse("M13,2 L4,14 H12 L11,22 L20,10 H12 L13,2 Z");
        }

        RefreshCompactStatus();
    }

    private static void AnimateProgressBar(System.Windows.Controls.ProgressBar bar, double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4.0 }
        };
        Timeline.SetDesiredFrameRate(anim, 240);
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
    }

    private static void UpdateSparkline(System.Windows.Shapes.Polyline line, Queue<double> history, double value)
    {
        const int maxSamples = 18;
        history.Enqueue(Math.Clamp(value, 0, 100));
        while (history.Count > maxSamples)
            history.Dequeue();

        var pts = new PointCollection(history.Count);
        double w = line.Width > 0 ? line.Width : 62;
        double h = line.Height > 0 ? line.Height : 22;
        int count = history.Count;
        int idx = 0;
        foreach (double v in history)
        {
            double x = count <= 1 ? w : (double)idx / (maxSamples - 1) * w + (maxSamples - count) * (w / (maxSamples - 1));
            double y = h - 2 - (v / 100.0) * (h - 4);
            pts.Add(new System.Windows.Point(x, y));
            idx++;
        }
        line.Points = pts;
    }

    private void RefreshCompactStatus()
    {
        if (_timerRunning && _timerRemainingSecs > 0)
        {
            int mins = _timerRemainingSecs / 60;
            int secs = _timerRemainingSecs % 60;
            CompactStatusText.Text = $"⏱ {mins:00}:{secs:00}";
            CompactStatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF9F0A"));
        }
        else if (_hasMedia && _isPlaying)
        {
            CompactStatusText.Text = Services.LocalizationService.Get(_config.Language, "CompactPlaying");
            CompactStatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#30D158"));
        }
        else
        {
            CompactStatusText.Text = $"CPU {_latestCpu:0}%";
            CompactStatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A1A1AA"));
        }
    }

    private async void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.TogglePlayPauseAsync();
    private async void NextBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.NextAsync();
    private async void PrevBtn_Click(object sender, RoutedEventArgs e) => await _mediaService.PreviousAsync();

    // --- Volume Slider Bar ---
    private void UpdateVolumeSliderUI(int vol, bool muted)
    {
        _updatingVolumeSlider = true;
        VolumeSlider.Value = muted ? 0 : vol;
        VolumePctText.Text = muted ? Services.LocalizationService.Get(_config.Language, "ActMute") : $"{vol}%";
        bool isMuted = muted || vol == 0;
        VolOnIcon.Visibility = isMuted ? Visibility.Collapsed : Visibility.Visible;
        VolMuteIcon.Visibility = isMuted ? Visibility.Visible : Visibility.Collapsed;
        _updatingVolumeSlider = false;
    }

    private void VolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isDraggingVolume = true;
    private void VolumeSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) => _isDraggingVolume = false;

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolumeSlider) return;
        int val = (int)Math.Round(VolumeSlider.Value);
        VolumePctText.Text = val == 0 ? Services.LocalizationService.Get(_config.Language, "ActMute") : $"{val}%";
        bool isMuted = val == 0;
        VolOnIcon.Visibility = isMuted ? Visibility.Collapsed : Visibility.Visible;
        VolMuteIcon.Visibility = isMuted ? Visibility.Visible : Visibility.Collapsed;
        _audioBrightness.SetVolume(val);
    }

    private void VolumeMuteBtn_Click(object sender, RoutedEventArgs e) => _audioBrightness.ToggleMute();

    private void GameModeBtn_Click(object sender, RoutedEventArgs e) => ToggleGameMode();

    public void ToggleGameMode()
    {
        _manualGameMode = !_manualGameMode;
        ApplyGameModeState();
    }

    // --- Timer ---
    private void PresetTimer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && int.TryParse(btn.Tag?.ToString(), out int mins))
        {
            _timerTotalSecs = mins * 60;
            _timerRemainingSecs = _timerTotalSecs;
            _timerRunning = true;
            _countdownTimer.Start();
            TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerPause");
            UpdateTimerUI();
            RefreshCompactStatus();
        }
    }

    private void TimerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_timerTotalSecs == 0)
        {
            _timerTotalSecs = 5 * 60;
            _timerRemainingSecs = _timerTotalSecs;
            _timerRunning = true;
            _countdownTimer.Start();
            TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerPause");
        }
        else if (_timerRunning)
        {
            _timerRunning = false;
            _countdownTimer.Stop();
            TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerResume");
        }
        else
        {
            _timerRunning = true;
            _countdownTimer.Start();
            TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerPause");
        }
        UpdateTimerUI();
        RefreshCompactStatus();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _timerRunning = false;
        _countdownTimer.Stop();
        _timerTotalSecs = 0;
        _timerRemainingSecs = 0;
        TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerStart");
        UpdateTimerUI();
        RefreshCompactStatus();
    }

    private void OnCountdownTick()
    {
        if (_timerRemainingSecs > 0)
        {
            _timerRemainingSecs--;
            UpdateTimerUI();
            RefreshCompactStatus();
        }
        if (_timerRemainingSecs <= 0)
        {
            _timerRunning = false;
            _countdownTimer.Stop();
            TimerToggleBtn.Content = Services.LocalizationService.Get(_config.Language, "TimerStart");
            RefreshCompactStatus();
            TimerFinished?.Invoke();
        }
    }

    private void UpdateTimerUI()
    {
        int mins = _timerRemainingSecs / 60;
        int secs = _timerRemainingSecs % 60;
        TimerDisplayText.Text = $"{mins:00}:{secs:00}";
        TimerProgressBar.Value = _timerTotalSecs > 0
            ? (double)(_timerTotalSecs - _timerRemainingSecs) / _timerTotalSecs * 100.0
            : 0;
    }

    // --- 3-Item Clipboard History & Quick Actions ---
    private void PollClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                string raw = System.Windows.Clipboard.GetText().Replace("\r", " ").Replace("\n", " ").Trim();
                if (!string.IsNullOrEmpty(raw) && (_clipboardHistory.Count == 0 || _clipboardHistory[0] != raw))
                {
                    _clipboardHistory.Remove(raw);
                    _clipboardHistory.Insert(0, raw);
                    if (_clipboardHistory.Count > 3)
                        _clipboardHistory.RemoveAt(3);
                    RenderClipboardHistory();
                }
            }
        }
        catch { }
    }

    private void RenderClipboardHistory()
    {
        System.Windows.Controls.Button[] btns = [ClipBtn0, ClipBtn1, ClipBtn2];
        TextBlock[] texts = [ClipText0, ClipText1, ClipText2];

        if (_clipboardHistory.Count == 0)
        {
            EmptyClipLabel.Text = Services.LocalizationService.Get(_config.Language, "EmptyClip");
            EmptyClipLabel.Visibility = Visibility.Visible;
            ClipBtn0.Visibility = Visibility.Collapsed;
            ClipBtn1.Visibility = Visibility.Collapsed;
            ClipBtn2.Visibility = Visibility.Collapsed;
            return;
        }
        EmptyClipLabel.Visibility = Visibility.Collapsed;

        for (int i = 0; i < 3; i++)
        {
            if (i < _clipboardHistory.Count)
            {
                btns[i].Visibility = Visibility.Visible;
                texts[i].Text = $"📋 {_clipboardHistory[i]}";
            }
            else
            {
                btns[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ClipHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && int.TryParse(btn.Tag?.ToString(), out int idx))
        {
            if (idx >= 0 && idx < _clipboardHistory.Count)
            {
                try
                {
                    System.Windows.Clipboard.SetText(_clipboardHistory[idx]);
                }
                catch { }
            }
        }
    }

    private void MuteBtn_Click(object sender, RoutedEventArgs e) => NativeActions.VolumeMute();
    private void SnipBtn_Click(object sender, RoutedEventArgs e) => NativeActions.OpenSnippingTool();
    private void LockBtn_Click(object sender, RoutedEventArgs e) => NativeActions.LockPc();
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void ClearClipsBtn_Click(object sender, RoutedEventArgs e)
    {
        _clipboardHistory.Clear();
        try { System.Windows.Clipboard.Clear(); } catch { }
        RenderClipboardHistory();
    }

    private void AudioOutBtn_Click(object sender, RoutedEventArgs e) => NativeActions.OpenAudioOutputSwitcher();

    private void ColorPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = System.Windows.Input.Cursors.Cross,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };

        overlay.MouseLeftButtonDown += (_, args) =>
        {
            var pt = overlay.PointToScreen(args.GetPosition(overlay));
            overlay.Close();
            string hex = NativeActions.SampleScreenHex((int)pt.X, (int)pt.Y);
            try { System.Windows.Clipboard.SetText(hex); } catch { }
            _clipboardHistory.RemoveAll(s => s == hex);
            _clipboardHistory.Insert(0, hex);
            if (_clipboardHistory.Count > 3) _clipboardHistory.RemoveAt(3);
            RenderClipboardHistory();
        };
        overlay.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape) overlay.Close();
        };
        overlay.MouseRightButtonDown += (_, _) => overlay.Close();
        overlay.Show();
        overlay.Activate();
    }

    private void CalcInputBox_GotFocus(object sender, RoutedEventArgs e)
    {
        GameModeService.SetAllowActivate(_hwnd, true);
    }

    private void CalcInputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            CalcCopy_Click(sender, e);
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            CalcClear_Click(sender, e);
        }
    }

    private void CalcInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string raw = CalcInputBox.Text ?? "";
        CalcPlaceholder.Visibility = string.IsNullOrEmpty(raw) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(raw))
        {
            CalcExprText.Text = "=";
            CalcDisplayText.Text = "0";
            CalcDisplayText.Foreground = System.Windows.Media.Brushes.White;
            return;
        }

        string normalized = raw.Trim()
            .Replace("×", "*")
            .Replace("x", "*")
            .Replace("X", "*")
            .Replace("÷", "/")
            .Replace(":", "/")
            .Replace(",", ".");

        // Support percentages like 50% -> (50/100)
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(\d+(?:\.\d+)?)\s*%",
            "($1/100.0)");

        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[0-9+\-*/().\s]+$"))
        {
            CalcExprText.Text = raw.Trim();
            CalcDisplayText.Text = "...";
            CalcDisplayText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            return;
        }

        try
        {
            using var dt = new System.Data.DataTable();
            object? res = dt.Compute(normalized, null);
            if (res is not null && res != DBNull.Value)
            {
                double num = Convert.ToDouble(res, System.Globalization.CultureInfo.InvariantCulture);
                if (double.IsInfinity(num) || double.IsNaN(num))
                {
                    CalcExprText.Text = raw.Trim() + " =";
                    CalcDisplayText.Text = "∞";
                    CalcDisplayText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165));
                    return;
                }
                CalcExprText.Text = raw.Trim() + " =";
                CalcDisplayText.Text = num.ToString("G12", System.Globalization.CultureInfo.InvariantCulture);
                CalcDisplayText.Foreground = System.Windows.Media.Brushes.White;
            }
        }
        catch
        {
            CalcExprText.Text = raw.Trim();
            CalcDisplayText.Text = "...";
            CalcDisplayText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        }
    }

    private void CalcClear_Click(object sender, RoutedEventArgs e)
    {
        CalcInputBox.Text = "";
        CalcExprText.Text = "=";
        CalcDisplayText.Text = "0";
        CalcDisplayText.Foreground = System.Windows.Media.Brushes.White;
        CalcInputBox.Focus();
    }

    private void CalcCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(CalcDisplayText.Text) && CalcDisplayText.Text is not "..." and not "∞")
            {
                System.Windows.Clipboard.SetText(CalcDisplayText.Text);
                CalcExprText.Text = "✓ " + Services.LocalizationService.Get(_config.Language, "CalcCopy");
            }
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer.Stop();
        _eqTimer.Stop();
        _leaveTimer.Stop();
        _countdownTimer.Stop();
        _hudHideTimer.Stop();
        _rgbStoryboard?.Stop();

        _mediaService.Dispose();
        _systemMonitor.Dispose();
        _audioBrightness.Dispose();
        _deviceMonitor.Dispose();
        _gameModeService.Dispose();
        _notificationService.Dispose();
        base.OnClosed(e);
    }
}
