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
    private static readonly object MigrationLock = new();

    public static void EnsureMigrated()
    {
        lock (MigrationLock)
        {
            if (_migrated) return;
            try
            {
                Directory.CreateDirectory(Root);
                var marker = Path.Combine(Root, ".legacy-migration-complete");
                if (File.Exists(marker) || !Directory.Exists(LegacyRoot))
                {
                    _migrated = true;
                    return;
                }

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
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
                _migrated = true;
            }
            catch (Exception ex) { throw new IOException("Não foi possível migrar os backups antigos. Nenhum ajuste deve ser aplicado antes de recuperar o acesso aos dados.", ex); }
        }
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
