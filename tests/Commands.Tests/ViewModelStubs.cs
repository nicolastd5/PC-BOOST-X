using CommunityToolkit.Mvvm.Input;

// Isolate the real MainViewModel coordinator from Windows state and feature services.
namespace BoostParaPc.Models
{
    public sealed class SystemInfo { }
}
namespace BoostParaPc.Services
{
    public static class SystemInfoService
    {
        public static bool IsAdministrator() => false;
        public static Models.SystemInfo GetSystemInfo() => new();
    }
}
namespace BoostParaPc.ViewModels
{
    public class DashboardViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public bool RestorePointEnabled { get; set; } = true;
        public Task InitializeAsync() => Task.CompletedTask;
    }
    public class GamingViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public Task InitializeAsync() => Task.CompletedTask;
    }
    public class GameProfilesViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public List<object> Games { get; } = [];
        public AsyncRelayCommand DetectCommand { get; } = new(() => Task.CompletedTask);
    }
    public class CleanupViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public AsyncRelayCommand ScanCommand { get; } = new(() => Task.CompletedTask);
    }
    public class DebloatViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public List<object> Services { get; } = [];
        public AsyncRelayCommand LoadCommand { get; } = new(() => Task.CompletedTask);
    }
    public class StartupViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public AsyncRelayCommand LoadCommand { get; } = new(() => Task.CompletedTask);
    }
    public class MonitorViewModel(MainViewModel main)
    {
        private readonly MainViewModel _ = main;
        public void Start() { }
        public void Stop() { }
    }
}
