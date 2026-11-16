using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace HyperNotch.Services;

public sealed class SystemStats
{
    public double CpuPercent { get; init; }
    public double RamPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double GpuPercent { get; init; }
    public double DownloadMbps { get; init; }
    public double UploadMbps { get; init; }
    public bool HasBattery { get; init; }
    public int BatteryPercent { get; init; }
    public bool IsCharging { get; init; }
}

public sealed class SystemMonitorService : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrevCpu;

    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTime _prevNetTime = DateTime.UtcNow;
    private bool _hasPrevNet;

    private readonly List<PerformanceCounter> _gpuCounters = new();
    private DateTime _lastGpuRefresh = DateTime.MinValue;

    public event Action<SystemStats>? StatsUpdated;

    public void Start()
    {
        _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        SampleCpu();
        SampleNetwork();

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                double cpu = SampleCpu();
                var (ramPct, usedGb, totalGb) = SampleMemory();
                double gpu = SampleGpu();
                var (downMbps, upMbps) = SampleNetwork();
                var (hasBat, batPct, charging) = SampleBattery();

                StatsUpdated?.Invoke(new SystemStats
                {
                    CpuPercent = Math.Round(cpu, 1),
                    RamPercent = Math.Round(ramPct, 1),
                    RamUsedGb = Math.Round(usedGb, 1),
                    RamTotalGb = Math.Round(totalGb, 1),
                    GpuPercent = Math.Round(gpu, 1),
                    DownloadMbps = Math.Round(downMbps, 2),
                    UploadMbps = Math.Round(upMbps, 2),
                    HasBattery = hasBat,
                    BatteryPercent = batPct,
                    IsCharging = charging
                });
            }
            catch
            {
                // Safe fallback
            }
        }
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
            return 0;

        ulong idle = ToUInt64(idleTime);
        ulong kernel = ToUInt64(kernelTime);
        ulong user = ToUInt64(userTime);

        if (!_hasPrevCpu)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrevCpu = true;
            return 0;
        }

        ulong idleDiff = idle - _prevIdle;
        ulong kernelDiff = kernel - _prevKernel;
        ulong userDiff = user - _prevUser;

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        ulong total = kernelDiff + userDiff;
        if (total == 0) return 0;

        return Math.Clamp((double)(total - idleDiff) / total * 100.0, 0, 100);
    }

    private static (double Percent, double UsedGb, double TotalGb) SampleMemory()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref memStatus))
            return (0, 0, 0);

        double totalGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
        double availGb = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
        double usedGb = Math.Max(0, totalGb - availGb);
        return (memStatus.dwMemoryLoad, usedGb, totalGb);
    }

    private double SampleGpu()
    {
        // 1. Universal Windows 10/11 GPU Engine PerformanceCounter (AMD / Intel / NVIDIA)
        try
        {
            if ((DateTime.UtcNow - _lastGpuRefresh).TotalSeconds > 15 || _gpuCounters.Count == 0)
            {
                foreach (var c in _gpuCounters) c.Dispose();
                _gpuCounters.Clear();

                var category = new PerformanceCounterCategory("GPU Engine");
                foreach (string inst in category.GetInstanceNames())
                {
                    if (inst.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    {
                        var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                        _ = counter.NextValue();
                        _gpuCounters.Add(counter);
                    }
                }
                _lastGpuRefresh = DateTime.UtcNow;
            }

            if (_gpuCounters.Count > 0)
            {
                float sum = 0;
                foreach (var c in _gpuCounters)
                {
                    try { sum += c.NextValue(); } catch { }
                }
                return Math.Clamp(sum, 0, 100);
            }
        }
        catch
        {
            // Fallback to nvidia-smi if GPU Engine counters are unavailable
        }

        // 2. Fallback via nvidia-smi without console window
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc is not null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(800);
                if (double.TryParse(output.Split('\n')[0].Trim(), out double gpuVal))
                    return Math.Clamp(gpuVal, 0, 100);
            }
        }
        catch { }

        return 0;
    }

    private (double DownMbps, double UpMbps) SampleNetwork()
    {
        long totalRx = 0;
        long totalTx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                var stats = ni.GetIPv4Statistics();
                totalRx += stats.BytesReceived;
                totalTx += stats.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        DateTime now = DateTime.UtcNow;
        if (!_hasPrevNet)
        {
            _prevBytesReceived = totalRx;
            _prevBytesSent = totalTx;
            _prevNetTime = now;
            _hasPrevNet = true;
            return (0, 0);
        }

        double elapsed = Math.Max(0.2, (now - _prevNetTime).TotalSeconds);
        double downMbps = Math.Max(0, (totalRx - _prevBytesReceived) / elapsed / (1024.0 * 1024.0));
        double upMbps = Math.Max(0, (totalTx - _prevBytesSent) / elapsed / (1024.0 * 1024.0));

        _prevBytesReceived = totalRx;
        _prevBytesSent = totalTx;
        _prevNetTime = now;
        return (downMbps, upMbps);
    }

    private static (bool HasBattery, int Percent, bool IsCharging) SampleBattery()
    {
        try
        {
            var ps = Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
                return (false, 0, false);

            int pct = (int)Math.Clamp(Math.Round(ps.BatteryLifePercent * 100), 0, 100);
            bool charging = ps.PowerLineStatus == Forms.PowerLineStatus.Online;
            return (true, pct, charging);
        }
        catch
        {
            return (false, 0, false);
        }
    }

    private static ulong ToUInt64(FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters.Clear();
    }
}
