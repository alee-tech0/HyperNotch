using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace HyperNotch.Services;

public sealed class GameModeService : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly CancellationTokenSource _cts = new();
    private IntPtr _ownHwnd;
    private bool _lastFullscreen;

    public event Action<bool>? FullscreenChanged;

    public void Start(IntPtr ownHwnd)
    {
        _ownHwnd = ownHwnd;
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1200, token);
                bool fs = IsForegroundFullscreen();
                if (fs != _lastFullscreen)
                {
                    _lastFullscreen = fs;
                    FullscreenChanged?.Invoke(fs);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private bool IsForegroundFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _ownHwnd) return false;

        var cls = new StringBuilder(64);
        _ = GetClassName(fg, cls, cls.Capacity);
        string className = cls.ToString();
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!GetWindowRect(fg, out RECT rect))
            return false;

        var screen = Forms.Screen.FromHandle(fg);
        var bounds = screen.Bounds;

        return rect.Left <= bounds.Left &&
               rect.Top <= bounds.Top &&
               rect.Right >= bounds.Right &&
               rect.Bottom >= bounds.Bottom;
    }

    public static void SetNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
    }

    public static void SetAllowActivate(IntPtr hwnd, bool allow)
    {
        if (hwnd == IntPtr.Zero) return;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (allow)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_NOACTIVATE);
            SetForegroundWindow(hwnd);
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero) return;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (enable)
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
        else
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
