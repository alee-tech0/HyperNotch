using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using HyperNotch.Models;
using HyperNotch.Services;
using Forms = System.Windows.Forms;

namespace HyperNotch;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private AppConfig _config = new();
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        _singleInstanceMutex = new System.Threading.Mutex(true, "HyperNotch_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        try
        {
            System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(System.Windows.Media.Animation.Timeline),
                new PropertyMetadata(240));
        }
        catch { }
        base.OnStartup(e);

        _config = ConfigService.Load();
        _mainWindow = new MainWindow(_config);
        _mainWindow.TimerFinished += OnTimerFinished;
        _mainWindow.SettingsRequested += OpenSettings;
        _mainWindow.Show();

        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "HyperNotch",
            Visible = true
        };
        _trayIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                Dispatcher.Invoke(() =>
                {
                    var menu = new TrayMenuWindow(
                        _config.Language,
                        ToggleVisibility,
                        () => _mainWindow?.ToggleGameMode(),
                        OpenSettings,
                        ExitApp);
                    menu.ShowAtCursor();
                });
            }
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var streamInfo = GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (streamInfo is not null)
            {
                return new Icon(streamInfo.Stream);
            }
        }
        catch { }
        return CreatePillIcon();
    }

    private void ToggleVisibility()
    {
        if (_mainWindow is null) return;
        if (_mainWindow.IsVisible)
            _mainWindow.Hide();
        else
            _mainWindow.Show();
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_mainWindow is null) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_config);
        _settingsWindow.ConfigChanged += newCfg =>
        {
            _config = newCfg;
            _mainWindow.ApplyConfig(_config);
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnTimerFinished()
    {
        _trayIcon?.ShowBalloonTip(4000, "HyperNotch", LocalizationService.Get(_config.Language, "TimerFinishedMsg"), Forms.ToolTipIcon.Info);
        _mainWindow?.Show();
        _mainWindow?.Expand();
    }

    private void ExitApp()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mainWindow?.Close();
        try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
        Shutdown();
    }

    private static Icon CreatePillIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bgBrush = new SolidBrush(Color.FromArgb(11, 11, 13));
        using var borderPen = new Pen(Color.FromArgb(58, 58, 64), 2f);
        using var path = new GraphicsPath();
        path.AddArc(2, 9, 14, 14, 90, 180);
        path.AddArc(16, 9, 14, 14, 270, 180);
        path.CloseFigure();

        g.FillPath(bgBrush, path);
        g.DrawPath(borderPen, path);

        using var accentBrush = new SolidBrush(Color.FromArgb(10, 132, 255));
        g.FillEllipse(accentBrush, 20, 13, 6, 6);

        return Icon.FromHandle(bmp.GetHicon());
    }
}
