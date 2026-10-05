using System.IO;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>Preferências do usuário, em AppData\ProjectBoostX\settings.json. Arquivo ausente, corrompido ou de versão futura volta aos padrões.</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;
    private static readonly object Gate = new();
    private static AppSettings? _current;

    public int Version { get; set; } = CurrentVersion;
    public bool RequireRestorePoint { get; set; } = true;
    public bool FirstRunAcknowledged { get; set; }
    public bool AutoGameMode { get; set; }
    public bool StartWithWindows { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public List<string> BackgroundApps { get; set; } = ["OneDrive.exe", "ms-teams.exe", "Teams.exe"];

    public static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static AppSettings Current
    {
        get { lock (Gate) return _current ??= Load(); }
    }

    /// <summary>Descarta o cache e lê de novo do disco (usado nos testes).</summary>
    public static void Reset() { lock (Gate) _current = null; }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
            return loaded is { Version: >= 1 and <= CurrentVersion } ? loaded : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, overwrite: true);
            _current = this;
        }
    }
}
