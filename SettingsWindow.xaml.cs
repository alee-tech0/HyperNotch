using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HyperNotch.Models;
using HyperNotch.Services;
using Forms = System.Windows.Forms;

namespace HyperNotch;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _originalConfig;
    private readonly AppConfig _config;
    private bool _initializing = true;
    private bool _saved;
    private string _newBgImagePath = "";
    private Storyboard? _rgbStoryboard;

    public event Action<AppConfig>? ConfigChanged;

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _originalConfig = config;
        _config = new AppConfig
        {
            MonitorIndex = config.MonitorIndex,
            OffsetX = config.OffsetX,
            OffsetY = config.OffsetY,
            NotchMode = config.NotchMode,
            AutoStart = config.AutoStart,
            Theme = config.Theme,
            AutoGameMode = config.AutoGameMode,
            Language = config.Language,
            CustomBackgroundHex = config.CustomBackgroundHex,
            CustomBorderHex = config.CustomBorderHex,
            CustomAccentHex = config.CustomAccentHex,
            CustomBannerImage = config.CustomBannerImage,
            CustomBannerOpacity = config.CustomBannerOpacity,
            QuickAppPath = config.QuickAppPath
        };

        PopulateMonitorCombo(_config.Language);
        MonitorCombo.SelectedIndex = Math.Clamp(_config.MonitorIndex, 0, Math.Max(0, Forms.Screen.AllScreens.Length - 1));

        ThemeOledBtn.IsChecked = _config.Theme == IslandTheme.OledBlack;
        ThemeAcrylicBtn.IsChecked = _config.Theme == IslandTheme.AcrylicGlass;
        ThemeAeroBtn.IsChecked = _config.Theme == IslandTheme.FrutigerAero;
        ThemeRgbBtn.IsChecked = _config.Theme == IslandTheme.RgbBorder;

        OffsetXSlider.Value = _config.OffsetX;
        OffsetYSlider.Value = _config.OffsetY;
        OffsetXLabel.Text = $"{_config.OffsetX} px";
        OffsetYLabel.Text = $"{_config.OffsetY} px";
        NotchModeCheck.IsChecked = _config.NotchMode;
        OffsetYSlider.IsEnabled = !_config.NotchMode;
        AutoGameModeCheck.IsChecked = _config.AutoGameMode;
        AutoStartCheck.IsChecked = _config.AutoStart;

        LanguageCombo.ItemsSource = LocalizationService.AvailableLanguages;
        LanguageCombo.SelectedValue = string.IsNullOrWhiteSpace(_config.Language) ? "it" : _config.Language;

        CustomBgBox.Text = _config.CustomBackgroundHex;
        CustomBorderBox.Text = _config.CustomBorderHex;
        CustomAccentBox.Text = _config.CustomAccentHex;

        RefreshCommunityList();
        ApplyTheme(_config.Theme);
        ApplyLanguage(_config.Language);
        _initializing = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageScreen is null || PageThemes is null || PageCustom is null || PageCommunity is null || PageSystem is null || PageInfo is null) return;

        PageScreen.Visibility = NavScreen.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageThemes.Visibility = NavThemes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageCustom.Visibility = NavCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageCommunity.Visibility = NavCommunity.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSystem.Visibility = NavSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageInfo.Visibility = NavInfo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        UIElement? activePage = NavScreen.IsChecked == true ? PageScreen
            : NavThemes.IsChecked == true ? PageThemes
            : NavCustom.IsChecked == true ? PageCustom
            : NavCommunity.IsChecked == true ? PageCommunity
            : NavSystem.IsChecked == true ? PageSystem
            : NavInfo.IsChecked == true ? PageInfo
            : null;
        if (activePage is not null)
        {
            var ease = new System.Windows.Media.Animation.ExponentialEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Exponent = 4.5 };
            activePage.Opacity = 0;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = ease
            };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(fade, 240);
            activePage.BeginAnimation(OpacityProperty, fade);

            var tt = activePage.RenderTransform as TranslateTransform ?? new TranslateTransform();
            activePage.RenderTransform = tt;
            var slide = new System.Windows.Media.Animation.DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease
            };
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(slide, 240);
            tt.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        PageTitleText.Text = sender == NavScreen ? LocalizationService.Get(_config.Language, "TitleScreen")
            : sender == NavThemes ? LocalizationService.Get(_config.Language, "TitleThemes")
            : sender == NavCustom ? LocalizationService.Get(_config.Language, "TitleCustom")
            : sender == NavCommunity ? LocalizationService.Get(_config.Language, "TitleCommunity")
            : sender == NavInfo ? LocalizationService.Get(_config.Language, "TitleInfo")
            : LocalizationService.Get(_config.Language, "TitleSystem");
    }

    private void ThemeCard_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _config.Theme = sender == ThemeAcrylicBtn ? IslandTheme.AcrylicGlass
            : sender == ThemeAeroBtn ? IslandTheme.FrutigerAero
            : sender == ThemeRgbBtn ? IslandTheme.RgbBorder
            : IslandTheme.OledBlack;
        ApplyTheme(_config.Theme);
        ConfigChanged?.Invoke(_config);
    }

    private void ApplyTheme(IslandTheme theme)
    {
        _rgbStoryboard?.Stop();
        _rgbStoryboard = null;
        SettingsGlossOverlay.Visibility = Visibility.Collapsed;
        SettingsBannerLayer.Visibility = Visibility.Collapsed;
        SettingsBannerLayer.Background = null;

        switch (theme)
        {
            case IslandTheme.OledBlack:
                SettingsRootBorder.Background = BrushFromHex("#0A0A0E");
                SettingsRootBorder.BorderBrush = BrushFromHex("#282834");
                ApplyPalette("#0F0F15", "#1E1E28", "#13131A", "#22222C", "#181822", "#1D283A", "#1D1D26", "#A1A1AA", "#38BDF8");
                break;

            case IslandTheme.AcrylicGlass:
                SettingsRootBorder.Background = BrushFromHex("#D815151C");
                SettingsRootBorder.BorderBrush = new LinearGradientBrush(
                    ColorFromHex("#60FFFFFF"), ColorFromHex("#18FFFFFF"),
                    new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
                SettingsGlossOverlay.Visibility = Visibility.Visible;
                SettingsGlossOverlay.Opacity = 0.35;
                ApplyPalette("#22FFFFFF", "#2EFFFFFF", "#26FFFFFF", "#36FFFFFF", "#2CFFFFFF", "#40FFFFFF", "#30FFFFFF", "#CBD5E1", "#7DD3FC");
                break;

            case IslandTheme.FrutigerAero:
                var aeroBg = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1)
                };
                aeroBg.GradientStops.Add(new GradientStop(ColorFromHex("#EC0284C7"), 0.0));
                aeroBg.GradientStops.Add(new GradientStop(ColorFromHex("#EC0D9488"), 0.55));
                aeroBg.GradientStops.Add(new GradientStop(ColorFromHex("#EC0369A1"), 1.0));
                SettingsRootBorder.Background = aeroBg;
                SettingsRootBorder.BorderBrush = BrushFromHex("#B0FFFFFF");
                SettingsGlossOverlay.Visibility = Visibility.Visible;
                SettingsGlossOverlay.Opacity = 0.9;
                ApplyPalette("#2AFFFFFF", "#58FFFFFF", "#34FFFFFF", "#70FFFFFF", "#40FFFFFF", "#5CFFFFFF", "#400C4A6E", "#E0F2FE", "#FFFFFF");
                break;

            case IslandTheme.RgbBorder:
                SettingsRootBorder.Background = BrushFromHex("#09090C");
                var rgbBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1)
                };
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Cyan, 0.0));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Magenta, 0.33));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.OrangeRed, 0.66));
                rgbBrush.GradientStops.Add(new GradientStop(Colors.Lime, 1.0));
                rgbBrush.RelativeTransform = new RotateTransform(0, 0.5, 0.5);
                SettingsRootBorder.BorderBrush = rgbBrush;

                var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3.5))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                Timeline.SetDesiredFrameRate(anim, 240);
                _rgbStoryboard = new Storyboard();
                _rgbStoryboard.Children.Add(anim);
                Storyboard.SetTarget(anim, SettingsRootBorder);
                Storyboard.SetTargetProperty(anim, new PropertyPath("BorderBrush.RelativeTransform.Angle"));
                _rgbStoryboard.Begin();

                ApplyPalette("#11111A", "#262238", "#151520", "#2C2640", "#1E1B2E", "#2F264A", "#222030", "#B8B5C8", "#C084FC");
                break;

            case IslandTheme.Custom:
                SettingsRootBorder.Background = SafeBrushFromHex(_config.CustomBackgroundHex, "#B91C1C");
                SettingsRootBorder.BorderBrush = SafeBrushFromHex(_config.CustomBorderHex, "#FCA5A5");
                ApplySettingsBanner(_config.CustomBannerImage, _config.CustomBannerOpacity);
                string accent = TryNormalizeHex(_config.CustomAccentHex, "#FDE047");
                ApplyPalette("#66000000", "#45FFFFFF", "#5C000000", "#48FFFFFF", "#70000000", "#68FFFFFF", "#50000000", "#F3F4F6", accent);
                break;
        }
    }

    private void ApplySettingsBanner(string bannerSpec, double opacity)
    {
        if (string.IsNullOrWhiteSpace(bannerSpec) || bannerSpec == "none")
        {
            SettingsBannerLayer.Visibility = Visibility.Collapsed;
            SettingsBannerLayer.Background = null;
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
                SettingsBannerLayer.Visibility = Visibility.Collapsed;
                return;
            }

            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();

            SettingsBannerLayer.Background = new ImageBrush(bmp)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Opacity = Math.Clamp(opacity, 0.15, 1.0)
            };
            SettingsBannerLayer.Visibility = Visibility.Visible;
        }
        catch
        {
            SettingsBannerLayer.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyPalette(
        string sidebarBg, string sidebarBorder,
        string cardBg, string cardBorder,
        string navHover, string navActive,
        string trackBg, string secondaryText, string accent)
    {
        Resources["SettingsSidebarBgBrush"] = BrushFromHex(sidebarBg);
        Resources["SettingsSidebarBorderBrush"] = BrushFromHex(sidebarBorder);
        Resources["SettingsCardBgBrush"] = BrushFromHex(cardBg);
        Resources["SettingsCardBorderBrush"] = BrushFromHex(cardBorder);
        Resources["SettingsNavHoverBrush"] = BrushFromHex(navHover);
        Resources["SettingsNavActiveBrush"] = BrushFromHex(navActive);
        Resources["SettingsTrackBgBrush"] = BrushFromHex(trackBg);
        Resources["SettingsSecondaryTextBrush"] = BrushFromHex(secondaryText);
        Resources["SettingsAccentBrush"] = BrushFromHex(accent);
    }

    private static System.Windows.Media.Color ColorFromHex(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static SolidColorBrush BrushFromHex(string hex) => new(ColorFromHex(hex));

    private static SolidColorBrush SafeBrushFromHex(string hex, string fallback)
    {
        try { return BrushFromHex(hex); }
        catch { return BrushFromHex(fallback); }
    }

    private static string TryNormalizeHex(string hex, string fallback)
    {
        try
        {
            _ = ColorFromHex(hex);
            return hex;
        }
        catch
        {
            return fallback;
        }
    }

    private void ActivateCustomThemeLive()
    {
        _initializing = true;
        ThemeOledBtn.IsChecked = false;
        ThemeAcrylicBtn.IsChecked = false;
        ThemeAeroBtn.IsChecked = false;
        ThemeRgbBtn.IsChecked = false;
        _initializing = false;
        _config.Theme = IslandTheme.Custom;
        ApplyTheme(IslandTheme.Custom);
        ConfigChanged?.Invoke(_config);
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        string[] parts = tag.Split('|');
        if (parts.Length != 3) return;

        _initializing = true;
        CustomBgBox.Text = parts[0];
        CustomBorderBox.Text = parts[1];
        CustomAccentBox.Text = parts[2];
        _initializing = false;

        _config.CustomBackgroundHex = parts[0];
        _config.CustomBorderHex = parts[1];
        _config.CustomAccentHex = parts[2];
        _config.CustomBannerImage = "none";
        ActivateCustomThemeLive();
    }

    private void CustomColorBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_initializing) return;
        _config.CustomBackgroundHex = TryNormalizeHex(CustomBgBox.Text.Trim(), _config.CustomBackgroundHex);
        _config.CustomBorderHex = TryNormalizeHex(CustomBorderBox.Text.Trim(), _config.CustomBorderHex);
        _config.CustomAccentHex = TryNormalizeHex(CustomAccentBox.Text.Trim(), _config.CustomAccentHex);
        _config.CustomBannerImage = "none";
        ActivateCustomThemeLive();
    }

    private void ToggleCreateBg_Click(object sender, RoutedEventArgs e)
    {
        bool open = CreateBgPanel.Visibility != Visibility.Visible;
        CreateBgPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ToggleCreateBgBtn.Content = LocalizationService.Get(_config.Language, open ? "CloseCreateBg" : "CreateBgBtn");
    }

    private void BrowseNewBgImage_Click(object sender, RoutedEventArgs e)
    {
        string imgFilterLabel = LocalizationService.Get(_config.Language, "ImageFilter");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Get(_config.Language, "ChooseImgTitle"),
            Filter = $"{imgFilterLabel} (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() == true)
        {
            _newBgImagePath = dlg.FileName;
            NewBgImageStatus.Text = System.IO.Path.GetFileName(dlg.FileName);
        }
    }

    private void SaveNewBg_Click(object sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(NewBgNameBox.Text) ? LocalizationService.Get(_config.Language, "DefaultBgName") : NewBgNameBox.Text.Trim();
        var item = new CommunityBackground
        {
            Name = name,
            Author = "User",
            BackgroundHex = TryNormalizeHex(NewBgColorBox.Text.Trim(), "#1E1B4B"),
            BorderHex = TryNormalizeHex(NewBgBorderBox.Text.Trim(), "#818CF8"),
            AccentHex = TryNormalizeHex(NewBgAccentBox.Text.Trim(), "#38BDF8"),
            ImagePath = _newBgImagePath,
            IsUserCreated = true
        };

        CommunityBackgroundService.SaveUserBackground(item);
        _newBgImagePath = "";
        NewBgImageStatus.Text = LocalizationService.Get(_config.Language, "NoImg");
        CreateBgPanel.Visibility = Visibility.Collapsed;
        ToggleCreateBgBtn.Content = LocalizationService.Get(_config.Language, "CreateBgBtn");

        RefreshCommunityList();
        ApplyCommunityBackground(item);
    }

    private void ApplyCommunityBackground(CommunityBackground bg)
    {
        _config.CustomBackgroundHex = bg.BackgroundHex;
        _config.CustomBorderHex = bg.BorderHex;
        _config.CustomAccentHex = bg.AccentHex;
        _config.CustomBannerImage = string.IsNullOrWhiteSpace(bg.ImagePath) ? "none" : bg.ImagePath;

        _initializing = true;
        CustomBgBox.Text = bg.BackgroundHex;
        CustomBorderBox.Text = bg.BorderHex;
        CustomAccentBox.Text = bg.AccentHex;
        _initializing = false;

        ActivateCustomThemeLive();
    }

    private void RefreshCommunityList()
    {
        CommunityListPanel.Children.Clear();
        var items = CommunityBackgroundService.LoadAll();
        if (items.Count == 0)
        {
            var empty = new System.Windows.Controls.TextBlock
            {
                Text = LocalizationService.Get(_config.Language, "CommunityEmpty"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 18, 4, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            empty.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SettingsSecondaryTextBrush");
            CommunityListPanel.Children.Add(empty);
            return;
        }

        foreach (var bg in items)
        {
            var card = new System.Windows.Controls.Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 7)
            };
            card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SettingsCardBgBrush");
            card.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "SettingsCardBorderBrush");

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var swatch = new System.Windows.Controls.Border
            {
                Width = 36,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = SafeBrushFromHex(bg.BackgroundHex, "#1E1B4B"),
                BorderBrush = SafeBrushFromHex(bg.BorderHex, "#818CF8"),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(swatch, 0);
            grid.Children.Add(swatch);

            var info = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = bg.Name,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            });
            string subText = bg.IsUserCreated
                ? (!string.IsNullOrWhiteSpace(bg.ImagePath)
                    ? LocalizationService.Get(_config.Language, "CreatedByYouImg")
                    : LocalizationService.Get(_config.Language, "CreatedByYou"))
                : string.Format(LocalizationService.Get(_config.Language, "ByAuthor"), bg.Author);
            var sub = new System.Windows.Controls.TextBlock { Text = subText, FontSize = 10 };
            sub.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SettingsSecondaryTextBrush");
            info.Children.Add(sub);
            System.Windows.Controls.Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var actions = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var applyBtn = new System.Windows.Controls.Button
            {
                Content = LocalizationService.Get(_config.Language, "Apply"),
                Style = (Style)FindResource("ActionBtn"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11
            };
            applyBtn.Click += (_, _) => ApplyCommunityBackground(bg);
            actions.Children.Add(applyBtn);

            if (bg.IsUserCreated)
            {
                var delBtn = new System.Windows.Controls.Button
                {
                    Content = "🗑",
                    Style = (Style)FindResource("ActionBtn"),
                    Padding = new Thickness(7, 5, 7, 5),
                    FontSize = 11,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                delBtn.Click += (_, _) =>
                {
                    CommunityBackgroundService.DeleteUserBackground(bg.Id);
                    RefreshCommunityList();
                };
                actions.Children.Add(delBtn);
            }

            System.Windows.Controls.Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            CommunityListPanel.Children.Add(card);
        }
    }

    private void OffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        _config.OffsetX = (int)Math.Round(OffsetXSlider.Value);
        _config.OffsetY = (int)Math.Round(OffsetYSlider.Value);
        OffsetXLabel.Text = $"{_config.OffsetX} px";
        OffsetYLabel.Text = $"{_config.OffsetY} px";
        ConfigChanged?.Invoke(_config);
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _config.MonitorIndex = Math.Max(0, MonitorCombo.SelectedIndex);
        _config.NotchMode = NotchModeCheck.IsChecked == true;
        OffsetYSlider.IsEnabled = !_config.NotchMode;
        _config.AutoGameMode = AutoGameModeCheck.IsChecked == true;
        _config.AutoStart = AutoStartCheck.IsChecked == true;
        if (LanguageCombo.SelectedValue is string lang && !string.IsNullOrWhiteSpace(lang))
        {
            _config.Language = lang;
            ApplyLanguage(lang);
        }
        ConfigChanged?.Invoke(_config);
    }

    private void ApplyLanguage(string lang)
    {
        NavScreen.Content = LocalizationService.Get(lang, "NavScreen");
        NavThemes.Content = LocalizationService.Get(lang, "NavThemes");
        NavCustom.Content = LocalizationService.Get(lang, "NavCustom");
        NavCommunity.Content = LocalizationService.Get(lang, "NavCommunity");
        NavSystem.Content = LocalizationService.Get(lang, "NavSystem");
        NavInfo.Content = LocalizationService.Get(lang, "NavInfo");

        MonitorLabel.Text = LocalizationService.Get(lang, "MonitorLabel");
        OffsetXTitleLabel.Text = LocalizationService.Get(lang, "OffsetXLabel");
        OffsetYTitleLabel.Text = LocalizationService.Get(lang, "OffsetYLabel");

        ThemeOledTitle.Text = LocalizationService.Get(lang, "ThemeOled");
        ThemeOledSub.Text = LocalizationService.Get(lang, "ThemeOledSub");
        ThemeAcrylicTitle.Text = LocalizationService.Get(lang, "ThemeAcrylic");
        ThemeAcrylicSub.Text = LocalizationService.Get(lang, "ThemeAcrylicSub");
        ThemeAeroTitle.Text = LocalizationService.Get(lang, "ThemeAero");
        ThemeAeroSub.Text = LocalizationService.Get(lang, "ThemeAeroSub");
        ThemeRgbTitle.Text = LocalizationService.Get(lang, "ThemeRgb");
        ThemeRgbSub.Text = LocalizationService.Get(lang, "ThemeRgbSub");

        PickBarColorLabel.Text = LocalizationService.Get(lang, "PickBarColor");
        ColorRedBtn.Content = LocalizationService.Get(lang, "ColorRed");
        ColorOrangeBtn.Content = LocalizationService.Get(lang, "ColorOrange");
        ColorYellowBtn.Content = LocalizationService.Get(lang, "ColorYellow");
        ColorGreenBtn.Content = LocalizationService.Get(lang, "ColorGreen");
        ColorEmeraldBtn.Content = LocalizationService.Get(lang, "ColorEmerald");
        ColorCyanBtn.Content = LocalizationService.Get(lang, "ColorCyan");
        ColorBlueBtn.Content = LocalizationService.Get(lang, "ColorBlue");
        ColorIndigoBtn.Content = LocalizationService.Get(lang, "ColorIndigo");
        ColorPurpleBtn.Content = LocalizationService.Get(lang, "ColorPurple");
        ColorPinkBtn.Content = LocalizationService.Get(lang, "ColorPink");
        ColorSilverBtn.Content = LocalizationService.Get(lang, "ColorSilver");
        ColorBlackBtn.Content = LocalizationService.Get(lang, "ColorBlack");
        PresetBgLabel.Text = LocalizationService.Get(lang, "PresetBg");
        PreCyberBtn.Content = LocalizationService.Get(lang, "PreCyber");
        PreSynthBtn.Content = LocalizationService.Get(lang, "PreSynth");
        PreNordicBtn.Content = LocalizationService.Get(lang, "PreNordic");
        PreOceanBtn.Content = LocalizationService.Get(lang, "PreOcean");
        PreLavaBtn.Content = LocalizationService.Get(lang, "PreLava");
        PreIceBtn.Content = LocalizationService.Get(lang, "PreIce");
        CustomHexLabel.Text = LocalizationService.Get(lang, "CustomHex");
        HexBgLabel.Text = LocalizationService.Get(lang, "HexBg");
        HexBorderLabel.Text = LocalizationService.Get(lang, "HexBorder");
        HexAccentLabel.Text = LocalizationService.Get(lang, "HexAccent");

        CommunityDescLabel.Text = LocalizationService.Get(lang, "CommunityDesc");
        ToggleCreateBgBtn.Content = CreateBgPanel.Visibility == Visibility.Visible
            ? LocalizationService.Get(lang, "CloseCreateBg")
            : LocalizationService.Get(lang, "CreateBgBtn");
        NewBgNameLabel.Text = LocalizationService.Get(lang, "BgName");
        NewBgColorLabel.Text = LocalizationService.Get(lang, "HexBg");
        NewBgBorderLabel.Text = LocalizationService.Get(lang, "HexBorder");
        NewBgAccentLabel.Text = LocalizationService.Get(lang, "HexAccent");
        BrowseNewBgImgBtn.Content = LocalizationService.Get(lang, "BrowseImgOpt");
        if (string.IsNullOrEmpty(_newBgImagePath))
            NewBgImageStatus.Text = LocalizationService.Get(lang, "NoImg");
        SaveNewBgBtn.Content = LocalizationService.Get(lang, "SaveToPc");

        NotchModeCheck.Content = LocalizationService.Get(lang, "NotchMode");
        AutoGameModeCheck.Content = LocalizationService.Get(lang, "AutoGameMode");
        AutoStartCheck.Content = LocalizationService.Get(lang, "AutoStart");
        LanguageLabel.Text = LocalizationService.Get(lang, "SelectLanguage");

        HyperNotchDescText.Text = LocalizationService.Get(lang, "HyperNotchDesc");
        CreatorBadgeText.Text = LocalizationService.Get(lang, "CreatorBadge");
        CreatorBioText.Text = LocalizationService.Get(lang, "CreatorBio");

        if (string.IsNullOrWhiteSpace(NewBgNameBox.Text) || NewBgNameBox.Text == "Mio Sfondo")
            NewBgNameBox.Text = LocalizationService.Get(lang, "DefaultBgName");

        int prevMon = MonitorCombo.SelectedIndex;
        bool prevInit = _initializing;
        _initializing = true;
        PopulateMonitorCombo(lang);
        if (prevMon >= 0 && prevMon < MonitorCombo.Items.Count)
            MonitorCombo.SelectedIndex = prevMon;
        _initializing = prevInit;

        ResetBtn.Content = LocalizationService.Get(lang, "Reset");
        SaveBtn.Content = LocalizationService.Get(lang, "SaveClose");

        PageTitleText.Text = NavScreen.IsChecked == true ? LocalizationService.Get(lang, "TitleScreen")
            : NavThemes.IsChecked == true ? LocalizationService.Get(lang, "TitleThemes")
            : NavCustom.IsChecked == true ? LocalizationService.Get(lang, "TitleCustom")
            : NavCommunity.IsChecked == true ? LocalizationService.Get(lang, "TitleCommunity")
            : NavInfo.IsChecked == true ? LocalizationService.Get(lang, "TitleInfo")
            : LocalizationService.Get(lang, "TitleSystem");

        RefreshCommunityList();
    }

    private void PopulateMonitorCombo(string lang)
    {
        MonitorCombo.Items.Clear();
        var screens = Forms.Screen.AllScreens;
        string primarySuffix = LocalizationService.Get(lang, "MonitorPrimaryTag");
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            string primaryTag = s.Primary ? primarySuffix : "";
            MonitorCombo.Items.Add($"Monitor {i + 1}: {s.Bounds.Width}x{s.Bounds.Height}{primaryTag}");
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        MonitorCombo.SelectedIndex = 0;
        ThemeOledBtn.IsChecked = true;
        OffsetXSlider.Value = 0;
        OffsetYSlider.Value = 10;
        OffsetXLabel.Text = "0 px";
        OffsetYLabel.Text = "10 px";
        NotchModeCheck.IsChecked = false;
        OffsetYSlider.IsEnabled = true;
        AutoGameModeCheck.IsChecked = true;
        LanguageCombo.SelectedValue = "it";
        CustomBgBox.Text = "#B91C1C";
        CustomBorderBox.Text = "#FCA5A5";
        CustomAccentBox.Text = "#FDE047";
        _initializing = false;

        _config.MonitorIndex = 0;
        _config.Theme = IslandTheme.OledBlack;
        _config.OffsetX = 0;
        _config.OffsetY = 10;
        _config.AutoGameMode = true;
        _config.Language = "it";
        _config.CustomBackgroundHex = "#B91C1C";
        _config.CustomBorderHex = "#FCA5A5";
        _config.CustomAccentHex = "#FDE047";
        _config.CustomBannerImage = "none";
        _config.CustomBannerOpacity = 0.85;
        ApplyTheme(_config.Theme);
        ApplyLanguage(_config.Language);
        ConfigChanged?.Invoke(_config);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ConfigService.Save(_config);
        _saved = true;
        ConfigChanged?.Invoke(_config);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_saved)
            ConfigChanged?.Invoke(_originalConfig);
        base.OnClosed(e);
    }
}
