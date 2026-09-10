using System.Diagnostics;
using BoostParaPc.Models;

namespace BoostParaPc.Services;

/// <summary>
/// Medidor ao vivo de CPU, RAM, disco e rede (sem dependência externa).
/// </summary>
public sealed class PerformanceMonitorService : IDisposable
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _ramAvailable;
    private readonly PerformanceCounter _diskTime;
    private readonly PerformanceCounter _netBytes;
    private readonly Timer _timer;
    private readonly long _totalRamBytes;
    private readonly object _lock = new();

    public event EventHandler<PerformanceSample>? SampleUpdated;

    public bool IsRunning { get; private set; }

    public PerformanceMonitorService()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _ramAvailable = new PerformanceCounter("Memory", "Available MBytes");
        try { _diskTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); }
        catch { _diskTime = new PerformanceCounter("Processor", "% Processor Time", "_Total"); }

        // Rede: primeiro adaptador não-loopback
        try
        {
            var cat = new PerformanceCounterCategory("Network Interface");
            var instances = cat.GetInstanceNames();
            var nic = instances.FirstOrDefault(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                      ?? instances.FirstOrDefault();
            _netBytes = nic is null
                ? new PerformanceCounter("Memory", "Available MBytes")
                : new PerformanceCounter("Network Interface", "Bytes Total/sec", nic);
        }
        catch
        {
            _netBytes = new PerformanceCounter("Memory", "Available MBytes");
        }

        _totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        _cpuCounter.NextValue(); // warmup

        _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(int intervalMs = 1000)
    {
        IsRunning = true;
        _timer.Change(0, intervalMs);
    }

    public void Stop()
    {
        IsRunning = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnTick(object? state)
    {
        if (!IsRunning) return;
        try
        {
            float cpu, ramUsedPct, disk, netKbps;
            lock (_lock)
            {
                cpu = Math.Clamp(_cpuCounter.NextValue(), 0, 100);
                var availableMb = _ramAvailable.NextValue();
                var totalMb = _totalRamBytes / (1024.0 * 1024.0);
                ramUsedPct = totalMb <= 0 ? 0 : Math.Clamp((float)((totalMb - availableMb) / totalMb * 100.0), 0, 100);
                disk = Math.Clamp(_diskTime.NextValue(), 0, 100);
                var bytesPerSec = _netBytes.NextValue();
                netKbps = Math.Max(0, bytesPerSec / 1024f);
            }

            var processCount = Process.GetProcesses().Length;
            SampleUpdated?.Invoke(this, new PerformanceSample(cpu, ramUsedPct, disk, netKbps, processCount, DateTime.Now));
        }
        catch
        {
            // contador pode falhar em alguns setups
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _cpuCounter.Dispose();
        _ramAvailable.Dispose();
        _diskTime.Dispose();
        _netBytes.Dispose();
    }
}

public sealed record PerformanceSample(
    float CpuPercent,
    float RamPercent,
    float DiskPercent,
    float NetworkKbps,
    int ProcessCount,
    DateTime Timestamp);
