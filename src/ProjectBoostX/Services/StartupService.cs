using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

public static class StartupService
{
    private static readonly SemaphoreSlim MutationLock = new(1, 1);
    private static readonly string[] RunKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
    ];
    private const string Wow64RunKey = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
    private static string StateDirectory => Path.Combine(AppPaths.DataDir, "startup-state");
    private static string FileBackupDirectory => Path.Combine(AppPaths.BackupDir, "startup-files");
    private sealed record DisabledEntry(string Name, string Command, string Source, string Location,
        string? BackupId, string? FileBackupName);

    public static Task<IReadOnlyList<StartupItem>> GetItemsAsync() => Task.Run<IReadOnlyList<StartupItem>>(() =>
    {
        var items = new List<StartupItem>();
        foreach (var (root, hive) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
        {
            foreach (var keyPath in RunKeys.Concat(hive == "HKLM" ? new[] { Wow64RunKey } : []))
            {
                using var key = root.OpenSubKey(keyPath);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "",
                        Source = keyPath == Wow64RunKey ? "Registro (32-bit)" : "Registro",
                        Location = $"{hive}\\{keyPath}", IsEnabled = true
                    });
            }
        }
        foreach (var directory in StartupDirectories()) items.AddRange(GetFolderItems(directory));
        foreach (var entry in ReadEntries().Where(e => e.Source.StartsWith("Registro", StringComparison.Ordinal)))
        {
            var item = ToItem(entry);
            var current = items.FirstOrDefault(i => Identity(i) == Identity(item));
            if (current is null) items.Add(item);
            else current.IsEnabled = false;
        }
        return items.OrderBy(i => i.Name).ToList();
    });

    internal static IEnumerable<StartupItem> GetFolderItems(string directory)
    {
        var items = new List<StartupItem>();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                var legacy = file.EndsWith(".boostbak", StringComparison.OrdinalIgnoreCase);
                var original = legacy ? file[..^9] : file;
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(original), Command = original,
                    Source = "Pasta Startup", Location = directory, IsEnabled = !legacy
                });
            }
        }
        foreach (var entry in ReadEntries().Where(e => e.Source == "Pasta Startup" && SamePath(e.Location, directory)))
        {
            var item = ToItem(entry);
            var current = items.FirstOrDefault(i => Identity(i) == Identity(item));
            if (current is null) items.Add(item);
            else current.IsEnabled = false;
        }
        return items;
    }

    public static Task DisableAsync(StartupItem item) => MutateAsync(() =>
    {
        if (File.Exists(StatePath(item)))
            throw new InvalidOperationException("Já existe um backup pendente para esta entrada. Reative antes de desativar novamente.");
        if (item.Source.StartsWith("Registro", StringComparison.Ordinal))
        {
            var (root, keyPath) = RegistryLocation(item);
            using var key = root.OpenSubKey(keyPath, writable: true)
                ?? throw new InvalidOperationException("A chave de inicialização não existe mais.");
            if (!key.GetValueNames().Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("A entrada de inicialização não existe mais.");
            var backupId = "startup_v2_" + Identity(item);
            RegistryBackupService.CreateBackup(backupId, [(root, keyPath, item.Name)]);
            WriteEntry(item, new(item.Name, item.Command, item.Source, item.Location, backupId, null));
            key.DeleteValue(item.Name, throwOnMissingValue: true);
        }
        else if (item.Source == "Pasta Startup")
        {
            var original = ValidateFolderItem(item);
            if (!File.Exists(original)) throw new FileNotFoundException("Arquivo de inicialização não encontrado.", original);
            Directory.CreateDirectory(FileBackupDirectory);
            var backupName = Guid.NewGuid().ToString("N") + ".backup";
            File.Copy(original, Path.Combine(FileBackupDirectory, backupName), overwrite: false);
            WriteEntry(item, new(item.Name, original, item.Source, item.Location, null, backupName));
            File.Delete(original);
        }
        else throw new NotSupportedException("Fonte de inicialização não suportada.");
    });

    public static Task EnableAsync(StartupItem item) => MutateAsync(() => Enable(item));

    public static Task RevertAllAsync() => MutateAsync(() =>
    {
        var failures = new List<Exception>();
        var items = ReadEntries().Select(ToItem).ToList();
        foreach (var directory in StartupDirectories())
            items.AddRange(GetFolderItems(directory).Where(i => !i.IsEnabled));
        foreach (var item in items.DistinctBy(Identity))
        {
            try { Enable(item); }
            catch (Exception ex) { failures.Add(new IOException($"{item.Name}: {ex.Message}", ex)); }
        }
        if (failures.Count > 0) throw new AggregateException("Algumas entradas de inicialização não puderam ser restauradas.", failures);
    });

    private static void Enable(StartupItem item)
    {
        var stateFile = StatePath(item);
        var entry = File.Exists(stateFile)
            ? JsonSerializer.Deserialize<DisabledEntry>(File.ReadAllText(stateFile))
                ?? throw new InvalidDataException("Backup de inicialização inválido.")
            : null;
        if (entry is not null && Identity(ToItem(entry)) != Identity(item))
            throw new InvalidDataException("O backup pertence a outra entrada de inicialização.");
        if (item.Source == "Pasta Startup")
        {
            var original = ValidateFolderItem(item);
            var backup = entry?.FileBackupName is { } name
                ? Path.Combine(FileBackupDirectory, ValidateBackupName(name))
                : original + ".boostbak";
            if (!File.Exists(backup)) throw new FileNotFoundException("Backup de inicialização não encontrado.", backup);
            RejectReparsePath(backup);
            if (File.Exists(original))
            {
                if (!File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(backup)))
                    throw new IOException("Já existe outro arquivo no destino. O backup foi preservado.");
            }
            else File.Copy(backup, original, overwrite: false);
            var archive = Path.Combine(AppPaths.BackupDir, "restored-startup");
            Directory.CreateDirectory(archive);
            File.Move(backup, Path.Combine(archive, Guid.NewGuid().ToString("N") + ".backup"));
        }
        else if (item.Source.StartsWith("Registro", StringComparison.Ordinal))
        {
            RegistryLocation(item);
            if (entry?.BackupId is null) throw new InvalidOperationException("Não há backup tipado para esta entrada.");
            if (RegistryBackupService.RestoreMatchingBackups(entry.BackupId) == 0)
                throw new InvalidOperationException("Nenhum valor de inicialização foi restaurado.");
        }
        else throw new NotSupportedException("Fonte de inicialização não suportada.");
        if (File.Exists(stateFile)) File.Delete(stateFile);
    }

    private static (RegistryKey Root, string Path) RegistryLocation(StartupItem item)
    {
        var split = item.Location.IndexOf('\\');
        if (split < 0) throw new InvalidDataException("Localização do registro inválida.");
        var root = item.Location[..split] switch
        {
            "HKCU" => Registry.CurrentUser, "HKLM" => Registry.LocalMachine,
            _ => throw new InvalidDataException("Hive de inicialização inválida.")
        };
        var path = item.Location[(split + 1)..];
        if (!RunKeys.Contains(path, StringComparer.OrdinalIgnoreCase) &&
            !(root == Registry.LocalMachine && string.Equals(path, Wow64RunKey, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A chave não é uma localização de inicialização suportada.");
        return (root, path);
    }

    private static string ValidateFolderItem(StartupItem item)
    {
        var path = Path.GetFullPath(item.Command);
        if (!SamePath(Path.GetDirectoryName(path)!, item.Location))
            throw new InvalidDataException("O arquivo está fora da pasta de inicialização indicada.");
        RejectReparsePath(path);
        return path;
    }
    private static void RejectReparsePath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Links e pontos de redirecionamento não são alterados.");
    }
    private static string ValidateBackupName(string name)
    {
        if (Path.GetFileName(name) != name) throw new InvalidDataException("Nome de backup inválido.");
        return name;
    }
    private static IEnumerable<string> StartupDirectories() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
    }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
    private static IEnumerable<DisabledEntry> ReadEntries()
    {
        if (!Directory.Exists(StateDirectory)) yield break;
        foreach (var file in Directory.EnumerateFiles(StateDirectory, "*.json"))
            yield return JsonSerializer.Deserialize<DisabledEntry>(File.ReadAllText(file))
                ?? throw new InvalidDataException($"Backup de inicialização inválido: {Path.GetFileName(file)}");
    }
    private static StartupItem ToItem(DisabledEntry entry) => new()
    {
        Name = entry.Name, Command = entry.Command, Source = entry.Source,
        Location = entry.Location, IsEnabled = false
    };
    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static string Identity(StartupItem item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{item.Source}\0{item.Location}\0{(item.Source == "Pasta Startup" ? item.Command : item.Name)}".ToUpperInvariant())));
    private static string StatePath(StartupItem item) => Path.Combine(StateDirectory, Identity(item) + ".json");
    private static void WriteEntry(StartupItem item, DisabledEntry entry)
    {
        Directory.CreateDirectory(StateDirectory);
        var destination = StatePath(item);
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entry));
        File.Move(temporary, destination, overwrite: false);
    }
    private static async Task MutateAsync(Action action)
    {
        await MutationLock.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(action).ConfigureAwait(false); }
        finally { MutationLock.Release(); }
    }
}
