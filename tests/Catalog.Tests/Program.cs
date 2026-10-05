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

string[] serviceNames = ["DiagTrack", "dmwappushservice", "SysMain", "WSearch", "Fax", "MapsBroker",
    "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc", "Spooler"];
string[] removedIds = ["memory.pagedpool", "memory.large", "memory.standby", "sys.priority.sep", "sys.flushdns",
    "services.remoteregistry", "net.tcp", "net.dns.flush", "net.throttle.off", "telemetry.cortana",
    "input.mousespeed", "game.notifications"];

void SeedWindows(OptimizationItem item, params string[] missingServices)
{
    // Metade dos valores já existe com outro conteúdo; a outra metade não existe.
    for (var i = 0; i < item.Registry.Count; i += 2)
    {
        var v = item.Registry[i];
        using var key = v.Root.CreateSubKey(v.Key);
        key.SetValue(v.Name, v.Value is int number ? (object)(number + 1) : "valor-anterior", v.Kind);
    }
    foreach (var service in serviceNames.Except(missingServices))
    {
        using var key = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
        key.SetValue("Start", 2, RegistryValueKind.DWord);
    }
    const string interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    Registry.LocalMachine.CreateSubKey(interfaces).Dispose();
    using (var nic = Registry.LocalMachine.CreateSubKey(interfaces + @"\{nic-1}"))
        nic.SetValue("DhcpIPAddress", "192.168.0.2", RegistryValueKind.String);
    using (var power = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power"))
        power.SetValue("HibernateEnabled", 1, RegistryValueKind.DWord);
}

string Snapshot() => RegistryKey.Dump() + "\n|" +
    string.Join(",", PowerNative.Values.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));

foreach (var item in OptimizationCatalog.All.Where(i => !i.IsAction))
    cases.Add(($"{item.Id}: aplicar, detectar e reverter devolve o sistema ao estado inicial", async () =>
    {
        SeedWindows(item);
        var before = Snapshot();
        Equal(ApplyState.Applied, await OptimizationEngine.ApplyAsync(item));
        Require(OptimizationEngine.IsApplied(item), "Não foi detectado como aplicado.");
        Require(await OptimizationEngine.RevertAsync(item), item.StatusMessage ?? "Reversão falhou.");
        Equal(before, Snapshot());
    }));

cases.AddRange(
[
    ("Catálogo tem 37 itens com id único e bem formado", () =>
    {
        Equal(37, OptimizationCatalog.All.Count);
        Equal(37, OptimizationCatalog.All.Select(i => i.Id).Distinct().Count());
        var bad = OptimizationCatalog.All.FirstOrDefault(i => !System.Text.RegularExpressions.Regex.IsMatch(i.Id, @"\A[a-z0-9.]+\z"));
        Require(bad is null, $"Id inválido: {bad?.Id}");
        return Task.CompletedTask;
    }),
    ("Nenhum valor de Registro é gravado por dois itens", () =>
    {
        var clash = OptimizationCatalog.All
            .SelectMany(i => i.Registry.Select(v => (i.Id, Path: $@"{v.Root.Name}\{v.Key}\{v.Name}".ToLowerInvariant())))
            .GroupBy(x => x.Path).FirstOrDefault(g => g.Count() > 1);
        Require(clash is null, $"{clash?.Key} é gravado por {string.Join(" e ", clash?.Select(x => x.Id) ?? [])}");
        return Task.CompletedTask;
    }),
    ("Todo item tem nome, descrição e impacto, e é detectável, ação ou rastreado", () =>
    {
        foreach (var i in OptimizationCatalog.All)
        {
            Require(i.Name.Length > 0 && i.Description.Length > 0 && i.Impact.Length > 0, $"{i.Id} tem texto vazio.");
            Require(i.IsAction || i.Registry.Count > 0 || i.IsAppliedExtra is not null || i.Id == "power.high",
                $"{i.Id} não tem como ser detectado.");
        }
        return Task.CompletedTask;
    }),
    ("Itens removidos não voltam", () =>
    {
        var back = OptimizationCatalog.All.FirstOrDefault(i => removedIds.Contains(i.Id));
        Require(back is null, $"{back?.Id} voltou ao catálogo.");
        return Task.CompletedTask;
    }),
    ("Presets nunca incluem avançados nem itens não recomendados", () =>
    {
        OptimizationEngine.ApplyHardwareRules(OptimizationCatalog.All, Pc(laptop: true));
        var balanced = OptimizationCatalog.Preset(RiskLevel.Moderate).ToList();
        Require(balanced.Count > 10, "Preset Equilibrado ficou vazio demais.");
        Require(balanced.All(i => i.Risk != RiskLevel.Advanced && i.IsRecommended && !i.IsAction), "Preset inclui item indevido.");
        Require(balanced.All(i => i.Id is not ("power.cpu.max" or "power.pcie.aspm" or "sys.corepark" or "power.sleep" or "sys.hibernation" or "power.high")),
            "Preset de notebook inclui ajuste de energia contraindicado.");
        Require(OptimizationCatalog.Preset(RiskLevel.Safe).All(i => i.Risk == RiskLevel.Safe), "Preset Seguro inclui risco maior.");
        OptimizationEngine.ApplyHardwareRules(OptimizationCatalog.All, Pc());
        return Task.CompletedTask;
    }),
    ("Core parking é bloqueado em notebook, CPU híbrida e X3D; SysMain em HD", () =>
    {
        var park = OptimizationCatalog.All.Single(i => i.Id == "sys.corepark");
        var sysmain = OptimizationCatalog.All.Single(i => i.Id == "services.sysmain");
        Require(park.NotRecommended!(Pc(laptop: true)) is not null, "Notebook não bloqueou core parking.");
        Require(park.NotRecommended!(Pc("13th Gen Intel(R) Core(TM) i7-13700K", 16, 24)) is not null, "Híbrida não bloqueou.");
        Require(park.NotRecommended!(Pc("AMD Ryzen 7 7800X3D 8-Core Processor", 8, 16)) is not null, "X3D não bloqueou.");
        Require(park.NotRecommended!(Pc()) is null, "Desktop comum foi bloqueado.");
        Require(sysmain.NotRecommended!(Pc(disk: "HD")) is not null, "HD não bloqueou SysMain.");
        Require(sysmain.NotRecommended!(Pc()) is null, "SSD bloqueou SysMain.");
        return Task.CompletedTask;
    }),
    ("Serviço que não existe nesta edição do Windows não gera erro", async () =>
    {
        var fax = OptimizationCatalog.All.Single(i => i.Id == "services.fax");
        SeedWindows(fax, missingServices: "Fax");
        Equal(ApplyState.Applied, await OptimizationEngine.ApplyAsync(fax));
        Require(await OptimizationEngine.RevertAsync(fax), "Reversão de serviço ausente falhou.");
    }),
    ("Limpar DNS é ação pontual e chama ipconfig", async () =>
    {
        var dns = OptimizationCatalog.All.Single(i => i.Id == "tools.dnsflush");
        Equal(ApplyState.NotApplied, await OptimizationEngine.ApplyAsync(dns));
        Require(ProcessRunner.Calls.Contains("ipconfig /flushdns"), "ipconfig não foi chamado.");
    }),
    ("Nagle sem interface de rede ativa falha com mensagem", async () =>
    {
        var nagle = OptimizationCatalog.All.Single(i => i.Id == "net.nagle");
        Equal(ApplyState.Failed, await OptimizationEngine.ApplyAsync(nagle));
        Require(nagle.StatusMessage!.Contains("interface"), "Mensagem não explica a falha.");
    }),
]);

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
