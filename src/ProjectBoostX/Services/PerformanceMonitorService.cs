using System.Diagnostics;

namespace BoostParaPc.Services;

/// <summary>
/// Medidor ao vivo de CPU, RAM, disco, rede, GPU e VRAM. Os contadores são criados em segundo plano no primeiro Start,
/// nunca no construtor: abrir o programa não cria nenhum PerformanceCounter.
/// </summary>
public sealed class PerformanceMonitorService : IDisposable
{
    private readonly object _lock = new();
    private readonly long _totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    private Timer? _timer;
    private PerformanceCounter? _cpu, _ramAvailable, _disk, _processes;
    private List<PerformanceCounter> _net = [];
    private Dictionary<string, CounterSample> _gpuPrevious = [];
    private bool _ready, _disposed;
    private int _intervalMs = 1000;

    public event EventHandler<PerformanceSample>? SampleUpdated;

    public bool IsRunning { get; private set; }

    public void Start(int intervalMs = 1000)
    {
        if (_disposed || IsRunning) return;
        IsRunning = true;
        _intervalMs = intervalMs;
        _ = Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_ready) CreateCounters();
                if (IsRunning && !_disposed) (_timer ??= new Timer(OnTick)).Change(0, _intervalMs);
            }
        });
    }

    public void Stop()
    {
        IsRunning = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void CreateCounters()
    {
        _cpu = Create("Processor Information", "% Processor Utility", "_Total") ?? Create("Processor", "% Processor Time", "_Total");
        _ramAvailable = Create("Memory", "Available MBytes");
        _disk = Create("PhysicalDisk", "% Disk Time", "_Total");
        _processes = Create("System", "Processes");
        try
        {
            foreach (var instance in new PerformanceCounterCategory("Network Interface").GetInstanceNames())
                if (Create("Network Interface", "Bytes Total/sec", instance) is { } counter) _net.Add(counter);
        }
        catch { /* sem interface de rede */ }
        _cpu?.NextValue();
        _ready = true;
    }

    private static PerformanceCounter? Create(string category, string counter, string? instance = null)
    {
        try { return instance is null ? new PerformanceCounter(category, counter) : new PerformanceCounter(category, counter, instance); }
        catch { return null; }
    }

    private void OnTick(object? state)
    {
        if (!IsRunning || _disposed) return;
        try
        {
            float cpu, ram, disk, net;
            int processes;
            lock (_lock)
            {
                cpu = Math.Clamp(_cpu?.NextValue() ?? 0, 0, 100);
                var totalMb = _totalRamBytes / (1024.0 * 1024.0);
                ram = totalMb <= 0 || _ramAvailable is null ? 0 : Math.Clamp((float)((totalMb - _ramAvailable.NextValue()) / totalMb * 100.0), 0, 100);
                disk = Math.Clamp(_disk?.NextValue() ?? 0, 0, 100);
                net = Math.Max(0, _net.Sum(SafeNext) / 1024f);
                processes = (int)(_processes?.NextValue() ?? 0);
            }
            var (gpu, vramMb) = ReadGpu();
            SampleUpdated?.Invoke(this, new PerformanceSample(cpu, ram, disk, net, processes, DateTime.Now, gpu, vramMb));
        }
        catch { /* contador pode falhar em alguns setups */ }
    }

    private static float SafeNext(PerformanceCounter counter)
    {
        try { return counter.NextValue(); } catch { return 0; }
    }

    /// <summary>As instâncias de GPU mudam conforme processos abrem e fecham: lê a categoria inteira a cada amostra.</summary>
    private (float Gpu, float VramMb) ReadGpu()
    {
        try
        {
            var engines = new PerformanceCounterCategory("GPU Engine").ReadCategory();
            float gpu = 0;
            var current = new Dictionary<string, CounterSample>();
            if (engines["Utilization Percentage"] is { } utilization)
                foreach (System.Collections.DictionaryEntry entry in utilization)
                {
                    var name = (string)entry.Key;
                    var sample = ((InstanceData)entry.Value!).Sample;
                    current[name] = sample;
                    if (name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase) && _gpuPrevious.TryGetValue(name, out var previous))
                        gpu += CounterSample.Calculate(previous, sample);
                }
            _gpuPrevious = current;

            float vram = 0;
            var memory = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
            if (memory["Dedicated Usage"] is { } dedicated)
                foreach (System.Collections.DictionaryEntry entry in dedicated)
                    vram += ((InstanceData)entry.Value!).RawValue / (1024f * 1024f);
            return (Math.Clamp(gpu, 0, 100), vram);
        }
        catch { return (0, 0); }
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        lock (_lock)
        {
            _timer?.Dispose();
            _cpu?.Dispose(); _ramAvailable?.Dispose(); _disk?.Dispose(); _processes?.Dispose();
            foreach (var counter in _net) counter.Dispose();
        }
    }
}

public sealed record PerformanceSample(
    float CpuPercent,
    float RamPercent,
    float DiskPercent,
    float NetworkKbps,
    int ProcessCount,
    DateTime Timestamp,
    float GpuPercent = 0,
    float VramMb = 0);
