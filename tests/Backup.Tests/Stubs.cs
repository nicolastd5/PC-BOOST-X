namespace Microsoft.Win32
{
    public enum RegistryValueKind { Unknown, String, ExpandString, Binary, DWord, MultiString, QWord, None }
    public enum RegistryValueOptions { DoNotExpandEnvironmentNames }
    public static class Registry
    {
        public static RegistryKey CurrentUser = new("HKEY_CURRENT_USER");
        public static RegistryKey LocalMachine = new("HKEY_LOCAL_MACHINE");
    }
    public sealed class RegistryKey(string name) : IDisposable
    {
        public string Name => name;
        public bool DenyWrites { get; set; }
        private readonly Dictionary<string, RegistryKey> children = new();
        private readonly Dictionary<string, (object Value, RegistryValueKind Kind)> values = new();
        public RegistryKey? OpenSubKey(string path, bool writable = false) => children.GetValueOrDefault(path);
        public RegistryKey CreateSubKey(string path, bool writable = false)
        {
            if (!children.TryGetValue(path, out var key)) children[path] = key = new(name + "\\" + path);
            return key;
        }
        public object? GetValue(string valueName, object? fallback = null, RegistryValueOptions options = default)
            => values.TryGetValue(valueName, out var value) ? value.Value : fallback;
        public RegistryValueKind GetValueKind(string valueName) => values[valueName].Kind;
        public void SetValue(string valueName, object value, RegistryValueKind kind)
        {
            if (DenyWrites) throw new UnauthorizedAccessException("Simulated protected key");
            values[valueName] = (value, kind);
        }
        public void DeleteValue(string valueName, bool throwOnMissingValue = false)
        {
            if (DenyWrites) throw new UnauthorizedAccessException("Simulated protected key");
            values.Remove(valueName);
        }
        public void Dispose() { }
    }
}
namespace BoostParaPc.Services
{
    public static class AppPaths
    {
        public static string Root = "";
        public static string DataDir => Root;
        public static string AppliedFile => Path.Combine(Root, "applied.json");
        public static string BackupDir { get { var dir = Path.Combine(Root, "backups"); Directory.CreateDirectory(dir); return dir; } }
    }
    public static class OptimizationStateStore
    {
        public static bool Cleared;
        public static void ClearAll() => Cleared = true;
    }
    public static class ProcessRunner
    {
        public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
        { public bool Success => ExitCode == 0 && !TimedOut; }
        public static List<string> Calls = [];
        public static Task<CommandResult> RunAsync(string executable, string arguments, int timeoutMs = 30000, bool elevated = false)
        {
            Calls.Add(executable + " " + arguments);
            return Task.FromResult(new CommandResult(5, "", "Simulated failure", false));
        }
        public static async Task<CommandResult> RunCheckedAsync(string executable, string arguments, int timeoutMs = 30000, bool elevated = false)
        {
            var r = await RunAsync(executable, arguments, timeoutMs, elevated);
            if (!r.Success) throw new IOException(r.StdErr);
            return r;
        }
    }
    public static class StartupService { public static Task RevertAllAsync() => Task.CompletedTask; }
    public static class SystemSettingsBackupService { public static Task RevertAllAsync() => Task.CompletedTask; }
}
