using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Forms = System.Windows.Forms;

namespace HyperNotch.Services;

public sealed class DeviceMonitorService : IDisposable
{
    private const string BatteryPropKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private readonly CancellationTokenSource _cts = new();
    private readonly object _btLock = new();
    private readonly HashSet<string> _knownConnectedIds = new();
    private DeviceWatcher? _btWatcher;
    private bool _initialBtScanDone;
    private Forms.PowerLineStatus? _lastPowerLine;

    public event Action<int>? ChargerConnected;
    public event Action<string, int?>? BluetoothConnected;

    public void Start()
    {
        StartPowerMonitor();
        StartBluetoothWatcher();
    }

    private void StartPowerMonitor()
    {
        try
        {
            var ps = Forms.SystemInformation.PowerStatus;
            if (!ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
            {
                _lastPowerLine = ps.PowerLineStatus;
                _ = Task.Run(() => PowerPollLoopAsync(_cts.Token));
            }
        }
        catch { }
    }

    private async Task PowerPollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, token);
                var ps = Forms.SystemInformation.PowerStatus;
                var current = ps.PowerLineStatus;
                if (_lastPowerLine != Forms.PowerLineStatus.Online && current == Forms.PowerLineStatus.Online)
                {
                    int pct = (int)Math.Clamp(Math.Round(ps.BatteryLifePercent * 100), 0, 100);
                    ChargerConnected?.Invoke(pct);
                }
                _lastPowerLine = current;
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void StartBluetoothWatcher()
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            string[] requestedProps = [BatteryPropKey, "System.Devices.Aep.IsConnected"];
            _btWatcher = DeviceInformation.CreateWatcher(selector, requestedProps);

            _btWatcher.EnumerationCompleted += (_, _) => _initialBtScanDone = true;

            _btWatcher.Added += (_, info) =>
            {
                bool isNew;
                lock (_btLock)
                    isNew = _knownConnectedIds.Add(info.Id);
                if (_initialBtScanDone && isNew && !string.IsNullOrWhiteSpace(info.Name))
                {
                    int? bat = ExtractBattery(info.Properties);
                    BluetoothConnected?.Invoke(info.Name, bat);
                }
            };

            _btWatcher.Removed += (_, update) =>
            {
                lock (_btLock)
                    _knownConnectedIds.Remove(update.Id);
            };

            _btWatcher.Start();
        }
        catch
        {
            // Bluetooth radio may not be present
        }
    }

    private static int? ExtractBattery(IReadOnlyDictionary<string, object> props)
    {
        try
        {
            if (props.TryGetValue(BatteryPropKey, out object? val) && val is not null)
            {
                int pct = Convert.ToInt32(val);
                if (pct is >= 0 and <= 100)
                    return pct;
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        try
        {
            if (_btWatcher is not null &&
                (_btWatcher.Status == DeviceWatcherStatus.Started || _btWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
            {
                _btWatcher.Stop();
            }
        }
        catch { }
    }
}
