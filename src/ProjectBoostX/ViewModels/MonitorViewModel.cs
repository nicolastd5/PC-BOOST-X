using BoostParaPc.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoostParaPc.ViewModels;

public partial class MonitorViewModel : ObservableObject, IScreen, IDisposable
{
    public const int HistoryLength = 60;

    private readonly PerformanceMonitorService _service = new();
    private readonly Queue<double>[] _history = Enumerable.Range(0, 6).Select(_ => new Queue<double>()).ToArray();

    [ObservableProperty] private string _cpuDisplay = "—";
    [ObservableProperty] private string _ramDisplay = "—";
    [ObservableProperty] private string _diskDisplay = "—";
    [ObservableProperty] private string _netDisplay = "—";
    [ObservableProperty] private string _gpuDisplay = "—";
    [ObservableProperty] private string _vramDisplay = "—";
    [ObservableProperty] private int _processCount;

    [ObservableProperty] private IReadOnlyList<double> _cpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _ramHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _diskHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _netHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _gpuHistory = [];
    [ObservableProperty] private IReadOnlyList<double> _vramHistory = [];

    /// <summary>Escala do gráfico de rede: o maior valor recente, no mínimo 1 MB/s.</summary>
    [ObservableProperty] private double _netMax = 1024;

    public MonitorViewModel(MainViewModel main) => _service.SampleUpdated += OnSample;

    public Task ActivateAsync()
    {
        _service.Start();
        return Task.CompletedTask;
    }

    public void Deactivate() => _service.Stop();

    private void OnSample(object? sender, PerformanceSample s) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        ProcessCount = s.ProcessCount;
        CpuDisplay = $"{s.CpuPercent:0}%";
        RamDisplay = $"{s.RamPercent:0}%";
        DiskDisplay = $"{s.DiskPercent:0}%";
        GpuDisplay = $"{s.GpuPercent:0}%";
        VramDisplay = s.VramMb >= 1024 ? $"{s.VramMb / 1024:0.0} GB" : $"{s.VramMb:0} MB";
        NetDisplay = s.NetworkKbps >= 1024 ? $"{s.NetworkKbps / 1024:0.0} MB/s" : $"{s.NetworkKbps:0} KB/s";

        CpuHistory = Push(0, s.CpuPercent);
        RamHistory = Push(1, s.RamPercent);
        DiskHistory = Push(2, s.DiskPercent);
        NetHistory = Push(3, s.NetworkKbps);
        GpuHistory = Push(4, s.GpuPercent);
        VramHistory = Push(5, s.VramMb);
        NetMax = Math.Max(1024, NetHistory.Max());
    });

    private IReadOnlyList<double> Push(int index, double value)
    {
        var queue = _history[index];
        queue.Enqueue(value);
        while (queue.Count > HistoryLength) queue.Dequeue();
        return queue.ToArray();
    }

    public void Dispose() => _service.Dispose();
}
