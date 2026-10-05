namespace BoostParaPc.Services
{
    // Only external boundaries are substituted. Cleanup and startup services are linked production files.
    internal static class AppPaths
    {
        public static string DataDir { get; set; } = "";
        public static string BackupDir => Path.Combine(DataDir, "backups");
    }
    internal static class ProcessRunner
    {
        public static Task<string> RunPowerShellAsync(string script, int timeoutMs = 30000, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Tests must never execute system commands.");
    }
    internal static class RegistryBackupService
    {
        public static string CreateBackup(string id, IEnumerable<(Microsoft.Win32.RegistryKey root, string keyPath, string valueName)> entries)
            => throw new InvalidOperationException("Tests must never mutate the real registry.");
        public static int RestoreMatchingBackups(string id)
            => throw new InvalidOperationException("Tests must never mutate the real registry.");
    }
}
namespace BoostParaPc.Models
{
    public sealed class StartupItem
    {
        public required string Name { get; init; }
        public required string Command { get; init; }
        public required string Source { get; init; }
        public required string Location { get; init; }
        public bool IsEnabled { get; set; } = true;
    }
}
