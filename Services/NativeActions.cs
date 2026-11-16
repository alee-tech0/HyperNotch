using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HyperNotch.Services;

public static class NativeActions
{
    private const byte VK_LWIN = 0x5B;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    public static void VolumeUp() => SendKey(VK_VOLUME_UP);
    public static void VolumeDown() => SendKey(VK_VOLUME_DOWN);
    public static void VolumeMute() => SendKey(VK_VOLUME_MUTE);

    public static void LockPc()
    {
        try { LockWorkStation(); } catch { }
    }

    public static void SleepPc()
    {
        try { SetSuspendState(false, false, false); } catch { }
    }

    public static void EmptyRecycleBin()
    {
        try { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND); } catch { }
    }

    public static void SetAwake(bool enable)
    {
        try
        {
            SetThreadExecutionState(enable
                ? (ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)
                : ES_CONTINUOUS);
        }
        catch { }
    }

    public static void OpenAudioOutputSwitcher()
    {
        try
        {
            // Win + Ctrl + V opens Windows 11 Sound Output flyout
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event((byte)'V', 0, 0, UIntPtr.Zero);
            keybd_event((byte)'V', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); } catch { }
        }
    }

    public static void OpenQuickSettings()
    {
        try
        {
            // Win + A opens Windows Quick Settings
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event((byte)'A', 0, 0, UIntPtr.Zero);
            keybd_event((byte)'A', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch { }
    }

    public static void OpenSnippingTool()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch { }
    }

    public static string SampleScreenHex(int screenX, int screenY)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return "#FFFFFF";
        try
        {
            uint pixel = GetPixel(hdc, screenX, screenY);
            byte r = (byte)(pixel & 0x000000FF);
            byte g = (byte)((pixel & 0x0000FF00) >> 8);
            byte b = (byte)((pixel & 0x00FF0000) >> 16);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
