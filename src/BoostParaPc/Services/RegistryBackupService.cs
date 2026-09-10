using System.IO;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Backup/restore de valores de registro antes de otimizações.
/// Guarda em JSON local para permitir reverter com segurança.
/// </summary>
public static class RegistryBackupService
{
    private static string BackupDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoostParaPc", "backups");

    private sealed record BackupEntry(
        string Hive,
        string KeyPath,
        string ValueName,
        string Kind,
        string? StringValue,
        int? DwordValue,
        string[]? MultiValue);

    public static string CreateBackup(string id, IEnumerable<(RegistryKey root, string keyPath, string valueName)> entries)
    {
        Directory.CreateDirectory(BackupDir);
        var list = new List<BackupEntry>();

        foreach (var (root, keyPath, valueName) in entries)
        {
            using var key = root.OpenSubKey(keyPath, writable: false);
            if (key is null)
            {
                list.Add(new BackupEntry(root.Name, keyPath, valueName, "Missing", null, null, null));
                continue;
            }

            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                list.Add(new BackupEntry(root.Name, keyPath, valueName, "Missing", null, null, null));
                continue;
            }

            var kind = key.GetValueKind(valueName).ToString();
            list.Add(kind switch
            {
                "DWord" => new BackupEntry(root.Name, keyPath, valueName, kind, null, Convert.ToInt32(value), null),
                "QWord" => new BackupEntry(root.Name, keyPath, valueName, kind, null, unchecked((int)Convert.ToInt64(value)), null),
                "MultiString" => new BackupEntry(root.Name, keyPath, valueName, kind, null, null, (string[])value),
                _ => new BackupEntry(root.Name, keyPath, valueName, kind, value.ToString(), null, null)
            });
        }

        var file = Path.Combine(BackupDir, $"{id}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    public static int RestoreBackup(string file)
    {
        if (!File.Exists(file)) return 0;
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<BackupEntry>>(File.ReadAllText(file));
        if (entries is null) return 0;

        var restored = 0;
        foreach (var e in entries)
        {
            try
            {
                var root = e.Hive switch
                {
                    "HKEY_CURRENT_USER" => Registry.CurrentUser,
                    "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                    _ => null
                };
                if (root is null) continue;

                using var key = root.CreateSubKey(e.KeyPath, writable: true);
                if (key is null) continue;

                if (e.Kind == "Missing")
                {
                    key.DeleteValue(e.ValueName, throwOnMissingValue: false);
                }
                else if (e.Kind is "DWord" or "QWord")
                {
                    key.SetValue(e.ValueName, e.DwordValue ?? 0, e.Kind == "QWord" ? RegistryValueKind.QWord : RegistryValueKind.DWord);
                }
                else if (e.Kind == "MultiString")
                {
                    key.SetValue(e.ValueName, e.MultiValue ?? [], RegistryValueKind.MultiString);
                }
                else
                {
                    key.SetValue(e.ValueName, e.StringValue ?? string.Empty, RegistryValueKind.String);
                }
                restored++;
            }
            catch
            {
                // ignora chaves protegidas
            }
        }
        return restored;
    }

    public static IReadOnlyList<string> ListBackups()
    {
        if (!Directory.Exists(BackupDir)) return [];
        return Directory.GetFiles(BackupDir, "*.json")
            .OrderByDescending(File.GetCreationTimeUtc)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
    }

    public static void SetDword(RegistryKey root, string keyPath, string name, int value)
    {
        using var key = root.CreateSubKey(keyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }

    public static int? GetDword(RegistryKey root, string keyPath, string name)
    {
        using var key = root.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(name) is int i ? i : key?.GetValue(name) is long l ? (int)l : null;
    }

    public static void SetString(RegistryKey root, string keyPath, string name, string value)
    {
        using var key = root.CreateSubKey(keyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }
}
