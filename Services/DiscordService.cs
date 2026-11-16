using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace HyperNotch.Services;

public sealed class DiscordService : IDisposable
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    private readonly CancellationTokenSource _cts = new();
    private bool _lastRunning;
    private bool _lastInVoice;

    public event Action<bool, bool>? DiscordStateChanged;

    public bool IsRunning { get; private set; }
    public bool IsInVoice { get; private set; }

    public void Start()
    {
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                bool running = IsDiscordProcessRunning();
                bool inVoice = running && HasActiveDiscordAudioSession();

                if (running != _lastRunning || inVoice != _lastInVoice)
                {
                    _lastRunning = running;
                    _lastInVoice = inVoice;
                    IsRunning = running;
                    IsInVoice = inVoice;
                    DiscordStateChanged?.Invoke(running, inVoice);
                }

                await Task.Delay(2000, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private static bool IsDiscordProcessRunning() =>
        Process.GetProcessesByName("Discord").Length > 0 ||
        Process.GetProcessesByName("DiscordPTB").Length > 0 ||
        Process.GetProcessesByName("DiscordCanary").Length > 0;

    private static bool HasActiveDiscordAudioSession()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.State == AudioSessionState.AudioSessionStateActive && IsDiscordSession(session))
                            return true;
                    }
                }
                catch { }
                finally { device.Dispose(); }
            }
        }
        catch { }
        return false;
    }

    private static bool IsDiscordSession(AudioSessionControl session)
    {
        uint pid = session.GetProcessID;
        if (pid == 0) return false;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName.Contains("Discord", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Mutes/unmutes ALL active Windows microphone (Capture) endpoints.
    /// Guarantees microphone mute in Discord regardless of selected input device.
    /// </summary>
    public static bool SetMicrophoneMute(bool mute)
    {
        bool updated = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var mic in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try
                {
                    mic.AudioEndpointVolume.Mute = mute;
                    updated = true;
                }
                catch { }
                finally { mic.Dispose(); }
            }

            foreach (var role in new[] { Role.Console, Role.Communications })
            {
                try
                {
                    using var defMic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                    defMic.AudioEndpointVolume.Mute = mute;
                    updated = true;
                }
                catch { }
            }
        }
        catch { }
        return updated;
    }

    /// <summary>
    /// Mutes/unmutes Discord's audio session across ALL active render devices.
    /// </summary>
    public static bool SetDiscordOutputMute(bool mute)
    {
        bool updated = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (IsDiscordSession(session))
                        {
                            session.SimpleAudioVolume.Mute = mute;
                            updated = true;
                        }
                    }
                }
                catch { }
                finally { device.Dispose(); }
            }
        }
        catch { }
        return updated;
    }

    public static int GetDiscordOutputVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (IsDiscordSession(session))
                            return (int)Math.Round(session.SimpleAudioVolume.Volume * 100);
                    }
                }
                catch { }
                finally { device.Dispose(); }
            }
        }
        catch { }
        return 100;
    }

    /// <summary>
    /// Sets Discord's independent app volume (0-100%) across all active render devices.
    /// </summary>
    public static void SetDiscordOutputVolume(int percent)
    {
        float scalar = Math.Clamp(percent, 0, 100) / 100f;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (IsDiscordSession(session))
                            session.SimpleAudioVolume.Volume = scalar;
                    }
                }
                catch { }
                finally { device.Dispose(); }
            }
        }
        catch { }
    }

    /// <summary>
    /// Sends a global keybind while keeping <paramref name="selfHwnd"/> in the foreground
    /// so other apps (e.g. Antigravity) never receive the shortcut and Discord never pops up.
    /// </summary>
    public static bool SendKeybindToDiscord(string keybind, IntPtr selfHwnd = default)
    {
        if (string.IsNullOrWhiteSpace(keybind) || !IsDiscordProcessRunning()) return false;

        if (!TryParseKeybind(keybind, out var modifiers, out ushort mainKey))
            return false;

        _ = Task.Run(() =>
        {
            if (selfHwnd != IntPtr.Zero && GetForegroundWindow() != selfHwnd)
            {
                AllowSetForegroundWindow(-1);
                SetForegroundWindow(selfHwnd);
                Thread.Sleep(25);
            }

            // Only inject if HyperNotch itself (or Discord) is foreground — never inject into Antigravity/other apps
            IntPtr fg = GetForegroundWindow();
            if (selfHwnd != IntPtr.Zero && fg != selfHwnd)
                return;

            foreach (ushort mod in modifiers)
            {
                var modDown = new[] { CreateKeyInput(mod, keyUp: false) };
                _ = SendInput(1, modDown, Marshal.SizeOf<INPUT>());
            }
            if (modifiers.Count > 0) Thread.Sleep(40);

            var keyDown = new[] { CreateKeyInput(mainKey, keyUp: false) };
            _ = SendInput(1, keyDown, Marshal.SizeOf<INPUT>());
            Thread.Sleep(55);

            var keyUp = new[] { CreateKeyInput(mainKey, keyUp: true) };
            _ = SendInput(1, keyUp, Marshal.SizeOf<INPUT>());
            if (modifiers.Count > 0) Thread.Sleep(25);

            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                var modUp = new[] { CreateKeyInput(modifiers[i], keyUp: true) };
                _ = SendInput(1, modUp, Marshal.SizeOf<INPUT>());
            }
        });

        return true;
    }

    private static IntPtr FindDiscordWindow()
    {
        foreach (string name in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero &&
                        IsWindowVisible(proc.MainWindowHandle))
                    {
                        return proc.MainWindowHandle;
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        return IntPtr.Zero;
    }

    private static bool TryParseKeybind(string keybind, out List<ushort> modifiers, out ushort mainKey)
    {
        modifiers = new List<ushort>();
        mainKey = 0;

        foreach (string raw in keybind.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string token = raw.ToUpperInvariant();
            switch (token)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers.Add(0xA2); // VK_LCONTROL
                    break;
                case "SHIFT":
                    modifiers.Add(0xA0); // VK_LSHIFT
                    break;
                case "ALT":
                    modifiers.Add(0xA4); // VK_LMENU
                    break;
                default:
                    mainKey = ParseVirtualKey(token);
                    break;
            }
        }

        return mainKey != 0;
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        uint scan = MapVirtualKey(vk, 0);
        uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C)
            flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private static ushort ParseVirtualKey(string token)
    {
        if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            return token[0];

        if (token.StartsWith('F') && int.TryParse(token[1..], out int fNum) && fNum is >= 1 and <= 24)
            return (ushort)(0x70 + fNum - 1);

        return token switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" => 0x1B,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            _ => 0
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
