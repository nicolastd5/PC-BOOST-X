using System.Diagnostics;
using System.IO;
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

public static class StartupService
{
    private static readonly string[] RunKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
    ];

    private static readonly string[] Wow64RunKeys =
    [
        @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run"
    ];

    public static async Task<IReadOnlyList<StartupItem>> GetItemsAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<StartupItem>();

            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                var root = hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
                var hiveName = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";

                foreach (var keyPath in RunKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(keyPath);
                        if (key is null) continue;
                        foreach (var name in key.GetValueNames())
                        {
                            var cmd = key.GetValue(name)?.ToString() ?? "";
                            items.Add(new StartupItem
                            {
                                Name = name,
                                Command = cmd,
                                Source = "Registro",
                                Location = $"{hiveName}\\{keyPath}",
                                IsEnabled = true
                            });
                        }
                    }
                    catch { }
                }

                if (hive == RegistryHive.LocalMachine)
                {
                    foreach (var keyPath in Wow64RunKeys)
                    {
                        try
                        {
                            using var key = root.OpenSubKey(keyPath);
                            if (key is null) continue;
                            foreach (var name in key.GetValueNames())
                            {
                                var cmd = key.GetValue(name)?.ToString() ?? "";
                                items.Add(new StartupItem
                                {
                                    Name = name,
                                    Command = cmd,
                                    Source = "Registro (32-bit)",
                                    Location = $"HKLM\\{keyPath}",
                                    IsEnabled = true
                                });
                            }
                        }
                        catch { }
                    }
                }
            }

            // Startup folder
            var startupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            if (Directory.Exists(startupDir))
            {
                foreach (var file in Directory.EnumerateFiles(startupDir))
                {
                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Source = "Pasta Startup",
                        Location = startupDir,
                        IsEnabled = true
                    });
                }
            }

            // Tasks de inicialização (via PowerShell)
            try
            {
                var ps = ProcessRunner.RunPowerShellAsync(
                    "Get-ScheduledTask | Where-Object {$_.Settings.DisallowStartIfOnBatteries -eq $false -and $_.TaskPath -notlike '\\Microsoft\\*'} | Select-Object -First 30 TaskName, TaskPath | ConvertTo-Csv -NoTypeInformation",
                    timeoutMs: 15_000).GetAwaiter().GetResult();
                // parse leve — opcional, não bloqueia UI
            }
            catch { }

            return items.OrderBy(i => i.Name).ToList();
        });
    }

    public static async Task DisableAsync(StartupItem item)
    {
        await Task.Run(() =>
        {
            if (item.Source.StartsWith("Registro"))
            {
                var hive = item.Location.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                var keyPath = item.Location[(item.Location.IndexOf('\\') + 1)..];
                using var key = hive.OpenSubKey(keyPath, writable: true);
                if (key is not null)
                {
                    var value = key.GetValue(item.Name);
                    if (value is not null)
                    {
                        // backup
                        var bakDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BoostParaPc", "backups");
                        Directory.CreateDirectory(bakDir);
                        File.WriteAllText(
                            Path.Combine(bakDir, $"startup_{item.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"),
                            $"{item.Location}|{item.Name}|{value}");
                        key.DeleteValue(item.Name, throwOnMissingValue: false);
                    }
                }
            }
            else if (item.Source == "Pasta Startup")
            {
                var bak = item.Command + ".boostbak";
                if (!File.Exists(bak))
                    File.Move(item.Command, bak);
            }
        });
    }

    public static async Task EnableAsync(StartupItem item)
    {
        // Reverte o último backup com o mesmo nome
        await Task.Run(() =>
        {
            var bakDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoostParaPc", "backups");
            if (!Directory.Exists(bakDir)) return;

            var latest = Directory.GetFiles(bakDir, $"startup_{item.Name}_*.txt")
                .OrderByDescending(File.GetCreationTimeUtc)
                .FirstOrDefault();
            if (latest is null) return;

            var parts = File.ReadAllText(latest).Split('|', 3);
            if (parts.Length != 3) return;

            var hive = parts[0].StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            var keyPath = parts[0][(parts[0].IndexOf('\\') + 1)..];
            using var key = hive.CreateSubKey(keyPath, writable: true);
            key?.SetValue(parts[1], parts[2]);
        });
    }
}
