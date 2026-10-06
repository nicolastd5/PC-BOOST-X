using BoostParaPc.Models;
using BoostParaPc.Services;
using Microsoft.Win32;
using System.Text.Json;

const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
const string Gpu = @"Software\Microsoft\DirectX\UserGpuPreferences";
const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\game.exe";
const string Perf = Ifeo + @"\PerfOptions";
const string UnrealSample = "[/Script/Engine.GameUserSettings]\r\nbUseVSync=False\r\nResolutionSizeX=1920\r\n\r\n" +
    "[ScalabilityGroups]\r\nsg.ResolutionQuality=100.000000\r\nsg.ViewDistanceQuality=3\r\nsg.ShadowQuality=3\r\n" +
    "sg.EffectsQuality=0\r\nsg.PostProcessQuality=2\r\nsg.TextureQuality=3\r\n\r\n" +
    "[ShaderPipelineCache.CacheFile]\r\nsg.ShadowQuality=3\r\nLastOpened=Game\r\n";

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
    ("Unreal: o preset leve só baixa o que está acima do alvo e preserva o resto do arquivo", _ =>
    {
        var (text, changes) = UnrealIni.ApplyLightPreset(UnrealSample);
        Equal(UnrealSample
            .Replace("sg.ResolutionQuality=100.000000", "sg.ResolutionQuality=80.000000")
            .Replace("[ScalabilityGroups]\r\nsg.ResolutionQuality=80.000000\r\nsg.ViewDistanceQuality=3\r\nsg.ShadowQuality=3",
                     "[ScalabilityGroups]\r\nsg.ResolutionQuality=80.000000\r\nsg.ViewDistanceQuality=3\r\nsg.ShadowQuality=1")
            .Replace("sg.PostProcessQuality=2", "sg.PostProcessQuality=1"), text);
        Equal("sg.ResolutionQuality,sg.ShadowQuality,sg.PostProcessQuality", string.Join(",", changes.Select(c => c.Key)));
        Equal(UnrealSample, UnrealIni.Restore(text, changes));
    }),
    ("Unreal: resolução automática, valores já baixos e arquivo sem a seção padrão não mudam", _ =>
    {
        var low = "[ScalabilityGroups]\nsg.ResolutionQuality=0\nsg.ShadowQuality=1\nsg.EffectsQuality=0\nsg.ReflectionQuality=auto\n";
        var (text, changes) = UnrealIni.ApplyLightPreset(low);
        Equal(low, text); Equal(0, changes.Count);
        var custom = "[/Script/FortniteGame.FortGameUserSettings]\nsg.ShadowQuality=3\n";
        Equal(custom, UnrealIni.ApplyLightPreset(custom).Text);
    }),
    ("Unreal: desfazer não sobrescreve o que o jogador mudou depois no jogo", _ =>
    {
        var (text, changes) = UnrealIni.ApplyLightPreset(UnrealSample);
        var edited = text.Replace("[ScalabilityGroups]\r\nsg.ResolutionQuality=80.000000\r\nsg.ViewDistanceQuality=3\r\nsg.ShadowQuality=1",
                                  "[ScalabilityGroups]\r\nsg.ResolutionQuality=80.000000\r\nsg.ViewDistanceQuality=3\r\nsg.ShadowQuality=2");
        var restored = UnrealIni.Restore(edited, changes);
        Require(restored.Contains("sg.ShadowQuality=2\r\nsg.EffectsQuality"), "A escolha feita depois pelo jogador foi sobrescrita.");
        Require(restored.Contains("sg.ResolutionQuality=100.000000") && restored.Contains("sg.PostProcessQuality=2"), "Os demais valores não voltaram.");
    }),
    ("Unreal: aplica e desfaz no arquivo do jogo, preservando a codificação", game =>
    {
        var config = UnrealLayout(game, UnrealSample, new System.Text.UnicodeEncoding(false, true));
        var original = File.ReadAllBytes(config);
        Require(UnrealBoostService.IsUnrealGame(game.ExecutablePath), "Jogo Unreal não foi reconhecido.");
        Equal(config, UnrealBoostService.FindConfig(game.ExecutablePath, game.Name));
        UnrealBoostService.Apply(game.ExecutablePath, game.Name);
        var bytes = File.ReadAllBytes(config);
        Require(bytes[0] == 0xFF && bytes[1] == 0xFE, "A codificação UTF-16 do arquivo foi perdida.");
        Require(System.Text.Encoding.Unicode.GetString(bytes).Contains("sg.ShadowQuality=1"), "O preset não foi gravado.");
        Require(UnrealBoostService.IsBoosted(game.ExecutablePath), "O jogo não ficou marcado como alterado.");
        Require(UnrealBoostService.Revert(game.ExecutablePath), "Não havia o que desfazer.");
        Require(original.SequenceEqual(File.ReadAllBytes(config)), "O arquivo não voltou a ser idêntico ao original.");
        Require(!UnrealBoostService.IsBoosted(game.ExecutablePath), "O jogo continua marcado depois de desfazer.");
    }),
    ("Unreal: aplicar duas vezes mantém os valores originais para desfazer", game =>
    {
        var config = UnrealLayout(game, UnrealSample);
        UnrealBoostService.Apply(game.ExecutablePath, game.Name);
        File.WriteAllText(config, File.ReadAllText(config).Replace("sg.EffectsQuality=0", "sg.EffectsQuality=3"));
        UnrealBoostService.Apply(game.ExecutablePath, game.Name);
        Require(File.ReadAllText(config).Contains("sg.EffectsQuality=1"), "A segunda aplicação não baixou o valor novo.");
        UnrealBoostService.Revert(game.ExecutablePath);
        var text = File.ReadAllText(config);
        Require(text.Contains("sg.ShadowQuality=3") && text.Contains("sg.ResolutionQuality=100.000000"), "Os originais da primeira aplicação foram perdidos.");
        Require(text.Contains("sg.EffectsQuality=3"), "O valor da segunda aplicação não voltou.");
    }),
    ("Unreal: jogo aberto ou arquivo somente leitura não é alterado", game =>
    {
        var config = UnrealLayout(game, UnrealSample);
        var original = File.ReadAllText(config);
        UnrealBoostService.IsRunning = (_, _) => true;
        Throws(() => UnrealBoostService.Apply(game.ExecutablePath, game.Name));
        UnrealBoostService.IsRunning = (_, _) => false;
        File.SetAttributes(config, FileAttributes.ReadOnly);
        try { Throws(() => UnrealBoostService.Apply(game.ExecutablePath, game.Name)); }
        finally { File.SetAttributes(config, FileAttributes.Normal); }
        Equal(original, File.ReadAllText(config));
        Require(!UnrealBoostService.IsBoosted(game.ExecutablePath), "Uma tentativa recusada deixou o jogo marcado.");
    }),
    ("Unreal: jogo de outro motor não é reconhecido e configuração ausente tem mensagem clara", game =>
    {
        UnrealBoostService.LocalAppData = Path.Combine(AppPaths.DataDir, "local");
        UnrealBoostService.IsRunning = (_, _) => false;
        Require(!UnrealBoostService.IsUnrealGame(game.ExecutablePath), "Jogo sem a pasta Engine foi tratado como Unreal.");
        Directory.CreateDirectory(Path.Combine(AppPaths.DataDir, "Engine", "Binaries"));
        try { UnrealBoostService.Apply(game.ExecutablePath, game.Name); throw new Exception("Aplicou sem arquivo de configuração."); }
        catch (InvalidOperationException e) { Require(e.Message.Contains("Abra o jogo"), "Mensagem não orienta o jogador: " + e.Message); }
        Require(!UnrealBoostService.Revert(game.ExecutablePath), "Desfazer sem nada aplicado relatou sucesso.");
    }),
    ("Unreal: reverter tudo desfaz todos os jogos alterados", game =>
    {
        var config = UnrealLayout(game, UnrealSample);
        UnrealBoostService.Apply(game.ExecutablePath, game.Name);
        Equal(1, UnrealBoostService.RevertAll());
        Equal(UnrealSample, File.ReadAllText(config));
        Equal(0, UnrealBoostService.RevertAll());
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

// Jogo Unreal de mentira: pasta Engine ao lado do projeto e a configuração em um %LOCALAPPDATA% próprio do teste.
static string UnrealLayout(GameProfile game, string ini, System.Text.Encoding? encoding = null)
{
    var root = Path.GetDirectoryName(game.ExecutablePath)!;
    Directory.CreateDirectory(Path.Combine(root, "Engine", "Binaries"));
    Directory.CreateDirectory(Path.Combine(root, "MyGame", "Binaries", "Win64"));
    UnrealBoostService.LocalAppData = Path.Combine(root, "local");
    UnrealBoostService.IsRunning = (_, _) => false;
    var config = Path.Combine(UnrealBoostService.LocalAppData, "MyGame", "Saved", "Config", "Windows", "GameUserSettings.ini");
    Directory.CreateDirectory(Path.GetDirectoryName(config)!);
    encoding ??= new System.Text.UTF8Encoding(false);
    File.WriteAllBytes(config, [.. encoding.GetPreamble(), .. encoding.GetBytes(ini)]);
    return config;
}

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
