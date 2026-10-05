namespace BoostParaPc.Services
{
    public static class AppPaths
    {
        public static string DataDir { get; set; } = "";
        public static string BackupDir => Path.Combine(DataDir, "backups");
    }
}

namespace BoostParaPc.Models
{
    public sealed class GameProfile
    {
        public required string Name { get; init; }
        public required string ExecutablePath { get; init; }
        public string Platform { get; init; } = "";
        public string? AppId { get; init; }
        public int? SteamAppId { get; set; }
        public string RecommendationKey { get; init; } = "casual";
        public bool IsFullscreenOptDisabled { get; set; }
        public bool IsHighDpiOverridden { get; set; }
        public bool IsHighPriority { get; set; }
        public bool IsGpuPreferred { get; set; }
        public string Status { get; set; } = "—";
        public string FileName => Path.GetFileName(ExecutablePath);
    }
}
