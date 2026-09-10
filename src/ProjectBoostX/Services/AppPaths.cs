using System.IO;

namespace BoostParaPc.Services;

/// <summary>
/// Pastas de dados do app. Novo nome: ProjectBoostX; migra BoostParaPc legado se existir.
/// </summary>
public static class AppPaths
{
    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectBoostX");

    private static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoostParaPc");

    private static bool _migrated;

    public static void EnsureMigrated()
    {
        if (_migrated) return;
        _migrated = true;
        try
        {
            Directory.CreateDirectory(Root);
            if (!Directory.Exists(LegacyRoot)) return;

            foreach (var dir in new[] { "backups" })
            {
                var src = Path.Combine(LegacyRoot, dir);
                var dst = Path.Combine(Root, dir);
                if (!Directory.Exists(src)) continue;
                Directory.CreateDirectory(dst);
                foreach (var file in Directory.GetFiles(src))
                {
                    var target = Path.Combine(dst, Path.GetFileName(file));
                    if (!File.Exists(target))
                        File.Copy(file, target);
                }
            }

            foreach (var file in Directory.GetFiles(LegacyRoot, "*.json"))
            {
                var target = Path.Combine(Root, Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Copy(file, target);
            }
        }
        catch { }
    }

    public static string DataDir
    {
        get
        {
            EnsureMigrated();
            Directory.CreateDirectory(Root);
            return Root;
        }
    }

    public static string BackupDir
    {
        get
        {
            var d = Path.Combine(DataDir, "backups");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string AppliedFile => Path.Combine(DataDir, "applied.json");
}
