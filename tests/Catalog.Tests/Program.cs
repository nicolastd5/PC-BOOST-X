using BoostParaPc.Models;
using BoostParaPc.Services;
using Microsoft.Win32;

const string K = @"Software\Fixture";

var cases = new List<(string Name, Func<Task> Run)>
{
    ("Aplicar grava e detecta; reverter restaura o valor anterior e remove o que não existia", async () =>
    {
        Seed(K, "A", 7);
        var before = RegistryKey.Dump();
        var item = Make("item.a", Dw("A", 0), Dw("B", 1));
        Equal(ApplyState.Applied, await OptimizationEngine.ApplyAsync(item));
        Require(OptimizationEngine.IsApplied(item), "Item aplicado não foi detectado.");
        Require(await OptimizationEngine.RevertAsync(item), "Reversão falhou.");
        Equal(before, RegistryKey.Dump());
        Equal(ApplyState.NotApplied, item.State);
    }),
    ("Reverter um item não desfaz outro", async () =>
    {
        var a = Make("item.a", Dw("A", 0));
        var b = Make("item.b", Dw("B", 0));
        await OptimizationEngine.ApplyAsync(a);
        await OptimizationEngine.ApplyAsync(b);
        await OptimizationEngine.RevertAsync(a);
        Require(!OptimizationEngine.IsApplied(a), "Item revertido continua detectado.");
        Require(OptimizationEngine.IsApplied(b), "Reverter um item desfez o outro.");
    }),
    ("Falha no meio desfaz o que já foi gravado", async () =>
    {
        Seed(K, "A", 7);
        var before = RegistryKey.Dump();
        var failed = false;
        RegistryKey.BeforeWrite = (_, name) =>
        {
            if (name != "B" || failed) return;
            failed = true;
            throw new UnauthorizedAccessException("Acesso negado pelo teste.");
        };
        var item = Make("item.a", Dw("A", 0), Dw("B", 1));
        Equal(ApplyState.Failed, await OptimizationEngine.ApplyAsync(item));
        Equal(before, RegistryKey.Dump());
        Require(item.StatusMessage == "Acesso negado pelo teste.", "A mensagem da falha foi perdida.");
    }),
    ("Se a reversão automática também falha, os backups ficam para nova tentativa", async () =>
    {
        Seed(K, "A", 7);
        var before = RegistryKey.Dump();
        RegistryKey.FailWritePath = @"Software\Blocked";
        var item = Make("item.a", Dw("A", 0), Dw("B", 1, @"Software\Blocked"));
        Equal(ApplyState.Failed, await OptimizationEngine.ApplyAsync(item));
        Require(RegistryBackupService.ListBackups().Count == 2, "Backup perdido após falha.");
        RegistryKey.FailWritePath = null;
        Require(await OptimizationEngine.RevertAsync(item), "Nova tentativa de reversão falhou.");
        Equal(before, RegistryKey.Dump());
    }),
    ("Pasta de backup indisponível impede qualquer gravação", async () =>
    {
        File.WriteAllText(Path.Combine(AppPaths.DataDir, "backups"), "bloqueia a criação da pasta");
        var item = Make("item.a", Dw("A", 0));
        Equal(ApplyState.Failed, await OptimizationEngine.ApplyAsync(item));
        Equal(0, RegistryKey.WriteCount);
    }),
    ("ApplyExtra recebe o id; se falhar, o Registro volta ao que era", async () =>
    {
        var before = RegistryKey.Dump();
        string? received = null;
        var item = Make("item.a", Dw("A", 0));
        item = new OptimizationItem
        {
            Id = item.Id, Name = item.Name, Description = item.Description, Category = item.Category, Risk = item.Risk,
            Registry = item.Registry,
            ApplyExtra = owner => { received = owner; throw new InvalidOperationException("extra falhou"); }
        };
        Equal(ApplyState.Failed, await OptimizationEngine.ApplyAsync(item));
        Equal("item.a", received);
        Equal(before, RegistryKey.Dump());
    }),
    ("Ação pontual executa e não fica marcada como aplicada", async () =>
    {
        var ran = false;
        var item = new OptimizationItem
        {
            Id = "tools.x", Name = "x", Description = "x", Category = OptimizationCategory.System, Risk = RiskLevel.Safe,
            IsAction = true, ApplyExtra = _ => { ran = true; return Task.CompletedTask; }
        };
        Equal(ApplyState.NotApplied, await OptimizationEngine.ApplyAsync(item));
        Require(ran, "A ação não executou.");
        Require(!OptimizationEngine.IsApplied(item), "Ação pontual ficou como aplicada.");
        Require(!OptimizationStateStore.Load().Contains("tools.x"), "Ação pontual foi persistida.");
    }),
    ("Item sem detecção usa o estado salvo; ids antigos desconhecidos são ignorados", () =>
    {
        OptimizationStateStore.Save(["item.x", "memory.pagedpool"]);
        var tracked = Make("item.x");
        var other = Make("item.y");
        OptimizationEngine.Refresh([tracked, other]);
        Equal(ApplyState.Applied, tracked.State);
        Equal(ApplyState.NotApplied, other.State);
        return Task.CompletedTask;
    }),
    ("Detecção exige todos os valores e o delegate extra", () =>
    {
        Seed(K, "A", 0);
        Require(!OptimizationEngine.IsApplied(Make("item.a", Dw("A", 0), Dw("B", 1))), "Detectou com valor faltando.");
        Seed(K, "B", 1);
        Require(OptimizationEngine.IsApplied(Make("item.a", Dw("A", 0), Dw("B", 1))), "Não detectou com todos os valores.");
        var withExtra = new OptimizationItem
        {
            Id = "item.e", Name = "e", Description = "e", Category = OptimizationCategory.System, Risk = RiskLevel.Safe,
            Registry = [Dw("A", 0)], IsAppliedExtra = () => false
        };
        Require(!OptimizationEngine.IsApplied(withExtra), "Ignorou o delegate de detecção.");
        return Task.CompletedTask;
    }),
    ("Cada operação gera uma linha no log", async () =>
    {
        var item = Make("item.a", Dw("A", 0));
        await OptimizationEngine.ApplyAsync(item);
        await OptimizationEngine.RevertAsync(item);
        var lines = File.ReadAllLines(ActionLog.FilePath);
        Equal(2, lines.Length);
        Require(lines[0].Contains("\"item.a\"") && lines[0].Contains("aplicar"), "Linha de aplicar ausente.");
        Require(lines[1].Contains("reverter"), "Linha de reverter ausente.");
    }),
    ("Regras de hardware preenchem e limpam o motivo", () =>
    {
        var item = new OptimizationItem
        {
            Id = "item.h", Name = "h", Description = "h", Category = OptimizationCategory.Power, Risk = RiskLevel.Safe,
            NotRecommended = pc => pc.IsLaptop ? "Não em notebook." : null
        };
        OptimizationEngine.ApplyHardwareRules([item], Pc(laptop: true));
        Equal("Não em notebook.", item.NotRecommendedReason);
        Require(!item.IsRecommended, "Item bloqueado aparece como recomendado.");
        OptimizationEngine.ApplyHardwareRules([item], Pc());
        Require(item.IsRecommended, "O motivo não foi limpo.");
        return Task.CompletedTask;
    }),
    ("CPU híbrida e X3D são reconhecidas", () =>
    {
        Require(Pc("13th Gen Intel(R) Core(TM) i7-13700K", 16, 24).IsHybridCpu, "i7-13700K não foi reconhecido como híbrido.");
        Require(Pc("Intel(R) Core(TM) Ultra 7 265K", 20, 20).IsHybridCpu, "Core Ultra não foi reconhecido como híbrido.");
        Require(!Pc("AMD Ryzen 5 5600", 6, 12).IsHybridCpu, "Ryzen 5 5600 foi tratado como híbrido.");
        Require(!Pc("Intel(R) Core(TM) i5-9400F", 6, 6).IsHybridCpu, "CPU sem SMT foi tratada como híbrida.");
        Require(!Pc("CPU desconhecida", 0, 12).IsHybridCpu, "Falha de leitura de núcleos virou CPU híbrida.");
        Require(Pc("AMD Ryzen 7 7800X3D 8-Core Processor", 8, 16).IsX3D, "X3D não reconhecido.");
        return Task.CompletedTask;
    }),
};

var root = Path.Combine(Path.GetTempPath(), "BoostCatalogTests", Guid.NewGuid().ToString("N"));
var failures = 0;
for (var i = 0; i < cases.Count; i++)
{
    RegistryKey.Reset();
    SystemSettingsBackupService.Reset();
    AppPaths.DataDir = Path.Combine(root, i.ToString());
    Directory.CreateDirectory(AppPaths.DataDir);
    try { await cases[i].Run(); Console.WriteLine("PASS " + cases[i].Name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + cases[i].Name + ": " + e.Message); }
}
Console.WriteLine($"{cases.Count - failures}/{cases.Count} passed; registry operations ran in memory only.");
Environment.ExitCode = failures == 0 ? 0 : 1;

static OptimizationItem Make(string id, params RegValue[] values) => new()
{
    Id = id, Name = id, Description = id, Category = OptimizationCategory.System, Risk = RiskLevel.Safe, Registry = values
};

static RegValue Dw(string name, int value, string key = K) =>
    new(Registry.CurrentUser, key, name, value, RegistryValueKind.DWord);

static void Seed(string path, string name, int value)
{
    using var key = Registry.CurrentUser.CreateSubKey(path);
    key.SetValue(name, value, RegistryValueKind.DWord);
}

static SystemInfo Pc(string cpu = "AMD Ryzen 5 5600", int cores = 6, int logical = 12, bool laptop = false,
    string disk = "SSD", bool win11 = true) =>
    new("Windows 11", "10.0.26200", cpu, cores, logical, 16, "GPU", disk, 500, 250, laptop, win11, "PC");

static void Equal(object? expected, object? actual)
{
    if (!Equals(expected, actual)) throw new Exception($"Esperado '{expected ?? "<nulo>"}', obtido '{actual ?? "<nulo>"}'.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
