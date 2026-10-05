using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>Snapshots sem perda, consumidos apenas após restauração completa.</summary>
public static class RegistryBackupService
{
    private static string BackupDir => AppPaths.BackupDir;
    private static readonly object Gate = new();
    private static long _lastTimestamp;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record BackupEntry(string Hive, string KeyPath, string ValueName, string Kind,
        string? StringValue, int? DwordValue, string[]? MultiValue,
        long? QwordValue = null, byte[]? BinaryValue = null);
    private sealed record Snapshot(int Version, DateTime CreatedUtc, List<BackupEntry> Entries);

    public static string CreateBackup(string id, IEnumerable<(RegistryKey root, string keyPath, string valueName)> entries)
    {
        ValidateId(id);
        lock (Gate)
        {
            var list = new List<BackupEntry>();
            foreach (var (root, path, name) in entries.Distinct())
            {
                using var key = root.OpenSubKey(path, writable: false);
                var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var entry = new BackupEntry(root.Name, path, name, "Missing", null, null, null);
                if (value is not null && key is not null)
                {
                    var kind = key.GetValueKind(name);
                    entry = entry with { Kind = kind.ToString() };
                    entry = kind switch
                    {
                        RegistryValueKind.DWord => entry with { DwordValue = Convert.ToInt32(value) },
                        RegistryValueKind.QWord => entry with { QwordValue = Convert.ToInt64(value) },
                        RegistryValueKind.Binary or RegistryValueKind.None => entry with { BinaryValue = (byte[])value },
                        RegistryValueKind.MultiString => entry with { MultiValue = (string[])value },
                        RegistryValueKind.String or RegistryValueKind.ExpandString => entry with { StringValue = (string)value },
                        _ => throw new InvalidDataException($"Tipo de Registro não suportado: {kind}")
                    };
                }
                list.Add(entry);
            }
            Directory.CreateDirectory(BackupDir);
            var now = new DateTime(_lastTimestamp = Math.Max(DateTime.UtcNow.Ticks, _lastTimestamp + 1), DateTimeKind.Utc);
            var file = Path.Combine(BackupDir, $"{id}_{now:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json");
            var temporary = file + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Snapshot(2, now, list), JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file);
            return file;
        }
    }

    public static int RestoreBackup(string file)
    {
        lock (Gate)
        {
            var snapshot = Read(file);
            foreach (var entry in snapshot.Entries) Validate(entry);
            foreach (var e in snapshot.Entries)
            {
                var root = e.Hive == "HKEY_CURRENT_USER" ? Registry.CurrentUser : Registry.LocalMachine;
                if (e.Kind == "Missing")
                {
                    using var existing = root.OpenSubKey(e.KeyPath, writable: true);
                    existing?.DeleteValue(e.ValueName, throwOnMissingValue: false);
                    continue;
                }
                using var key = root.CreateSubKey(e.KeyPath, writable: true)
                    ?? throw new IOException($"Não foi possível abrir {e.Hive}\\{e.KeyPath}");
                var kind = Enum.Parse<RegistryValueKind>(e.Kind);
                object value = kind switch
                {
                    RegistryValueKind.DWord => e.DwordValue!.Value,
                    RegistryValueKind.QWord => e.QwordValue!.Value,
                    RegistryValueKind.Binary or RegistryValueKind.None => e.BinaryValue!,
                    RegistryValueKind.MultiString => e.MultiValue!,
                    _ => e.StringValue!
                };
                key.SetValue(e.ValueName, value, kind);
            }
            return snapshot.Entries.Count;
        }
    }

    public static int RestoreAndArchive(string file)
    {
        lock (Gate)
        {
            var count = RestoreBackup(file);
            Archive(file);
            return count;
        }
    }

    public static int RestoreMatchingBackups(string id)
    {
        ValidateId(id);
        lock (Gate)
        {
            var count = 0;
            foreach (var file in PendingBackups().Where(f => Path.GetFileName(f).StartsWith(id + "_", StringComparison.Ordinal)))
                count += RestoreAndArchive(file);
            return count;
        }
    }

    public static IReadOnlyList<string> PendingBackups()
        => Directory.GetFiles(BackupDir, "*.json")
            .OrderByDescending(GetTimestamp).ThenByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
    public static IReadOnlyList<string> ListBackups() => PendingBackups().Select(Path.GetFileName).Cast<string>().ToList();

    public static void Archive(string file)
    {
        var archive = Path.Combine(BackupDir, "restored");
        Directory.CreateDirectory(archive);
        File.Move(file, Path.Combine(archive, Path.GetFileName(file) + "." + Guid.NewGuid().ToString("N")));
    }

    private static Snapshot Read(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacy = doc.RootElement.Deserialize<List<BackupEntry>>()
                ?? throw new InvalidDataException("Backup vazio ou inválido");
            // O formato antigo armazenava QWord truncado no campo DwordValue.
            // Não é possível recuperar os bits que já foram perdidos, mas ainda
            // restauramos o valor assinado como QWord em vez de descartar o backup.
            legacy = legacy.Select(e => e.Kind == "QWord" && !e.QwordValue.HasValue && e.DwordValue.HasValue
                ? e with { QwordValue = e.DwordValue.Value }
                : e).ToList();
            return new Snapshot(1, GetLegacyTimestamp(file), legacy);
        }
        var snapshot = doc.RootElement.Deserialize<Snapshot>() ?? throw new InvalidDataException("Backup inválido");
        if (snapshot.Version != 2 || snapshot.Entries is null)
            throw new InvalidDataException("Versão de backup não suportada");
        return snapshot;
    }

    private static DateTime GetTimestamp(string file)
    {
        try { return Read(file).CreatedUtc; }
        catch { return GetLegacyTimestamp(file); }
    }

    private static DateTime GetLegacyTimestamp(string file)
    {
        var match = Regex.Match(Path.GetFileName(file), @"_(\d{8}_\d{6})(?:_|\.json)");
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var date) ? date.ToUniversalTime() : File.GetLastWriteTimeUtc(file);
    }

    private static void Validate(BackupEntry e)
    {
        if (e.Hive is not ("HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE") || string.IsNullOrWhiteSpace(e.KeyPath))
            throw new InvalidDataException("Backup contém uma chave inválida");
        var valid = e.Kind switch
        {
            "Missing" => true,
            "DWord" => e.DwordValue.HasValue,
            "QWord" => e.QwordValue.HasValue,
            "Binary" or "None" => e.BinaryValue is not null,
            "MultiString" => e.MultiValue is not null,
            "String" or "ExpandString" => e.StringValue is not null,
            _ => false
        };
        if (!valid)
            throw new InvalidDataException($"Backup de {e.KeyPath}\\{e.ValueName} ({e.Kind}) incompleto. O valor original não pode ser reconstruído com segurança.");
    }

    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9_.{}-]+\z")) throw new ArgumentException("Identificador de backup inválido", nameof(id));
    }
    public const string DefaultOwner = "registry-value";

    public static void SetDword(RegistryKey root, string keyPath, string name, int value, string owner = DefaultOwner)
        => Set(root, keyPath, name, value, RegistryValueKind.DWord, owner);

    public static int? GetDword(RegistryKey root, string keyPath, string name)
    {
        using var key = root.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(name) is int i ? i : null;
    }
    public static void SetString(RegistryKey root, string keyPath, string name, string value, string owner = DefaultOwner)
        => Set(root, keyPath, name, value, RegistryValueKind.String, owner);

    /// <summary>Grava um valor depois de salvar o original num backup nomeado pelo dono.</summary>
    public static void Set(RegistryKey root, string path, string name, object value, RegistryValueKind kind,
        string owner = DefaultOwner)
    {
        lock (Gate)
        {
            using var existing = root.OpenSubKey(path);
            var previous = existing?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (previous is not null && existing!.GetValueKind(name) == kind && Equals(previous, value)) return;
            CreateBackup(owner, [(root, path, name)]);
            using var key = root.CreateSubKey(path, writable: true) ?? throw new IOException($"Chave inacessível: {path}");
            key.SetValue(name, value, kind);
        }
    }
}
