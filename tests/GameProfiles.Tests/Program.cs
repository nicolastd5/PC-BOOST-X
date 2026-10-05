using BoostParaPc.Models;
using BoostParaPc.Services;
using Microsoft.Win32;
using System.Text.Json;

const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
const string Gpu = @"Software\Microsoft\DirectX\UserGpuPreferences";
const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\game.exe";
const string Perf = Ifeo + @"\PerfOptions";

var cases = new (string Name, Action<GameProfile> Test)[]
{
    ("Applying preserves unrelated AppCompat flags", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN WIN7RTM");
        GameProfileService.ApplyProfile(game);
        var flags = Read(Registry.CurrentUser, Layers, game.ExecutablePath)?.ToString() ?? "";
        Require(flags.Contains("RUNASADMIN") && flags.Contains("WIN7RTM"), "Existing compatibility flags were lost.");
        Require(flags.Split(' ').Count(f => f == "~") == 1, "The layer prefix was duplicated.");
    }),
    ("Applying preserves independent GPU preferences", game =>
    {
        Set(Registry.CurrentUser, Gpu, game.ExecutablePath, "AutoHDREnable=1;GpuPreference=1;SwapEffectUpgradeEnable=0;");
        GameProfileService.ApplyProfile(game);
        Equal("AutoHDREnable=1;GpuPreference=2;SwapEffectUpgradeEnable=0;", Read(Registry.CurrentUser, Gpu, game.ExecutablePath));
    }),
    ("All modified values are backed up before the first write", game =>
    {
        game.IsHighPriority = true;
        game.IsGpuPreferred = true;
        game.IsFullscreenOptDisabled = true;
        RegistryKey.BeforeWrite = (_, _) =>
        {
            var files = Directory.Exists(AppPaths.BackupDir) ? Directory.GetFiles(AppPaths.BackupDir, "*.json") : [];
            Require(files.Length > 0, "No backup existed before the registry write.");
            using var json = JsonDocument.Parse(File.ReadAllText(files[0]));
            var entries = json.RootElement.GetProperty("Entries");
            Equal(3, entries.GetArrayLength());
        };
        GameProfileService.ApplyProfile(game, false);
    }),
    ("Priority uses the IFEO performance options subkey", game =>
    {
        game.IsHighPriority = true;
        GameProfileService.ApplyProfile(game, false);
        Equal(3, Read(Registry.LocalMachine, Perf, "CpuPriorityClass"));
        Equal(null, Read(Registry.LocalMachine, Ifeo, "CpuPriorityClass"));
    }),
    ("Repeated application is idempotent and retains the original backup", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN");
        GameProfileService.ApplyProfile(game);
        var firstWrites = RegistryKey.WriteCount;
        GameProfileService.ApplyProfile(game);
        Equal(firstWrites, RegistryKey.WriteCount);
        Equal(1, Directory.GetFiles(AppPaths.BackupDir, "*.json").Length);
        GameProfileService.RevertProfile(game);
        Equal("~ RUNASADMIN", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
    }),
    ("Revert restores original values and types", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN HIGHDPIAWARE", RegistryValueKind.ExpandString);
        Set(Registry.CurrentUser, Gpu, game.ExecutablePath, "GpuPreference=1;AutoHDREnable=1;");
        Set(Registry.LocalMachine, Perf, "CpuPriorityClass", 6, RegistryValueKind.DWord);
        game.IsHighPriority = game.IsFullscreenOptDisabled = game.IsGpuPreferred = true;
        GameProfileService.ApplyProfile(game, false);
        GameProfileService.RevertProfile(game);
        Equal("~ RUNASADMIN HIGHDPIAWARE", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
        using var layers = Registry.CurrentUser.OpenSubKey(Layers);
        Equal(RegistryValueKind.ExpandString, layers!.GetValueKind(game.ExecutablePath));
        Equal("GpuPreference=1;AutoHDREnable=1;", Read(Registry.CurrentUser, Gpu, game.ExecutablePath));
        Equal(6, Read(Registry.LocalMachine, Perf, "CpuPriorityClass"));
        Require(game.IsHighDpiOverridden, "Restored DPI setting was not reflected in the model.");
    }),
    ("Revert without a backup makes no destructive guesses", game =>
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN HIGHDPIAWARE");
        var writes = RegistryKey.WriteCount;
        Throws(() => GameProfileService.RevertProfile(game));
        Equal(writes, RegistryKey.WriteCount);
        Equal("~ RUNASADMIN HIGHDPIAWARE", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
        Require(game.Status != "Revertido", "A missing backup was reported as a successful restore.");
    }),
    ("Recommendations replace stale selections", game =>
    {
        game.IsHighDpiOverridden = game.IsHighPriority = true;
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ HIGHDPIAWARE RUNASADMIN");
        Set(Registry.LocalMachine, Perf, "CpuPriorityClass", 3, RegistryValueKind.DWord);
        GameProfileService.ApplyProfile(game);
        Require(!game.IsHighDpiOverridden && !game.IsHighPriority, "Casual recommendation inherited stale enabled flags.");
        Require(!Read(Registry.CurrentUser, Layers, game.ExecutablePath)!.ToString()!.Contains("HIGHDPIAWARE"), "DPI override remained active.");
        Equal(null, Read(Registry.LocalMachine, Perf, "CpuPriorityClass"));
    }),
    ("Manual disabling only removes the managed settings", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE");
        Set(Registry.CurrentUser, Gpu, game.ExecutablePath, "GpuPreference=2;AutoHDREnable=1;");
        Set(Registry.LocalMachine, Perf, "CpuPriorityClass", 3, RegistryValueKind.DWord);
        Set(Registry.LocalMachine, Perf, "IoPriority", 2, RegistryValueKind.DWord);
        GameProfileService.ApplyProfile(game, false);
        Equal("~ RUNASADMIN", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
        Equal("AutoHDREnable=1;", Read(Registry.CurrentUser, Gpu, game.ExecutablePath));
        Equal(null, Read(Registry.LocalMachine, Perf, "CpuPriorityClass"));
        Equal(2, Read(Registry.LocalMachine, Perf, "IoPriority"));
    }),
    ("Disabled options preserve other GPU and CPU preferences", game =>
    {
        Set(Registry.CurrentUser, Gpu, game.ExecutablePath, "GpuPreference=1;AutoHDREnable=1;");
        Set(Registry.LocalMachine, Perf, "CpuPriorityClass", 6, RegistryValueKind.DWord);
        var writes = RegistryKey.WriteCount;
        GameProfileService.ApplyProfile(game, false);
        Equal("GpuPreference=1;AutoHDREnable=1;", Read(Registry.CurrentUser, Gpu, game.ExecutablePath));
        Equal(6, Read(Registry.LocalMachine, Perf, "CpuPriorityClass"));
        Equal(writes, RegistryKey.WriteCount);
    }),
    ("Revert unwinds multiple changes back to the original state", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN");
        GameProfileService.ApplyProfile(game);
        game.IsHighDpiOverridden = true;
        GameProfileService.ApplyProfile(game, false);
        Equal(2, Directory.GetFiles(AppPaths.BackupDir, "*.json").Length);
        GameProfileService.RevertProfile(game);
        Equal("~ RUNASADMIN", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
        Equal(null, Read(Registry.CurrentUser, Gpu, game.ExecutablePath));
        Equal(0, Directory.GetFiles(AppPaths.BackupDir, "*.json").Length);
    }),
    ("Games with identical filenames use separate backups", game =>
    {
        var secondDir = Path.Combine(AppPaths.DataDir, "other");
        Directory.CreateDirectory(secondDir);
        var second = new GameProfile { Name = "Other Game", ExecutablePath = Path.Combine(secondDir, "game.exe") };
        File.WriteAllText(second.ExecutablePath, "never launched");
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN");
        Set(Registry.CurrentUser, Layers, second.ExecutablePath, "~ WIN7RTM");
        GameProfileService.ApplyProfile(game);
        GameProfileService.ApplyProfile(second);
        GameProfileService.RevertProfile(game);
        Equal("~ RUNASADMIN", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
        Require(Read(Registry.CurrentUser, Layers, second.ExecutablePath)!.ToString()!.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE"),
            "Reverting one game restored the other game's backup.");
        GameProfileService.RevertProfile(second);
        Equal("~ WIN7RTM", Read(Registry.CurrentUser, Layers, second.ExecutablePath));
    }),
    ("Revert failure keeps the backup available and exposes the error", game =>
    {
        Set(Registry.CurrentUser, Layers, game.ExecutablePath, "~ RUNASADMIN");
        GameProfileService.ApplyProfile(game);
        RegistryKey.FailWritePath = Layers;
        Throws(() => GameProfileService.RevertProfile(game));
        Require(game.Status.Contains("falha", StringComparison.OrdinalIgnoreCase), "Restore failure was hidden.");
        Require(Directory.GetFiles(AppPaths.BackupDir, "*.json").Length > 0, "Failed restore consumed the only backup.");
        RegistryKey.FailWritePath = null;
        GameProfileService.RevertProfile(game);
        Equal("~ RUNASADMIN", Read(Registry.CurrentUser, Layers, game.ExecutablePath));
    }),
    ("Write failures propagate without success status", game =>
    {
        RegistryKey.FailWritePath = Gpu;
        Throws(() => GameProfileService.ApplyProfile(game));
        Require(game.Status.Contains("falha", StringComparison.OrdinalIgnoreCase) || game.Status.Contains("erro", StringComparison.OrdinalIgnoreCase),
            "The failed profile did not expose an error status.");
        Require(!game.IsFullscreenOptDisabled && !game.IsGpuPreferred, "Failure was committed to model as applied.");
    }),
    ("An unavailable backup folder prevents every write", game =>
    {
        File.WriteAllText(AppPaths.BackupDir, "blocks directory creation");
        Throws(() => GameProfileService.ApplyProfile(game));
        Equal(0, RegistryKey.WriteCount);
    }),
    ("A missing executable is an explicit failure", game =>
    {
        File.Delete(game.ExecutablePath);
        Throws(() => GameProfileService.ApplyProfile(game));
        Equal(0, RegistryKey.WriteCount);
        Require(game.Status != "—", "Missing executable failure was not exposed.");
    }),
};

var testRoot = Path.Combine(Path.GetTempPath(), "BoostGameProfileTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var failed = 0;
for (var i = 0; i < cases.Length; i++)
{
    RegistryKey.Reset();
    AppPaths.DataDir = Path.Combine(testRoot, i.ToString());
    Directory.CreateDirectory(AppPaths.DataDir);
    var exe = Path.Combine(AppPaths.DataDir, "game.exe");
    File.WriteAllText(exe, "test executable placeholder; never launched");
    var game = new GameProfile { Name = "Test Game", ExecutablePath = exe };
    try { cases[i].Test(game); Console.WriteLine($"PASS {cases[i].Name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {cases[i].Name}: {e.Message}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} passed; registry operations ran in memory only.");
Environment.ExitCode = failed == 0 ? 0 : 1;

static object? Read(RegistryKey root, string path, string name)
{
    using var key = root.OpenSubKey(path);
    return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
}

static void Set(RegistryKey root, string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
{
    using var key = root.CreateSubKey(path);
    key.SetValue(name, value, kind);
}

static void Equal(object? expected, object? actual)
{
    if (!Equals(expected, actual)) throw new Exception($"Expected '{expected ?? "<missing>"}', got '{actual ?? "<missing>"}'.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Throws(Action action)
{
    try { action(); }
    catch { return; }
    throw new Exception("Expected an exception, but the operation reported success.");
}
