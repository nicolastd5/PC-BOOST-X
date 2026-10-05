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
    public static class AppPaths
    {
        public static string DataDir { get; set; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BoostCommandsTests", Guid.NewGuid().ToString("N"));
    }
}
namespace BoostParaPc.ViewModels
{
    /// <summary>Registra quais telas foram construídas, para provar que a criação é sob demanda.</summary>
    public static class Constructed
    {
        public static List<string> Names { get; } = [];
    }

    public class HomeViewModel : IScreen
    {
        public HomeViewModel(MainViewModel main) => Constructed.Names.Add("Home");
        public Task ActivateAsync() => Task.CompletedTask;
        public void Deactivate() { }
    }
    public class OptimizationsViewModel : IScreen
    {
        public OptimizationsViewModel(MainViewModel main) => Constructed.Names.Add("Optimizations");
        public int Activations { get; private set; }
        public int Deactivations { get; private set; }
        public Task ActivateAsync() { Activations++; return Task.CompletedTask; }
        public void Deactivate() => Deactivations++;
    }
    public class GamesViewModel { public GamesViewModel(MainViewModel main) => Constructed.Names.Add("Games"); }
    public class CleanupViewModel { public CleanupViewModel(MainViewModel main) => Constructed.Names.Add("Cleanup"); }
    public class StartupViewModel { public StartupViewModel(MainViewModel main) => Constructed.Names.Add("Startup"); }
    public class MonitorViewModel { public MonitorViewModel(MainViewModel main) => Constructed.Names.Add("Monitor"); }
    public class HistoryViewModel { public HistoryViewModel(MainViewModel main) => Constructed.Names.Add("History"); }
    public class SettingsViewModel { public SettingsViewModel(MainViewModel main) => Constructed.Names.Add("Settings"); }
}
