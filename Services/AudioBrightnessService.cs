using System.Management;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace HyperNotch.Services;

public sealed class AudioBrightnessService : IDisposable, IMMNotificationClient
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _audioLock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _defaultDevice;
    private int _lastVolume = -1;
    private bool _lastMuted;
    private int _lastBrightness = -1;

    public event Action<int, bool>? VolumeChanged;
    public event Action<int>? BrightnessChanged;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            HookDefaultAudioDevice();
        }
        catch { }

        _ = Task.Run(() => BrightnessPollLoopAsync(_cts.Token));
    }

    private void HookDefaultAudioDevice()
    {
        lock (_audioLock)
        {
            try
            {
                _defaultDevice?.Dispose();
                _defaultDevice = null;

                if (_enumerator is null) return;
                _defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _lastVolume = (int)Math.Round(_defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                _lastMuted = _defaultDevice.AudioEndpointVolume.Mute;

                _defaultDevice.AudioEndpointVolume.OnVolumeNotification += data =>
                {
                    int vol = (int)Math.Round(data.MasterVolume * 100);
                    bool muted = data.Muted;
                    if (vol != _lastVolume || muted != _lastMuted)
                    {
                        _lastVolume = vol;
                        _lastMuted = muted;
                        VolumeChanged?.Invoke(vol, muted);
                    }
                };
            }
            catch { }
        }
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            HookDefaultAudioDevice();
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public (int Volume, bool IsMuted) GetCurrentVolume()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            int vol = (int)Math.Round(dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            return (vol, dev.AudioEndpointVolume.Mute);
        }
        catch { }
        return (50, false);
    }

    public void SetVolume(int percent)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            float scalar = Math.Clamp(percent, 0, 100) / 100f;
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
            if (percent > 0 && dev.AudioEndpointVolume.Mute)
                dev.AudioEndpointVolume.Mute = false;
        }
        catch { }
    }

    public void ToggleMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            dev.AudioEndpointVolume.Mute = !dev.AudioEndpointVolume.Mute;
        }
        catch { }
    }

    private async Task BrightnessPollLoopAsync(CancellationToken token)
    {
        _lastBrightness = ReadBrightness();
        if (_lastBrightness < 0)
            return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, token);
                int current = ReadBrightness();
                if (current >= 0 && current != _lastBrightness)
                {
                    _lastBrightness = current;
                    BrightnessChanged?.Invoke(current);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private static int ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                return Convert.ToInt32(obj["CurrentBrightness"]);
            }
        }
        catch { }
        return -1;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        lock (_audioLock)
        {
            try { _enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
            try { _defaultDevice?.Dispose(); _defaultDevice = null; } catch { }
            try { _enumerator?.Dispose(); _enumerator = null; } catch { }
        }
    }
}
