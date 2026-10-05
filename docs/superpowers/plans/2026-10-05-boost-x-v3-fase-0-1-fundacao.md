# Boost X v3 — Fases 0 e 1 (Fundação) — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer cada otimização ser descrita uma única vez, aplicável e reversível individualmente, adequada ao hardware e funcional em qualquer idioma do Windows, sem mudar ainda o visual.

**Architecture:** O `switch` de 330 linhas do catálogo, as listas de backup e o detector viram dados: cada `OptimizationItem` declara seus valores de Registro e, quando precisa, dois delegates (aplicar e detectar o que não é Registro). Um `OptimizationEngine` aplica, detecta e reverte a partir dessa declaração; os backups passam a ser nomeados pelo id do item. Leituras de energia e de disco saem de processos externos para APIs nativas.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, CommunityToolkit.Mvvm 8.3.2, System.Management. Testes: executáveis de console sem framework, que compilam o código real e substituem só o limite do sistema.

**Spec:** `docs/superpowers/specs/2026-10-05-boost-x-v3-design.md` (seções 3 e 4). As fases 2 a 4 estão em `docs/superpowers/plans/2026-10-05-boost-x-v3-roteiro-fases-2-4.md`.

## Global Constraints

- Nenhuma dependência NuGet nova. As únicas são `CommunityToolkit.Mvvm` e `System.Management`.
- Nenhum teste lê ou grava o Registro real, inicia `powercfg`/`sc`/PowerShell reais nem cria ponto de restauração. Registro em memória (`tests/Shared/RegistryFixture.cs`) e stubs de classe.
- Toda gravação persistente tira backup **antes** de gravar, e o backup leva o id do item como dono.
- Nenhum item grava um valor que já é o padrão do Windows. Itens de risco `Advanced` e itens não recomendados para o PC nunca entram em preset nem em seleção automática.
- Namespace C# continua `BoostParaPc`. Textos de interface em pt-BR.
- Ids de item: só `a-z`, `0-9` e ponto.
- Trabalho na branch `feature/v3`. Mensagens de commit em português, no estilo do repositório (`fix:`, `feat:`, `chore:`), terminando com a linha `Co-Authored-By` do agente.
- `tests/CleanupStartup.Tests/fixtures` é mantido de propósito (o runner não apaga recursivamente por segurança). Não mexer.

## Review Focus

Entradas que a spec implica e que mais provavelmente atingem quem baixar o programa:

1. **Windows em idioma diferente do inglês** — os ajustes de energia devem funcionar. Hoje falham em pt-BR. Teste na Tarefa 2.
2. **Ajuste que não existe naquela máquina** (serviço Fax ausente, ajuste de energia oculto) — o item não pode estourar erro; serviço ausente é "nada a fazer". Testes nas Tarefas 2 e 5.
3. **Falha no meio de uma aplicação** (chave protegida, antivírus bloqueando) — o que já foi gravado é desfeito; se nem isso for possível, os backups ficam para nova tentativa. Testes na Tarefa 4.
4. **Dados da v2.1** (`applied.json` com ids removidos, backups sem dono) — o programa abre e "Reverter tudo" continua restaurando. Testes nas Tarefas 3 e 4.
5. **Pasta de dados sem permissão de escrita ou disco cheio** — nenhuma alteração é feita no Windows. Teste na Tarefa 4.

## Mapa de arquivos

| Arquivo | Ação | Responsabilidade |
|---|---|---|
| `src/ProjectBoostX/Services/Native/PowerNative.cs` | criar | Ler plano ativo, nome e índices AC/DC via `powrprof.dll` |
| `src/ProjectBoostX/Services/SystemSettingsBackupService.cs` | modificar | Leitura nativa; `Owner` no journal; `RevertAsync(owner)` |
| `src/ProjectBoostX/Services/PowerPlanService.cs` | modificar | Repassar `owner` |
| `src/ProjectBoostX/Services/RegistryBackupService.cs` | modificar | `Set(..., owner)` |
| `src/ProjectBoostX/Models/OptimizationItem.cs` | modificar | `RegValue` e campos declarativos |
| `src/ProjectBoostX/Models/SystemInfo.cs` | modificar | `IsX3D`, `IsHybridCpu` |
| `src/ProjectBoostX/Services/OptimizationEngine.cs` | criar | Aplicar, detectar, reverter, regras de hardware |
| `src/ProjectBoostX/Services/ActionLog.cs` | criar | Log JSONL de ações |
| `src/ProjectBoostX/Services/Catalog/*.cs` | criar | Catálogo declarativo (6 arquivos) |
| `src/ProjectBoostX/Services/OptimizationCatalog.cs` | apagar | Substituído por `Catalog/` |
| `src/ProjectBoostX/Services/OptimizationStateDetector.cs` | apagar | Substituído pelo motor |
| `src/ProjectBoostX/Services/GameOptimizationService.cs` | apagar | Chaves migradas para o catálogo |
| `src/ProjectBoostX/Services/SystemInfoService.cs` | modificar | Tipo de disco correto; sem `powercfg` |
| `src/ProjectBoostX/ViewModels/FeatureViewModels.cs` | modificar | Dashboard e Gaming usam o motor e as instâncias compartilhadas |
| `src/ProjectBoostX/ViewModels/MainViewModel.Hardware.cs` | criar | Aplicar regras de hardware quando `SystemInfo` chega |
| `src/ProjectBoostX/MainWindow.xaml` | modificar | Todas as categorias, botões por item |
| `tests/Shared/RegistryFixture.cs` | criar | Registro em memória, extraído de `GameProfiles.Tests/TestDependencies.cs` |
| `tests/Catalog.Tests/*` | criar | Testes do motor e do catálogo |

---

### Task 1: Base — branch, commit do trabalho pendente e .NET 10

**Files:**
- Modify: `src/ProjectBoostX/ProjectBoostX.csproj`
- Modify: `tests/Backup.Tests/Backup.Tests.csproj`, `tests/CleanupStartup.Tests/CleanupStartup.Tests.csproj`, `tests/Commands.Tests/Commands.Tests.csproj`, `tests/GameProfiles.Tests/GameProfiles.Tests.csproj`, `tests/SystemSettings.Tests/SystemSettings.Tests.csproj`

**Interfaces:**
- Consumes: nada.
- Produces: branch `feature/v3`; todos os projetos em .NET 10; comando único de verificação usado por todas as tarefas seguintes.

- [ ] **Step 1: Criar a branch e conferir o que vai entrar no commit**

```bash
git switch -c feature/v3
git status --short
```

Expected: 19 arquivos `M` em `src/`, `nuget.config`, e `??` para `docs/superpowers/`, `src/ProjectBoostX/Services/SystemSettingsBackupService.cs` e `tests/`. Nada de `bin/`, `obj/`, `publish/` nem `tests/CleanupStartup.Tests/fixtures/` (estão no `.gitignore`).

- [ ] **Step 2: Commit do trabalho de confiabilidade que está sem commit**

```bash
git add -A
git commit -m "fix: backups sem perda, reversão ordenada, limpeza e perfis seguros + testes de regressão"
```

- [ ] **Step 3: Instalar o SDK do .NET 10**

```bash
winget install Microsoft.DotNet.SDK.10
```

Depois, em um terminal novo:

```bash
dotnet --list-sdks
```

Expected: uma linha `10.0.x`.

- [ ] **Step 4: Trocar o framework em todos os projetos**

Em `src/ProjectBoostX/ProjectBoostX.csproj`, `tests/CleanupStartup.Tests/CleanupStartup.Tests.csproj` e `tests/Commands.Tests/Commands.Tests.csproj`: `net8.0-windows` → `net10.0-windows`.

Em `tests/Backup.Tests/Backup.Tests.csproj`, `tests/GameProfiles.Tests/GameProfiles.Tests.csproj` e `tests/SystemSettings.Tests/SystemSettings.Tests.csproj`: `net8.0` → `net10.0`.

Em `src/ProjectBoostX/ProjectBoostX.csproj`, também:

```xml
<PackageReference Include="System.Management" Version="10.0.0" />
```

- [ ] **Step 5: Verificar build e os 5 conjuntos de testes**

```bash
dotnet build ProjectBoostX.sln -c Release
dotnet run --project tests/Backup.Tests -c Release
dotnet run --project tests/CleanupStartup.Tests -c Release
dotnet run --project tests/GameProfiles.Tests -c Release
dotnet run --project tests/Commands.Tests -c Release
dotnet run --project tests/SystemSettings.Tests -c Release
```

Expected: build com 0 erros; `7/7`, `Failures: 0`, `16/16`, `14/14`, `16/16`. Se o build acusar avisos novos de nulabilidade do C# 14, corrigir o código apontado; não suprimir.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: migra para .NET 10"
```

---

### Task 2: Leitura de energia nativa (corrige Windows em português e ajustes ocultos)

Hoje `SystemSettingsBackupService.QueryPowerValueAsync` procura o texto `Current AC` na saída do `powercfg /query`. Em Windows pt-BR a linha é `Índice de Configurações de Correntes Alternadas Atuais: 0x…`, então **todo** ajuste de valor de energia falha com "powercfg não retornou índices AC/DC válidos". Além disso, `/query` não imprime ajustes ocultos, como o de core parking.

**Files:**
- Create: `src/ProjectBoostX/Services/Native/PowerNative.cs`
- Modify: `src/ProjectBoostX/Services/SystemSettingsBackupService.cs` (método `QueryPowerValueAsync`, perto do fim do arquivo)
- Modify: `tests/SystemSettings.Tests/FakeProcessRunner.cs`
- Test: `tests/SystemSettings.Tests/Program.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `PowerNative.ActiveScheme() : Guid?`, `PowerNative.ActiveSchemeName() : string`, `PowerNative.ReadAc(string subgroup, string setting, string? plan = null) : uint?`, `PowerNative.ReadDc(string subgroup, string setting, string? plan = null) : uint?`. `subgroup` e `setting` aceitam apelido (`sub_sleep`) ou GUID em texto.

- [ ] **Step 1: Fazer o processo falso responder em português e escrever o teste que falha**

Em `tests/SystemSettings.Tests/FakeProcessRunner.cs`, adicionar a propriedade junto das outras:

```csharp
    public static bool LocalizedPowerQuery { get; set; }
```

No método `Reset()`, acrescentar ao fim da linha que zera os sinalizadores:

```csharp
        LocalizedPowerQuery = false;
```

No ramo `/query`, trocar a linha `return Result($"Maximum: …")` por:

```csharp
                return Result(LocalizedPowerQuery
                    ? $"Índice de Configurações de Correntes Alternadas Atuais: 0x{v.Ac:x8}\nÍndice de Configurações de Correntes Contínuas Atuais: 0x{v.Dc:x8}"
                    : $"Maximum: 0xffffffff\nCurrent AC: 0x{v.Ac:x8}\nCurrent DC: 0x{v.Dc:x8}");
```

Em `tests/SystemSettings.Tests/Program.cs`, adicionar ao array `cases` (antes do último item):

```csharp
    ("Localized powercfg text does not block power changes", async () =>
    {
        ProcessRunner.LocalizedPowerQuery = true;
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true);
        Equal((0u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        await Call("RevertAllAsync");
        Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
    }),
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet run --project tests/SystemSettings.Tests -c Release`
Expected: `FAIL Localized powercfg text does not block power changes: powercfg não retornou índices AC/DC válidos.` e `16/17 passed`.

- [ ] **Step 3: Criar o leitor nativo**

`src/ProjectBoostX/Services/Native/PowerNative.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace BoostParaPc.Services;

/// <summary>
/// Leitura de planos de energia pela API do Windows: não inicia processos,
/// não depende do idioma do sistema e enxerga ajustes ocultos.
/// </summary>
public static class PowerNative
{
    private static readonly Dictionary<string, Guid> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sub_sleep"] = new("238c9fa8-0aad-41ed-83f4-97be242c8f20"),
        ["standbyidle"] = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da"),
        ["hibernateidle"] = new("9d7815a6-7ee4-497e-8888-515a05f02364"),
        ["sub_video"] = new("7516b95f-f776-4464-8c53-06167f40cc99"),
        ["videoidle"] = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"),
        ["sub_processor"] = new("54533251-82be-4824-96c1-47b60b740d00"),
        ["PROCTHROTTLEMAX"] = new("bc5038f7-23e0-4960-96da-33abaf5935ec"),
        ["PROCTHROTTLEMIN"] = new("893dee8e-2bef-41e0-89c6-b55d0929964c"),
    };

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootKey, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootKey, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootKey, in Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static string ActiveSchemeName()
    {
        if (ActiveScheme() is not { } scheme) return "Desconhecido";
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return "Desconhecido";
        var buffer = new byte[size];
        return PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0')
            : "Desconhecido";
    }

    public static uint? ReadAc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting, plan, ac: true);

    public static uint? ReadDc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting, plan, ac: false);

    private static uint? Read(string subgroup, string setting, string? plan, bool ac)
    {
        Guid? scheme = plan is null ? ActiveScheme() : Guid.TryParse(plan, out var parsed) ? parsed : null;
        if (scheme is not { } s || !TryGuid(subgroup, out var sub) || !TryGuid(setting, out var set)) return null;
        uint value;
        var status = ac
            ? PowerReadACValueIndex(IntPtr.Zero, s, sub, set, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, s, sub, set, out value);
        return status == 0 ? value : null;
    }

    private static bool TryGuid(string token, out Guid guid) =>
        Aliases.TryGetValue(token, out guid) || Guid.TryParse(token, out guid);
}
```

Os oito GUIDs foram conferidos com `powercfg /aliases` nesta máquina.

- [ ] **Step 4: Usar o leitor nativo no journal**

Em `src/ProjectBoostX/Services/SystemSettingsBackupService.cs`, substituir o método `QueryPowerValueAsync` inteiro por:

```csharp
    private static Task<(uint Ac, uint Dc)> QueryPowerValueAsync(string plan, string subgroup, string setting, CancellationToken ct)
    {
        // Leitura nativa: o texto do powercfg é traduzido pelo Windows e omite ajustes ocultos.
        var ac = PowerNative.ReadAc(subgroup, setting, plan);
        var dc = PowerNative.ReadDc(subgroup, setting, plan);
        if (ac is null || dc is null)
            throw new InvalidDataException("Este ajuste de energia não existe ou não pôde ser lido neste PC.");
        return Task.FromResult((ac.Value, dc.Value));
    }
```

- [ ] **Step 5: Dar ao teste o seu próprio `PowerNative`**

O projeto de teste não compila `Native/PowerNative.cs`. Ao fim de `tests/SystemSettings.Tests/FakeProcessRunner.cs`, adicionar:

```csharp
// Substitui a leitura nativa: os valores vêm do mesmo estado em memória do powercfg falso.
public static class PowerNative
{
    public static uint? ReadAc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting)?.Ac;
    public static uint? ReadDc(string subgroup, string setting, string? plan = null) => Read(subgroup, setting)?.Dc;

    private static (uint Ac, uint Dc)? Read(string subgroup, string setting) =>
        !ProcessRunner.InvalidPowerQuery && ProcessRunner.Values.TryGetValue(subgroup + " " + setting, out var value)
            ? value
            : null;
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet run --project tests/SystemSettings.Tests -c Release`
Expected: `17/17 passed`. O caso "Unparseable power state refuses mutation" continua passando: com `InvalidPowerQuery`, a leitura devolve `null` e nenhuma alteração é feita.

- [ ] **Step 7: Build do app e commit**

```bash
dotnet build ProjectBoostX.sln -c Release
git add -A
git commit -m "fix: lê energia pela API nativa (funciona em qualquer idioma e com ajustes ocultos)"
```

---

### Task 3: Backups com dono

**Files:**
- Modify: `src/ProjectBoostX/Services/RegistryBackupService.cs:184-208`
- Modify: `src/ProjectBoostX/Services/SystemSettingsBackupService.cs`
- Modify: `src/ProjectBoostX/Services/PowerPlanService.cs`
- Test: `tests/Backup.Tests/Program.cs`, `tests/SystemSettings.Tests/Program.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `RegistryBackupService.DefaultOwner` (`"registry-value"`)
  - `RegistryBackupService.Set(RegistryKey root, string keyPath, string name, object value, RegistryValueKind kind, string owner = DefaultOwner)`
  - `RegistryBackupService.SetDword(..., int value, string owner = DefaultOwner)`, `SetString(..., string value, string owner = DefaultOwner)`
  - `RegistryBackupService.RestoreMatchingBackups(string owner) : int` (já existe)
  - `SystemSettingsBackupService.SetPowerValueAsync(string subgroup, string setting, uint value, bool ac = true, string? owner = null)`
  - `SystemSettingsBackupService.ActivatePowerPlanAsync(string requestedPlan, bool duplicate = false, string? owner = null)`
  - `SystemSettingsBackupService.DisableServiceAsync(string name, string? owner = null)`
  - `SystemSettingsBackupService.SetHibernationAsync(bool enabled, string? owner = null)`
  - `SystemSettingsBackupService.SetTcpAutoTuningAsync(string level, string? owner = null)`
  - `SystemSettingsBackupService.DisableTelemetryTasksAsync(string? owner = null)`
  - `SystemSettingsBackupService.RevertAsync(string? owner)`; `RevertAllAsync()` continua existindo
  - `PowerPlanService.ActivateHighPerformanceAsync(string? owner = null) : Task<bool>`, `DisableSleepTimeoutsAsync(string? owner = null)`

- [ ] **Step 1: Teste que falha — Registro**

Em `tests/Backup.Tests/Program.cs`, antes da linha `Console.WriteLine($"{7 - failed}/7 …")`:

```csharp
await Test("owner-scoped-restore", () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    key.SetValue("A", 1, RegistryValueKind.DWord);
    key.SetValue("B", 1, RegistryValueKind.DWord);
    RegistryBackupService.SetDword(Registry.CurrentUser, "fixture", "A", 0, "item.a");
    RegistryBackupService.SetDword(Registry.CurrentUser, "fixture", "B", 0, "item.b");
    RegistryBackupService.RestoreMatchingBackups("item.a");
    Assert(Equals(1, key.GetValue("A")), "Owner backup was not restored");
    Assert(Equals(0, key.GetValue("B")), "Restoring one owner reverted another owner's change");
    Assert(RegistryBackupService.ListBackups().Count == 1, "Another owner's backup was consumed");
    return Task.CompletedTask;
});
```

E trocar a linha final para `Console.WriteLine($"{8 - failed}/8 regression tests passed");`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet run --project tests/Backup.Tests -c Release`
Expected: erro de compilação `CS1501` (nenhuma sobrecarga de `SetDword` aceita 5 argumentos).

- [ ] **Step 3: Implementar o dono no Registro**

Em `src/ProjectBoostX/Services/RegistryBackupService.cs`, substituir `SetDword`, `SetString` e `SetWithBackup` (linhas 184–208; `GetDword` fica como está) por:

```csharp
    public const string DefaultOwner = "registry-value";

    public static void SetDword(RegistryKey root, string keyPath, string name, int value, string owner = DefaultOwner)
        => Set(root, keyPath, name, value, RegistryValueKind.DWord, owner);

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
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet run --project tests/Backup.Tests -c Release`
Expected: `8/8 regression tests passed`.

- [ ] **Step 5: Teste que falha — journal**

Em `tests/SystemSettings.Tests/Program.cs`, substituir a função `Call` por uma que completa parâmetros opcionais:

```csharp
static Task Call(string name, params object[] args)
{
    var type = typeof(PowerPlanService).Assembly.GetType("BoostParaPc.Services.SystemSettingsBackupService")
        ?? throw new Exception("System settings journal has not been implemented.");
    var method = type.GetMethod(name) ?? throw new Exception($"Method {name} is missing.");
    var full = args.Concat(Enumerable.Repeat(Type.Missing, method.GetParameters().Length - args.Length)).ToArray();
    try { return (Task)method.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod, null, full, null)!; }
    catch (TargetInvocationException error) { throw error.InnerException!; }
}
```

E adicionar ao array `cases`:

```csharp
    ("Reverting one owner leaves other owners applied", async () =>
    {
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true, "item.a");
        await Call("SetPowerValueAsync", "sub_video", "videoidle", 0u, true, "item.b");
        await Call("RevertAsync", "item.a");
        Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        Equal((0u, 300u), ProcessRunner.Values["sub_video videoidle"]);
        Equal(1, Pending().Length);
        await Call("RevertAllAsync");
        Equal((900u, 300u), ProcessRunner.Values["sub_video videoidle"]);
    }),
```

Run: `dotnet run --project tests/SystemSettings.Tests -c Release`
Expected: `FAIL Reverting one owner leaves other owners applied: Method RevertAsync is missing.` (ou `Parameter count mismatch`), `17/18 passed`.

- [ ] **Step 6: Implementar o dono no journal**

Em `src/ProjectBoostX/Services/SystemSettingsBackupService.cs`:

1. No record `Journal`, acrescentar o último parâmetro:

```csharp
    private sealed record Journal(string Version, DateTime CreatedUtc, string Kind,
        PowerValueState? PowerValue = null, PlanState? Plan = null, ServiceState? Service = null,
        HibernationState? Hibernation = null, TcpState? Tcp = null, TasksState? Tasks = null,
        string? Owner = null);
```

2. Acrescentar `string? owner = null` como último parâmetro de `SetPowerValueAsync`, `ActivatePowerPlanAsync`, `DisableServiceAsync`, `SetHibernationAsync`, `SetTcpAutoTuningAsync` e `DisableTelemetryTasksAsync`.

3. Em **cada** `new Journal(JournalVersion, DateTime.UtcNow, "…", …)` desses seis métodos (são sete chamadas; `ActivatePowerPlanAsync` tem duas), acrescentar o argumento nomeado `Owner: owner`. Exemplo:

```csharp
        var journal = WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "PowerValue",
            PowerValue: new(plan, subgroup, setting, state.Ac, state.Dc), Owner: owner));
```

4. Substituir `RevertAllAsync` por:

```csharp
    public static Task RevertAllAsync() => RevertAsync(null);

    /// <summary>Reverte os journals pendentes do dono informado; <c>null</c> reverte todos.</summary>
    public static async Task RevertAsync(string? owner)
    {
        var cancellationToken = CancellationToken.None;
        lock (Gate)
        {
            Directory.CreateDirectory(JournalDir);
        }
        foreach (var file in GetPendingJournals())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journal = ReadJournal(file);
            if (owner is not null && journal.Owner != owner) continue;
            await RestoreJournalAsync(journal, cancellationToken).ConfigureAwait(false);
            ArchiveJournal(file);
        }
    }
```

Em `src/ProjectBoostX/Services/PowerPlanService.cs`, trocar os dois métodos:

```csharp
    public static async Task<bool> ActivateHighPerformanceAsync(string? owner = null)
    {
        try
        {
            var list = await ProcessRunner.RunCheckedAsync("powercfg.exe", "/list", 8_000).ConfigureAwait(false);
            var target = list.StdOut.Contains(HighPerformance, StringComparison.OrdinalIgnoreCase) ? HighPerformance
                : list.StdOut.Contains(UltimatePerformance, StringComparison.OrdinalIgnoreCase) ? UltimatePerformance : null;
            await SystemSettingsBackupService.ActivatePowerPlanAsync(target ?? UltimatePerformance, duplicate: target is null, owner: owner).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
```

```csharp
    public static async Task DisableSleepTimeoutsAsync(string? owner = null)
    {
        await SystemSettingsBackupService.SetPowerValueAsync("sub_sleep", "standbyidle", 0, owner: owner).ConfigureAwait(false);
        await SystemSettingsBackupService.SetPowerValueAsync("sub_sleep", "hibernateidle", 0, owner: owner).ConfigureAwait(false);
        await SystemSettingsBackupService.SetPowerValueAsync("sub_video", "videoidle", 0, owner: owner).ConfigureAwait(false);
    }
```

- [ ] **Step 7: Rodar tudo e commit**

```bash
dotnet run --project tests/SystemSettings.Tests -c Release
dotnet run --project tests/Backup.Tests -c Release
dotnet build ProjectBoostX.sln -c Release
```

Expected: `18/18 passed`, `8/8`, build sem erros.

```bash
git add -A
git commit -m "feat: backups e journal com dono, reversão por item"
```

---

### Task 4: Modelo declarativo, motor e log

**Files:**
- Modify: `src/ProjectBoostX/Models/OptimizationItem.cs`
- Modify: `src/ProjectBoostX/Models/SystemInfo.cs`
- Create: `src/ProjectBoostX/Services/OptimizationEngine.cs`
- Create: `src/ProjectBoostX/Services/ActionLog.cs`
- Create: `tests/Shared/RegistryFixture.cs`
- Modify: `tests/GameProfiles.Tests/TestDependencies.cs`, `tests/GameProfiles.Tests/GameProfiles.Tests.csproj`, `tests/SystemSettings.Tests/SystemSettings.Tests.csproj`
- Create: `tests/Catalog.Tests/Catalog.Tests.csproj`, `tests/Catalog.Tests/Stubs.cs`, `tests/Catalog.Tests/Program.cs`

**Interfaces:**
- Consumes: `RegistryBackupService.Set(...)`, `RegistryBackupService.RestoreMatchingBackups(string)`, `SystemSettingsBackupService.RevertAsync(string?)` (Task 3); `OptimizationStateStore.Load/MarkApplied/Clear` (existente).
- Produces:
  - `record RegValue(RegistryKey Root, string Key, string Name, object Value, RegistryValueKind Kind)`
  - Em `OptimizationItem`: `Caution`, `Registry`, `ApplyExtra` (`Func<string, Task>?`, recebe o id), `IsAppliedExtra` (`Func<bool>?`), `NotRecommended` (`Func<SystemInfo, string?>?`), `RequiresRestart`, `IsAction`, `NotRecommendedReason` (observável), `IsRecommended`
  - `SystemInfo.IsX3D`, `SystemInfo.IsHybridCpu`
  - `OptimizationEngine.ApplyAsync(OptimizationItem) : Task<ApplyState>` (nunca lança), `RevertAsync(OptimizationItem) : Task<bool>` (nunca lança), `IsApplied(OptimizationItem) : bool`, `Refresh(IEnumerable<OptimizationItem>)`, `ApplyHardwareRules(IEnumerable<OptimizationItem>, SystemInfo)`
  - `ActionLog.Write(string itemId, string action, bool ok, string? message = null)`, `ActionLog.FilePath`
  - `Microsoft.Win32.RegistryKey.Dump() : string` no Registro em memória dos testes

- [ ] **Step 1: Extrair o Registro em memória para um arquivo compartilhado**

Criar `tests/Shared/RegistryFixture.cs` com o bloco `namespace Microsoft.Win32 { … }` **movido** de `tests/GameProfiles.Tests/TestDependencies.cs` (as classes `Registry` e `RegistryKey`, sem alteração), mais este método dentro de `RegistryKey`:

```csharp
        /// <summary>Todos os valores existentes, em ordem estável, para comparar antes e depois.</summary>
        public static string Dump() => string.Join("\n", Keys
            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(k => k.Value.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .Select(v => $"{k.Key}\\{v.Key}={v.Value.Kind}:{v.Value.Value}")));
```

O comentário de duas linhas do topo de `TestDependencies.cs` vai junto. Em `TestDependencies.cs` ficam só os namespaces `BoostParaPc.Services` (`AppPaths`) e `BoostParaPc.Models` (`GameProfile`).

Em `tests/GameProfiles.Tests/GameProfiles.Tests.csproj` e em `tests/SystemSettings.Tests/SystemSettings.Tests.csproj`, adicionar ao `ItemGroup`:

```xml
    <Compile Include="../Shared/RegistryFixture.cs" Link="RegistryFixture.cs" />
```

Em `SystemSettings.Tests.csproj`, a linha existente que inclui `../GameProfiles.Tests/TestDependencies.cs` passa a ter `Link="TestDependencies.cs"` (o nome `RegistryFixture.cs` agora é do arquivo novo).

Run:

```bash
dotnet run --project tests/GameProfiles.Tests -c Release
dotnet run --project tests/SystemSettings.Tests -c Release
```

Expected: `16/16` e `18/18`, como antes.

- [ ] **Step 2: Criar o projeto de teste e os stubs**

`tests/Catalog.Tests/Catalog.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>$(NoWarn);CS0436;CA1416</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="../Shared/RegistryFixture.cs" Link="RegistryFixture.cs" />
    <Compile Include="../../src/ProjectBoostX/Models/Enums.cs" Link="Production/Enums.cs" />
    <Compile Include="../../src/ProjectBoostX/Models/OptimizationItem.cs" Link="Production/OptimizationItem.cs" />
    <Compile Include="../../src/ProjectBoostX/Models/SystemInfo.cs" Link="Production/SystemInfo.cs" />
    <Compile Include="../../src/ProjectBoostX/Services/RegistryBackupService.cs" Link="Production/RegistryBackupService.cs" />
    <Compile Include="../../src/ProjectBoostX/Services/OptimizationStateStore.cs" Link="Production/OptimizationStateStore.cs" />
    <Compile Include="../../src/ProjectBoostX/Services/OptimizationEngine.cs" Link="Production/OptimizationEngine.cs" />
    <Compile Include="../../src/ProjectBoostX/Services/ActionLog.cs" Link="Production/ActionLog.cs" />
  </ItemGroup>
</Project>
```

`tests/Catalog.Tests/Stubs.cs`:

```csharp
// O motor e o catálogo reais são compilados neste projeto. Só o que toca o Windows
// (pastas do app, comandos, energia e serviços) é substituído por estado em memória.
using Microsoft.Win32;

namespace BoostParaPc.Services;

public static class AppPaths
{
    public static string DataDir { get; set; } = "";
    public static string BackupDir
    {
        get { var dir = Path.Combine(DataDir, "backups"); Directory.CreateDirectory(dir); return dir; }
    }
    public static string AppliedFile => Path.Combine(DataDir, "applied.json");
}

public static class ProcessRunner
{
    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
    public static List<string> Calls { get; } = [];

    public static Task<CommandResult> RunCheckedAsync(string fileName, string arguments, int timeoutMs = 30_000,
        bool elevated = false, CancellationToken cancellationToken = default)
    {
        Calls.Add(fileName + " " + arguments);
        return Task.FromResult(new CommandResult(0, "", "", false));
    }
}

public static class PowerNative
{
    public static Dictionary<string, uint> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static uint? ReadAc(string subgroup, string setting, string? plan = null) =>
        Values.TryGetValue(subgroup + " " + setting, out var value) ? value : null;
}

public static class PowerPlanService
{
    public static Task<bool> ActivateHighPerformanceAsync(string? owner = null) => Task.FromResult(true);
}

/// <summary>Journal em memória: cada alteração guarda como desfazê-la e quem é o dono.</summary>
public static class SystemSettingsBackupService
{
    private static readonly List<(string? Owner, Action Undo)> Journal = [];

    public static void Reset()
    {
        Journal.Clear();
        PowerNative.Values.Clear();
        ProcessRunner.Calls.Clear();
    }

    public static Task SetPowerValueAsync(string subgroup, string setting, uint value, bool ac = true, string? owner = null)
    {
        var key = subgroup + " " + setting;
        var existed = PowerNative.Values.TryGetValue(key, out var previous);
        Journal.Add((owner, () => { if (existed) PowerNative.Values[key] = previous; else PowerNative.Values.Remove(key); }));
        PowerNative.Values[key] = value;
        return Task.CompletedTask;
    }

    public static Task DisableServiceAsync(string name, string? owner = null) =>
        SetDword($@"SYSTEM\CurrentControlSet\Services\{name}", "Start", 4, owner);

    public static Task SetHibernationAsync(bool enabled, string? owner = null) =>
        SetDword(@"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", enabled ? 1 : 0, owner);

    public static Task DisableTelemetryTasksAsync(string? owner = null) => Task.CompletedTask;

    public static Task RevertAsync(string? owner)
    {
        for (var i = Journal.Count - 1; i >= 0; i--)
        {
            if (owner is not null && Journal[i].Owner != owner) continue;
            Journal[i].Undo();
            Journal.RemoveAt(i);
        }
        return Task.CompletedTask;
    }

    private static Task SetDword(string path, string name, int value, string? owner)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path);
        var previous = (int)key.GetValue(name)!;
        Journal.Add((owner, () =>
        {
            using var restore = Registry.LocalMachine.CreateSubKey(path);
            restore.SetValue(name, previous, RegistryValueKind.DWord);
        }));
        key.SetValue(name, value, RegistryValueKind.DWord);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Escrever os testes do motor (falham)**

`tests/Catalog.Tests/Program.cs`:

```csharp
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
```

Run: `dotnet run --project tests/Catalog.Tests -c Release`
Expected: erro de compilação (`OptimizationEngine`, `ActionLog`, `RegValue` não existem).

- [ ] **Step 4: Estender o modelo**

Em `src/ProjectBoostX/Models/OptimizationItem.cs`: adicionar `using Microsoft.Win32;` no topo; antes da classe, o record; dentro da classe, os campos novos; e trocar `_isSelected = true` por `_isSelected` (a seleção passa a ser decidida pelas telas) e `IsReversible` pela versão derivada:

```csharp
/// <summary>Um valor de Registro que o item grava. Serve para aplicar, detectar e reverter.</summary>
public sealed record RegValue(RegistryKey Root, string Key, string Name, object Value, RegistryValueKind Kind);
```

```csharp
    /// <summary>Quando não usar. Vazio se não houver ressalva.</summary>
    public string Caution { get; init; } = string.Empty;

    public IReadOnlyList<RegValue> Registry { get; init; } = [];

    /// <summary>Alterações que não são Registro (energia, serviços). Recebe o id, que é o dono dos backups.</summary>
    public Func<string, Task>? ApplyExtra { get; init; }

    /// <summary>Detecção do que <see cref="ApplyExtra"/> altera.</summary>
    public Func<bool>? IsAppliedExtra { get; init; }

    /// <summary>Devolve o motivo quando o item é contraindicado para o PC; senão <c>null</c>.</summary>
    public Func<SystemInfo, string?>? NotRecommended { get; init; }

    public bool RequiresRestart { get; init; }

    /// <summary>Ação pontual (ex.: limpar DNS): não tem estado nem reversão.</summary>
    public bool IsAction { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommended))]
    private string? _notRecommendedReason;

    public bool IsRecommended => NotRecommendedReason is null;

    public bool IsReversible => !IsAction;
```

Em `src/ProjectBoostX/Models/SystemInfo.cs`, dar corpo ao record:

```csharp
    string ComputerName)
{
    public bool IsX3D => CpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase);

    // ponytail: heurística por nome e contagem de núcleos; trocar por GetLogicalProcessorInformationEx
    // (EfficiencyClass) se aparecer falso positivo que importe. Um falso positivo só rotula core parking
    // como "não recomendado"; o usuário ainda pode aplicar.
    public bool IsHybridCpu =>
        CpuName.Contains("Core(TM) Ultra", StringComparison.OrdinalIgnoreCase)
        || CpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase)
        || (CpuCores > 0 && CpuLogical != CpuCores && CpuLogical != CpuCores * 2);
}
```

- [ ] **Step 5: Criar o log**

`src/ProjectBoostX/Services/ActionLog.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>Registro de tudo que o programa aplicou ou reverteu, uma linha JSON por ação.</summary>
public static class ActionLog
{
    private static readonly object Gate = new();

    public sealed record Entry(DateTime Utc, string ItemId, string Action, bool Ok, string? Message);

    public static string FilePath => Path.Combine(AppPaths.DataDir, "log", "actions.jsonl");

    public static void Write(string itemId, string action, bool ok, string? message = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(new Entry(DateTime.UtcNow, itemId, action, ok, message)) + "\n");
            }
        }
        catch (IOException) { /* o log nunca derruba a operação */ }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 6: Criar o motor**

`src/ProjectBoostX/Services/OptimizationEngine.cs`:

```csharp
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>Aplica, detecta e reverte itens do catálogo a partir da declaração de cada um.</summary>
public static class OptimizationEngine
{
    /// <summary>Nunca lança: o resultado fica em <see cref="OptimizationItem.State"/> e <see cref="OptimizationItem.StatusMessage"/>.</summary>
    public static async Task<ApplyState> ApplyAsync(OptimizationItem item)
    {
        item.StatusMessage = null;
        try
        {
            await Task.Run(() =>
            {
                foreach (var v in item.Registry)
                    RegistryBackupService.Set(v.Root, v.Key, v.Name, v.Value, v.Kind, item.Id);
            });
            if (item.ApplyExtra is not null) await item.ApplyExtra(item.Id);

            ActionLog.Write(item.Id, "aplicar", true);
            if (item.IsAction)
            {
                item.StatusMessage = $"Executado às {DateTime.Now:HH:mm}";
                return item.State = ApplyState.NotApplied;
            }
            OptimizationStateStore.MarkApplied(item.Id);
            return item.State = ApplyState.Applied;
        }
        catch (Exception ex)
        {
            // Desfaz o que já foi gravado. Se nem isso for possível, os backups continuam pendentes.
            var message = ex.Message;
            try { await RevertCoreAsync(item.Id); }
            catch { message += " A reversão automática falhou; use Reverter para tentar de novo."; }
            item.StatusMessage = message;
            ActionLog.Write(item.Id, "aplicar", false, message);
            return item.State = ApplyState.Failed;
        }
    }

    /// <summary>Nunca lança. Devolve <c>false</c> se algum backup não pôde ser restaurado (ele continua pendente).</summary>
    public static async Task<bool> RevertAsync(OptimizationItem item)
    {
        try
        {
            await RevertCoreAsync(item.Id);
            item.StatusMessage = null;
            item.State = IsApplied(item) ? ApplyState.Applied : ApplyState.NotApplied;
            ActionLog.Write(item.Id, "reverter", true);
            return true;
        }
        catch (Exception ex)
        {
            item.StatusMessage = $"Reversão incompleta: {ex.Message}";
            ActionLog.Write(item.Id, "reverter", false, ex.Message);
            return false;
        }
    }

    public static bool IsApplied(OptimizationItem item)
    {
        if (item.IsAction) return false;
        // Sem nada para ler no sistema (ex.: plano de energia duplicado): vale o que o programa registrou.
        if (item.Registry.Count == 0 && item.IsAppliedExtra is null)
            return OptimizationStateStore.Load().Contains(item.Id);
        try { return RegistryMatches(item) && (item.IsAppliedExtra?.Invoke() ?? true); }
        catch { return false; }
    }

    public static void Refresh(IEnumerable<OptimizationItem> items)
    {
        foreach (var item in items)
            item.State = IsApplied(item) ? ApplyState.Applied : ApplyState.NotApplied;
    }

    public static void ApplyHardwareRules(IEnumerable<OptimizationItem> items, SystemInfo info)
    {
        foreach (var item in items)
            item.NotRecommendedReason = item.NotRecommended?.Invoke(info);
    }

    internal static bool RegistryMatches(OptimizationItem item) => item.Registry.All(v =>
    {
        using var key = v.Root.OpenSubKey(v.Key, writable: false);
        return Equals(key?.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), v.Value);
    });

    private static async Task RevertCoreAsync(string owner)
    {
        await Task.Run(() => RegistryBackupService.RestoreMatchingBackups(owner));
        await SystemSettingsBackupService.RevertAsync(owner);
        OptimizationStateStore.Clear(owner);
    }
}
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet run --project tests/Catalog.Tests -c Release`
Expected: `12/12 passed`.

O app continua compilando: `IsReversible` não tem consumidores e os campos novos são opcionais.

Run: `dotnet build ProjectBoostX.sln -c Release`
Expected: 0 erros.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: itens declarativos, motor de aplicar/detectar/reverter e log de ações"
```

---

### Task 5: Catálogo declarativo e revisado

Substitui `OptimizationCatalog.cs` (903 linhas), `OptimizationStateDetector.cs` e `GameOptimizationService.cs` por dados. O catálogo vai de 48 para 37 itens (lista de removidos e motivos na spec, seção 4).

**Files:**
- Create: `src/ProjectBoostX/Services/Catalog/OptimizationCatalog.cs`, `Catalog/Power.cs`, `Catalog/Gaming.cs`, `Catalog/InputVisual.cs`, `Catalog/Privacy.cs`, `Catalog/Services.cs`, `Catalog/SystemNetwork.cs`
- Delete: `src/ProjectBoostX/Services/OptimizationCatalog.cs`, `src/ProjectBoostX/Services/OptimizationStateDetector.cs`, `src/ProjectBoostX/Services/GameOptimizationService.cs`
- Modify: `tests/Catalog.Tests/Catalog.Tests.csproj`, `tests/Catalog.Tests/Program.cs`

**Interfaces:**
- Consumes: `RegValue`, campos de `OptimizationItem`, `OptimizationEngine` (Task 4); `SystemSettingsBackupService.*Async(..., owner)`, `PowerPlanService.ActivateHighPerformanceAsync(owner)`, `RegistryBackupService.SetDword(..., owner)`/`GetDword` (Task 3); `PowerNative.ReadAc` (Task 2); `ProcessRunner.RunCheckedAsync`.
- Produces: `OptimizationCatalog.All : IReadOnlyList<OptimizationItem>` (instâncias únicas), `OptimizationCatalog.Preset(RiskLevel maxRisk) : IEnumerable<OptimizationItem>`.

Depois desta tarefa o app **não compila** até a Task 7 (os viewmodels ainda chamam `CreateAll`, `ApplyAsync` e o detector). A verificação desta tarefa é o projeto `Catalog.Tests`; o build do app volta na Task 7.

- [ ] **Step 1: Incluir o catálogo no projeto de teste e escrever os testes (falham)**

Em `tests/Catalog.Tests/Catalog.Tests.csproj`, adicionar ao segundo `ItemGroup`:

```xml
    <Compile Include="../../src/ProjectBoostX/Services/Catalog/*.cs" LinkBase="Production/Catalog" />
```

Em `tests/Catalog.Tests/Program.cs`, logo **depois** da declaração de `cases` (antes de `var root = …`), adicionar:

```csharp
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
```

Run: `dotnet run --project tests/Catalog.Tests -c Release`
Expected: erro de compilação (`OptimizationCatalog` não existe neste projeto).

- [ ] **Step 2: Núcleo do catálogo**

`src/ProjectBoostX/Services/Catalog/OptimizationCatalog.cs`:

```csharp
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Catálogo de ajustes. Cada item declara uma vez o que grava; aplicar, detectar e reverter
/// saem dessa declaração (ver <see cref="OptimizationEngine"/>).
/// </summary>
public static partial class OptimizationCatalog
{
    /// <summary>Instâncias únicas: todas as telas observam os mesmos itens.</summary>
    public static IReadOnlyList<OptimizationItem> All { get; } =
    [
        .. PowerItems(), .. GamingItems(), .. InputVisualItems(),
        .. PrivacyItems(), .. ServiceItems(), .. SystemNetworkItems()
    ];

    /// <summary>Itens que um clique pode aplicar. Avançados e contraindicados para o PC ficam sempre de fora.</summary>
    public static IEnumerable<OptimizationItem> Preset(RiskLevel maxRisk) => All.Where(i =>
        !i.IsAction && i.IsRecommended && i.Risk != RiskLevel.Advanced && i.Risk <= maxRisk);

    private static RegistryKey HKCU => Registry.CurrentUser;
    private static RegistryKey HKLM => Registry.LocalMachine;

    private static RegValue Dw(RegistryKey root, string key, string name, int value) =>
        new(root, key, name, value, RegistryValueKind.DWord);

    private static RegValue Sz(RegistryKey root, string key, string name, string value) =>
        new(root, key, name, value, RegistryValueKind.String);

    private static string? Laptop(SystemInfo pc) =>
        pc.IsLaptop ? "Em notebook aumenta consumo, temperatura e ruído." : null;
}
```

- [ ] **Step 3: Energia**

`src/ProjectBoostX/Services/Catalog/Power.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string SubProcessor = "sub_processor";

    private static OptimizationItem PowerValue(string id, string name, string description, RiskLevel risk, string impact,
        Func<SystemInfo, string?>? notRecommended, params (string Subgroup, string Setting, uint Value)[] values) => new()
    {
        Id = id, Name = name, Description = description, Category = OptimizationCategory.Power, Risk = risk,
        Impact = impact, NotRecommended = notRecommended,
        ApplyExtra = async owner =>
        {
            foreach (var v in values)
                await SystemSettingsBackupService.SetPowerValueAsync(v.Subgroup, v.Setting, v.Value, owner: owner);
        },
        IsAppliedExtra = () => values.All(v => PowerNative.ReadAc(v.Subgroup, v.Setting) == v.Value)
    };

    private static IEnumerable<OptimizationItem> PowerItems() =>
    [
        new()
        {
            Id = "power.high",
            Name = "Plano de energia de alto desempenho",
            Description = "Ativa o plano Alto Desempenho (ou Desempenho Máximo, se for o disponível).",
            Category = OptimizationCategory.Power, Risk = RiskLevel.Safe, Impact = "CPU não reduz a frequência sob carga",
            NotRecommended = Laptop,
            ApplyExtra = async owner =>
            {
                if (!await PowerPlanService.ActivateHighPerformanceAsync(owner))
                    throw new InvalidOperationException("Não foi possível ativar o plano de alto desempenho.");
            }
        },
        PowerValue("power.sleep", "Desativar suspensão na tomada",
            "Impede que o PC suspenda, hiberne ou apague a tela sozinho enquanto está na tomada.",
            RiskLevel.Safe, "Sem interrupções em sessões longas",
            pc => pc.IsLaptop ? "Em notebook a tela e o PC ficam ligados indefinidamente na tomada." : null,
            ("sub_sleep", "standbyidle", 0), ("sub_sleep", "hibernateidle", 0), ("sub_video", "videoidle", 0)),
        PowerValue("power.cpu.max", "CPU sempre em 100%",
            "Fixa o estado mínimo e máximo do processador em 100% no plano ativo (na tomada).",
            RiskLevel.Moderate, "Sem redução de frequência; mais consumo em repouso", Laptop,
            (SubProcessor, "PROCTHROTTLEMAX", 100), (SubProcessor, "PROCTHROTTLEMIN", 100)),
        PowerValue("power.usb.suspend", "Desativar suspensão seletiva de USB",
            "Mantém mouse, teclado e headset USB sempre energizados.",
            RiskLevel.Safe, "Sem engasgos de periférico ao voltar de pausa", null,
            ("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0)),
        PowerValue("power.pcie.aspm", "Desativar economia do PCI Express (ASPM)",
            "Mantém o link da placa de vídeo e do SSD NVMe sempre ativo.",
            RiskLevel.Moderate, "Menos microtravamentos em GPU e NVMe", Laptop,
            ("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0)),
        PowerValue("power.wireless.max", "Wi-Fi em desempenho máximo",
            "Impede o adaptador Wi-Fi de economizar energia.",
            RiskLevel.Safe, "Ping mais estável no Wi-Fi", null,
            ("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0)),
        // GUID em vez do apelido CPMINCORES: o apelido não existe em todas as máquinas.
        PowerValue("sys.corepark", "Desativar core parking",
            "Impede o Windows de estacionar núcleos da CPU (mínimo de núcleos ativos em 100%).",
            RiskLevel.Moderate, "Todos os núcleos disponíveis de imediato",
            pc => pc.IsLaptop ? "Em notebook aumenta consumo, temperatura e ruído."
                : pc.IsX3D ? "Processadores X3D dependem do core parking para direcionar o jogo aos núcleos certos."
                : pc.IsHybridCpu ? "Em CPU com núcleos P e E, o Windows usa o core parking para escalonar corretamente."
                : null,
            ("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100)),
    ];
}
```

- [ ] **Step 4: Jogos**

`src/ProjectBoostX/Services/Catalog/Gaming.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string GameBar = @"Software\Microsoft\GameBar";
    private const string GameConfigStore = @"System\GameConfigStore";
    private const string GameDvr = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string SystemProfile = @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private static IEnumerable<OptimizationItem> GamingItems() =>
    [
        new()
        {
            Id = "game.mode", Name = "Modo de Jogo do Windows",
            Description = "Liga o Modo de Jogo: o Windows prioriza o jogo e adia atualizações durante a partida.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Mais prioridade para o jogo",
            Registry = [Dw(HKCU, GameBar, "AllowAutoGameMode", 1), Dw(HKCU, GameBar, "AutoGameModeEnabled", 1)]
        },
        new()
        {
            Id = "game.bartips", Name = "Desativar painel e atalhos da Xbox Game Bar",
            Description = "Remove o painel de boas-vindas e a abertura da Game Bar pelo controle.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Menos sobreposição durante o jogo",
            Registry = [Dw(HKCU, GameBar, "ShowStartupPanel", 0), Dw(HKCU, GameBar, "UseNexusForGameBarEnabled", 0)]
        },
        new()
        {
            Id = "game.dvr", Name = "Desativar gravação em segundo plano",
            Description = "Desliga a captura automática e a gravação do que já aconteceu (Game DVR).",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Safe, Impact = "Sem custo de captura",
            Caution = "Você deixa de poder salvar os últimos segundos de jogo com Win+Alt+G.",
            Registry =
            [
                Dw(HKCU, GameDvr, "AppCaptureEnabled", 0), Dw(HKCU, GameDvr, "HistoricalCaptureEnabled", 0),
                Dw(HKCU, GameConfigStore, "GameDVR_Enabled", 0)
            ]
        },
        new()
        {
            Id = "game.fso", Name = "Desativar otimizações de tela cheia (global)",
            Description = "Força tela cheia exclusiva clássica em todos os jogos.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Menos atraso de entrada em jogos antigos",
            Caution = "No Windows 11, jogos em janela sem borda podem perder VRR e HDR automático. Prefira o ajuste por jogo em Perfis.",
            Registry =
            [
                Dw(HKCU, GameConfigStore, "GameDVR_FSEBehaviorMode", 2),
                Dw(HKCU, GameConfigStore, "GameDVR_HonorUserFSEBehaviorMode", 1),
                Dw(HKCU, GameConfigStore, "GameDVR_FSEBehavior", 2),
                Dw(HKCU, GameConfigStore, "GameDVR_DXGIHonorFSEWindowsCompatible", 1),
                Dw(HKCU, GameConfigStore, "GameDVR_EFSEFeatureFlags", 0)
            ]
        },
        new()
        {
            Id = "game.gpu", Name = "Agendamento de GPU acelerado por hardware",
            Description = "Liga o HAGS. Necessário para geração de quadros do DLSS; exige driver compatível.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Menos latência entre CPU e GPU",
            RequiresRestart = true,
            Registry = [Dw(HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2)]
        },
        new()
        {
            Id = "game.mmprofile", Name = "Rede e multimídia sem limitação",
            Description = "Remove o limite de pacotes de rede durante reprodução de mídia e reserva o mínimo de CPU para tarefas de fundo.",
            Category = OptimizationCategory.Gaming, Risk = RiskLevel.Moderate, Impact = "Rede sem estrangulamento com áudio/vídeo tocando",
            // SystemResponsiveness: 10 é o mínimo que o Windows aceita (0 é tratado como 10).
            Registry = [Dw(HKLM, SystemProfile, "NetworkThrottlingIndex", -1), Dw(HKLM, SystemProfile, "SystemResponsiveness", 10)]
        },
    ];
}
```

- [ ] **Step 5: Entrada e visual**

`src/ProjectBoostX/Services/Catalog/InputVisual.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Mouse = @"Control Panel\Mouse";
    private const string Desktop = @"Control Panel\Desktop";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private static IEnumerable<OptimizationItem> InputVisualItems() =>
    [
        new()
        {
            Id = "input.mouseaccel", Name = "Desativar aceleração do mouse",
            Description = "Desliga 'Aprimorar precisão do ponteiro': o cursor anda sempre a mesma distância para o mesmo movimento.",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Mira consistente",
            Registry = [Sz(HKCU, Mouse, "MouseSpeed", "0"), Sz(HKCU, Mouse, "MouseThreshold1", "0"), Sz(HKCU, Mouse, "MouseThreshold2", "0")],
            ApplyExtra = _ => ProcessRunner.RunCheckedAsync("rundll32.exe", "user32.dll,UpdatePerUserSystemParameters")
        },
        new()
        {
            Id = "input.keyboard", Name = "Repetição de tecla mais rápida",
            Description = "Menor atraso inicial e maior taxa de repetição ao segurar uma tecla. É preferência, não desempenho.",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Teclado mais ágil ao segurar teclas",
            Registry = [Sz(HKCU, @"Control Panel\Keyboard", "KeyboardDelay", "0"), Sz(HKCU, @"Control Panel\Keyboard", "KeyboardSpeed", "31")]
        },
        new()
        {
            Id = "input.menudelay", Name = "Menus sem atraso",
            Description = "Abre submenus imediatamente (MenuShowDelay = 0).",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Interface mais ágil",
            Registry = [Sz(HKCU, Desktop, "MenuShowDelay", "0")]
        },
        new()
        {
            Id = "visual.effects", Name = "Efeitos visuais para desempenho",
            Description = "Desliga animações da barra de tarefas e o retângulo de seleção translúcido.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Interface mais leve",
            RequiresRestart = true,
            Registry =
            [
                Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3),
                Dw(HKCU, ExplorerAdvanced, "ListviewAlphaSelect", 0),
                Dw(HKCU, ExplorerAdvanced, "TaskbarAnimations", 0)
            ]
        },
        new()
        {
            Id = "visual.animations", Name = "Desativar animações de janela",
            Description = "Minimizar e maximizar sem animação; arrastar mostra só o contorno da janela.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Janelas instantâneas",
            RequiresRestart = true,
            Registry = [Sz(HKCU, Desktop + @"\WindowMetrics", "MinAnimate", "0"), Sz(HKCU, Desktop, "DragFullWindows", "0")]
        },
        new()
        {
            Id = "visual.transparency", Name = "Desativar transparência",
            Description = "Desliga o efeito translúcido de barra de tarefas, menu Iniciar e janelas.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Menos trabalho de composição para a GPU",
            Registry = [Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)]
        },
    ];
}
```

- [ ] **Step 6: Privacidade**

`src/ProjectBoostX/Services/Catalog/Privacy.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string PolicySystem = @"SOFTWARE\Policies\Microsoft\Windows\System";

    private static IEnumerable<OptimizationItem> PrivacyItems() =>
    [
        new()
        {
            Id = "telemetry.off", Name = "Reduzir telemetria",
            Description = "Define a coleta de diagnóstico no mínimo e desativa as tarefas do Programa de Aperfeiçoamento. Nas edições Home e Pro o mínimo é 'obrigatório', não zero.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Menos atividade de fundo e de rede",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)],
            ApplyExtra = owner => SystemSettingsBackupService.DisableTelemetryTasksAsync(owner)
        },
        new()
        {
            Id = "telemetry.tips", Name = "Desativar dicas, sugestões e apps instalados sozinhos",
            Description = "Corta sugestões no Iniciar e nas Configurações e a instalação silenciosa de apps promovidos.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Sem apps e anúncios surgindo sozinhos",
            Registry =
            [
                Dw(HKCU, Cdm, "SubscribedContent-338389Enabled", 0), Dw(HKCU, Cdm, "SubscribedContent-338388Enabled", 0),
                Dw(HKCU, Cdm, "SystemPaneSuggestionsEnabled", 0), Dw(HKCU, Cdm, "SilentInstalledAppsEnabled", 0)
            ]
        },
        new()
        {
            Id = "telemetry.activity", Name = "Desativar histórico de atividades",
            Description = "O Windows deixa de registrar e enviar o histórico de apps e arquivos abertos.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Menos gravação em disco",
            Registry =
            [
                Dw(HKLM, PolicySystem, "EnableActivityFeed", 0), Dw(HKLM, PolicySystem, "PublishUserActivities", 0),
                Dw(HKLM, PolicySystem, "UploadUserActivities", 0)
            ]
        },
        new()
        {
            Id = "telemetry.wer", Name = "Desativar Relatório de Erros do Windows",
            Description = "Não coleta nem envia relatórios quando um programa trava.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Safe, Impact = "Sem disco e rede usados após um travamento",
            Caution = "Dificulta o diagnóstico se você costuma enviar relatórios de falha a um suporte.",
            Registry =
            [
                Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1),
                Dw(HKCU, @"Software\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)
            ]
        },
        new()
        {
            Id = "telemetry.backgroundapps", Name = "Bloquear apps da Store em segundo plano",
            Description = "Apps da Microsoft Store deixam de rodar quando estão fechados.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Moderate, Impact = "Menos RAM e CPU em repouso",
            Caution = "Apps da Store param de notificar e sincronizar fechados (Email, Calendário, WhatsApp da Store).",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2)]
        },
        new()
        {
            Id = "telemetry.location", Name = "Desativar localização",
            Description = "Desliga o serviço de localização do Windows para todos os apps.",
            Category = OptimizationCategory.Telemetry, Risk = RiskLevel.Moderate, Impact = "Sem varredura de Wi-Fi/GPS",
            Caution = "Clima, mapas, fuso horário automático e 'Localizar meu dispositivo' deixam de funcionar.",
            Registry = [Dw(HKLM, @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1)]
        },
    ];
}
```

- [ ] **Step 7: Serviços**

`src/ProjectBoostX/Services/Catalog/Services.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    /// <summary>Tipo de inicialização do serviço (4 = desativado), ou <c>null</c> se ele não existe nesta edição do Windows.</summary>
    private static int? ServiceStart(string name)
    {
        using var key = HKLM.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: false);
        return key?.GetValue("Start") as int?;
    }

    private static OptimizationItem Service(string id, string name, string description, RiskLevel risk, string impact,
        string caution, Func<SystemInfo, string?>? notRecommended, params string[] services) => new()
    {
        Id = id, Name = name, Description = description, Category = OptimizationCategory.Services, Risk = risk,
        Impact = impact, Caution = caution, NotRecommended = notRecommended,
        ApplyExtra = async owner =>
        {
            // Serviço ausente ou já desativado: nada a fazer.
            foreach (var service in services.Where(s => ServiceStart(s) is int start && start != 4))
                await SystemSettingsBackupService.DisableServiceAsync(service, owner);
        },
        IsAppliedExtra = () => services.All(s => ServiceStart(s) is null or 4)
    };

    private static IEnumerable<OptimizationItem> ServiceItems() =>
    [
        Service("services.diagtrack", "Serviços de telemetria",
            "Desativa 'Experiências do Usuário Conectado e Telemetria' e o roteador de mensagens WAP.",
            RiskLevel.Safe, "Menos coleta em segundo plano", "", null, "DiagTrack", "dmwappushservice"),
        Service("services.sysmain", "SysMain (Superfetch)",
            "Desativa o pré-carregamento de programas na memória.",
            RiskLevel.Moderate, "Menos leitura de disco em repouso",
            "Programas que você abre sempre podem demorar um pouco mais na primeira abertura.",
            pc => pc.DiskType == "HD" ? "Em HD o SysMain acelera bastante a abertura de programas."
                : pc.DiskType == "SSD" ? null : "Tipo de disco não identificado; mantenha o SysMain ligado.",
            "SysMain"),
        Service("services.search", "Indexação do Windows Search",
            "Desativa o indexador de arquivos.",
            RiskLevel.Moderate, "Menos CPU e disco em repouso",
            "A busca do menu Iniciar, do Explorador e do Outlook fica bem mais lenta.", null, "WSearch"),
        Service("services.fax", "Serviço de Fax", "Desativa o serviço de fax.",
            RiskLevel.Safe, "Um serviço a menos", "", null, "Fax"),
        Service("services.maps", "Gerenciador de mapas baixados", "Desativa a atualização de mapas offline.",
            RiskLevel.Safe, "Um serviço a menos", "", null, "MapsBroker"),
        Service("services.xbox", "Serviços Xbox Live",
            "Desativa autenticação, salvamento em nuvem, rede e acessórios do Xbox.",
            RiskLevel.Moderate, "Quatro serviços a menos",
            "Game Pass, app Xbox, jogos da Microsoft Store e controles Xbox sem fio dependem destes serviços.", null,
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"),
        Service("services.printer", "Spooler de impressão", "Desativa o serviço de impressão.",
            RiskLevel.Advanced, "Um processo residente a menos",
            "Nenhuma impressora funciona, nem 'Imprimir em PDF'.", null, "Spooler"),
    ];
}
```

- [ ] **Step 8: Sistema e rede**

`src/ProjectBoostX/Services/Catalog/SystemNetwork.cs`:

```csharp
using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Interfaces = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    /// <summary>Chaves das interfaces de rede que têm endereço IP.</summary>
    private static IEnumerable<string> ActiveInterfaces()
    {
        using var interfaces = HKLM.OpenSubKey(Interfaces, writable: false);
        if (interfaces is null) yield break;
        foreach (var name in interfaces.GetSubKeyNames())
        {
            using var key = interfaces.OpenSubKey(name, writable: false);
            if (key?.GetValue("DhcpIPAddress") is not null || key?.GetValue("IPAddress") is not null)
                yield return $@"{Interfaces}\{name}";
        }
    }

    private static IEnumerable<OptimizationItem> SystemNetworkItems() =>
    [
        new()
        {
            Id = "net.nagle", Name = "Desativar algoritmo de Nagle",
            Description = "Envia pacotes TCP pequenos sem esperar, nas interfaces de rede ativas. Só afeta jogos que usam TCP; a maioria usa UDP e não muda.",
            Category = OptimizationCategory.Network, Risk = RiskLevel.Moderate, Impact = "Latência menor em jogos TCP",
            ApplyExtra = owner => Task.Run(() =>
            {
                var paths = ActiveInterfaces().ToList();
                if (paths.Count == 0)
                    throw new InvalidOperationException("Nenhuma interface de rede ativa foi encontrada.");
                foreach (var path in paths)
                {
                    RegistryBackupService.SetDword(HKLM, path, "TcpAckFrequency", 1, owner);
                    RegistryBackupService.SetDword(HKLM, path, "TcpNoDelay", 1, owner);
                }
            }),
            IsAppliedExtra = () => ActiveInterfaces().ToList() is { Count: > 0 } paths && paths.All(p =>
                RegistryBackupService.GetDword(HKLM, p, "TcpAckFrequency") == 1 &&
                RegistryBackupService.GetDword(HKLM, p, "TcpNoDelay") == 1)
        },
        new()
        {
            Id = "tools.dnsflush", Name = "Limpar cache de DNS",
            Description = "Esvazia o cache de nomes. Útil depois de trocar de rede, VPN ou servidor DNS.",
            Category = OptimizationCategory.Network, Risk = RiskLevel.Safe, Impact = "Resolução de nomes limpa",
            IsAction = true,
            ApplyExtra = _ => ProcessRunner.RunCheckedAsync("ipconfig", "/flushdns")
        },
        new()
        {
            Id = "memory.ntfs", Name = "NTFS sem registro de último acesso",
            Description = "O sistema de arquivos deixa de gravar a data de último acesso a cada leitura.",
            Category = OptimizationCategory.Memory, Risk = RiskLevel.Safe, Impact = "Menos escrita em disco",
            RequiresRestart = true,
            // 0x80000001 = desativado, gerenciado pelo usuário.
            Registry = [Dw(HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001))]
        },
        new()
        {
            Id = "sys.startupdelay", Name = "Remover atraso dos programas de inicialização",
            Description = "Os programas que abrem com o Windows deixam de esperar cerca de 10 segundos após o login.",
            Category = OptimizationCategory.System, Risk = RiskLevel.Safe, Impact = "Área de trabalho pronta mais cedo",
            Registry = [Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0)]
        },
        new()
        {
            Id = "sys.hibernation", Name = "Desativar hibernação",
            Description = "Desliga a hibernação e apaga o arquivo hiberfil.sys.",
            Category = OptimizationCategory.System, Risk = RiskLevel.Moderate, Impact = "Libera vários GB no disco do sistema",
            Caution = "A Inicialização Rápida do Windows também é desligada; o boot a frio pode ficar um pouco mais lento.",
            NotRecommended = pc => pc.IsLaptop ? "Notebook perde a hibernação ao fechar a tampa ou com bateria fraca." : null,
            ApplyExtra = owner => SystemSettingsBackupService.SetHibernationAsync(false, owner),
            IsAppliedExtra = () => RegistryBackupService.GetDword(HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0
        },
    ];
}
```

- [ ] **Step 9: Apagar o código substituído**

```bash
git rm src/ProjectBoostX/Services/OptimizationCatalog.cs src/ProjectBoostX/Services/OptimizationStateDetector.cs src/ProjectBoostX/Services/GameOptimizationService.cs
```

- [ ] **Step 10: Rodar e ver passar**

Run: `dotnet run --project tests/Catalog.Tests -c Release`
Expected: `57/57 passed` (12 do motor + 36 itens não pontuais + 9 do catálogo). Se algum caso `<id>: aplicar, detectar e reverter…` falhar com "Não foi detectado como aplicado", o valor declarado e o tipo não batem (por exemplo, `uint` onde o Registro devolve `int`); corrigir a declaração do item.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: catálogo declarativo revisado (37 itens, sem placebos nem sobreposição)"
```

---

### Task 6: Informações do sistema corretas e sem processos externos

**Files:**
- Modify: `src/ProjectBoostX/Services/SystemInfoService.cs`

**Interfaces:**
- Consumes: `PowerNative.ActiveSchemeName()` (Task 2).
- Produces: `SystemInfo.DiskType` devolve `"SSD"`, `"HD"` ou `"Desconhecido"` para o disco físico que contém a unidade do sistema. `SystemInfoService.GetCurrentPowerPlan` e `GetCurrentPowerPlanAsync` deixam de existir.

Este código só fala com o Windows (WMI); não há teste automatizado. A verificação é a comparação do Step 3, conferida no app na Task 7. O app segue sem compilar até lá (ver Task 5).

- [ ] **Step 1: Corrigir o tipo de disco**

Em `src/ProjectBoostX/Services/SystemInfoService.cs`, dentro de `GetDiskInfo`, substituir o bloco que começa em `var type = "Desconhecido";` e termina no `catch { type = "Desconhecido"; }` por:

```csharp
            var type = GetSystemDiskType(drive.Name[0]);
```

E adicionar o método:

```csharp
    /// <summary>
    /// Tipo do disco físico que contém a unidade. Win32_DiskDrive.MediaType devolve
    /// "Fixed hard disk media" também para SSD; a fonte correta é MSFT_PhysicalDisk.
    /// </summary>
    private static string GetSystemDiskType(char driveLetter)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            uint? diskNumber = null;
            using (var partitions = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition")))
            {
                foreach (var partition in partitions.Get())
                {
                    if (partition["DriveLetter"] is char letter && char.ToUpperInvariant(letter) == char.ToUpperInvariant(driveLetter))
                    {
                        diskNumber = Convert.ToUInt32(partition["DiskNumber"]);
                        break;
                    }
                }
            }
            if (diskNumber is null) return "Desconhecido";

            using var disks = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"));
            foreach (var disk in disks.Get())
            {
                if (disk["DeviceId"]?.ToString() != diskNumber.Value.ToString()) continue;
                return Convert.ToUInt16(disk["MediaType"]) switch { 4 => "SSD", 3 => "HD", _ => "Desconhecido" };
            }
        }
        catch { /* espaços de armazenamento, RAID ou WMI indisponível */ }
        return "Desconhecido";
    }
```

- [ ] **Step 2: Remover a leitura do plano por `powercfg`**

Apagar de `SystemInfoService.cs` os métodos `GetCurrentPowerPlan` e `GetCurrentPowerPlanAsync` inteiros. O único consumidor (`DashboardViewModel`) passa a usar `PowerNative.ActiveSchemeName()` na Task 7.

- [ ] **Step 3: Conferir contra o Windows**

```powershell
Get-CimInstance -Namespace root/Microsoft/Windows/Storage MSFT_Partition | Where-Object DriveLetter -eq 'C' | Select-Object DiskNumber
Get-CimInstance -Namespace root/Microsoft/Windows/Storage MSFT_PhysicalDisk | Select-Object DeviceId, MediaType, FriendlyName
powercfg /getactivescheme
```

Anotar o `MediaType` do disco cujo `DeviceId` é o `DiskNumber` de C: (4 = SSD) e o nome do plano entre parênteses. Os dois são comparados com o que o app mostra no Step 6 da Task 7.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "fix: tipo de disco correto (SSD aparecia como HD) e plano de energia sem powercfg"
```

---

### Task 7: Ligar as telas ao motor

**Files:**
- Modify: `src/ProjectBoostX/ViewModels/FeatureViewModels.cs` (classes `DashboardViewModel` e `GamingViewModel`)
- Create: `src/ProjectBoostX/ViewModels/MainViewModel.Hardware.cs`
- Modify: `src/ProjectBoostX/MainWindow.xaml` (navegação e a seção "Gaming optimizations", linhas 53–54 e 174–233)

**Interfaces:**
- Consumes: `OptimizationCatalog.All` (Task 5); `OptimizationEngine.ApplyAsync/RevertAsync/Refresh/ApplyHardwareRules` (Task 4); `PowerNative.ActiveSchemeName()` (Task 2); `RegistryBackupService.GetDword`.
- Produces: `GamingViewModel.ApplyItemCommand`, `GamingViewModel.RevertItemCommand` (parâmetro `OptimizationItem`).

`MainViewModel.cs` não é alterado: ele é compilado dentro de `tests/Commands.Tests` com stubs próprios. A regra de hardware entra por um arquivo parcial separado, que aquele projeto não inclui.

A tela "Serviços" (`DebloatViewModel`) continua existindo nesta fase, embora os mesmos serviços agora estejam no catálogo; ela é removida na Fase 2, quando a navegação é refeita.

- [ ] **Step 1: `DashboardViewModel`**

Substituir o construtor, `InitializeAsync`, `RefreshInfoAsync` e o laço de `BoostAsync`:

```csharp
    private static readonly string[] QuickWinIds =
        ["power.high", "game.mode", "game.dvr", "visual.transparency", "telemetry.off", "sys.startupdelay"];

    public DashboardViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var item in OptimizationCatalog.All.Where(i => QuickWinIds.Contains(i.Id)))
            QuickWins.Add(item);
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => OptimizationEngine.Refresh(QuickWins));
        foreach (var item in QuickWins)
            item.IsSelected = item.State != ApplyState.Applied && item.IsRecommended;
        AppliedCount = QuickWins.Count(i => i.State == ApplyState.Applied);
        SelectedCount = QuickWins.Count(i => i.IsSelected);
        await RefreshInfoAsync();
    }

    private async Task RefreshInfoAsync()
    {
        try
        {
            PowerPlan = await Task.Run(PowerNative.ActiveSchemeName);
            GameModeStatus = await Task.Run(() =>
                RegistryBackupService.GetDword(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") == 1
                    ? "Ativado" : "Desativado / padrão");
        }
        catch
        {
            PowerPlan = "Desconhecido";
        }
    }
```

Em `BoostAsync`, o bloco `int ok = 0, fail = 0; foreach (var item in selected) { … }` vira:

```csharp
        int ok = 0, fail = 0;
        foreach (var item in selected)
        {
            _main.StatusMessage = $"Aplicando: {item.Name}";
            if (await OptimizationEngine.ApplyAsync(item) == ApplyState.Applied) ok++;
            else fail++;
        }
```

Em `RevertAllAsync`, o bloco `if (result.Success) { … }` vira:

```csharp
        if (result.Success)
        {
            await Task.Run(() => OptimizationEngine.Refresh(OptimizationCatalog.All));
            await InitializeAsync();
            await _main.Gaming.InitializeAsync();
        }
```

- [ ] **Step 2: `GamingViewModel`**

Substituir a classe inteira por:

```csharp
public partial class GamingViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ObservableCollection<OptimizationItem> Items { get; } = [];

    [ObservableProperty]
    private bool _selectAll = true;

    public GamingViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var item in OptimizationCatalog.All) Items.Add(item);
    }

    public async Task InitializeAsync()
    {
        await Task.Run(() => OptimizationEngine.Refresh(Items));
        SelectRecommended();
    }

    /// <summary>A seleção automática só marca o que é seguro e indicado para este PC.</summary>
    private void SelectRecommended()
    {
        foreach (var item in Items)
            item.IsSelected = SelectAll && !item.IsAction && item.IsRecommended
                              && item.Risk == RiskLevel.Safe && item.State != ApplyState.Applied;
    }

    [RelayCommand]
    private void ToggleSelectAll() => SelectRecommended();

    [RelayCommand]
    private Task ApplySelectedAsync() => _main.RunOperationAsync("Aplicando otimizações…", async () =>
    {
        var selected = Items.Where(i => i.IsSelected && !i.IsAction).ToList();
        if (selected.Count == 0)
        {
            _main.StatusMessage = "Nada selecionado";
            return;
        }

        await _main.RequireRestorePointAsync("Project Boost X - Otimizações");

        var ok = 0;
        foreach (var item in selected)
        {
            _main.StatusMessage = $"Aplicando: {item.Name}";
            if (await OptimizationEngine.ApplyAsync(item) != ApplyState.Applied) continue;
            ok++;
            item.IsSelected = false;
        }

        _main.StatusMessage = $"{ok}/{selected.Count} otimizações aplicadas · {selected.Count - ok} falhas";
    });

    [RelayCommand]
    private Task ApplyItemAsync(OptimizationItem? item) => _main.RunOperationAsync("Aplicando…", async () =>
    {
        if (item is null) return;
        if (!item.IsAction) await _main.RequireRestorePointAsync($"Project Boost X - {item.Name}");
        var state = await OptimizationEngine.ApplyAsync(item);
        item.IsSelected = false;
        _main.StatusMessage = state == ApplyState.Failed
            ? $"{item.Name}: {item.StatusMessage}"
            : item.IsAction ? $"{item.Name}: concluído" : $"{item.Name}: aplicado";
    });

    [RelayCommand]
    private Task RevertItemAsync(OptimizationItem? item) => _main.RunOperationAsync("Revertendo…", async () =>
    {
        if (item is null) return;
        var ok = await OptimizationEngine.RevertAsync(item);
        _main.StatusMessage = ok ? $"{item.Name}: revertido" : $"{item.Name}: {item.StatusMessage}";
    });
}
```

- [ ] **Step 3: Regras de hardware quando as informações do PC chegam**

`src/ProjectBoostX/ViewModels/MainViewModel.Hardware.cs`:

```csharp
using BoostParaPc.Models;
using BoostParaPc.Services;

namespace BoostParaPc.ViewModels;

public partial class MainViewModel
{
    // Chamado pelo setter gerado de SystemInfo, na thread da interface.
    partial void OnSystemInfoChanged(SystemInfo? value)
    {
        if (value is null) return;
        OptimizationEngine.ApplyHardwareRules(OptimizationCatalog.All, value);
        foreach (var item in OptimizationCatalog.All.Where(i => !i.IsRecommended))
            item.IsSelected = false;
    }
}
```

- [ ] **Step 4: Tela — todas as categorias, botões por item e avisos**

Em `src/ProjectBoostX/MainWindow.xaml`:

1. Navegação: `Content="⚡  Boost de jogo"` → `Content="⚡  Otimizações"`.

2. Cabeçalho da seção (linhas 176–178):

```xml
                                    <TextBlock Text="Otimizações" Style="{StaticResource TitleText}" />
                                    <TextBlock Style="{StaticResource MutedText}" Margin="0,0,0,16"
                                               Text="Todos os ajustes do catálogo. Cada um pode ser aplicado e revertido sozinho. Itens de risco Moderado e Avançado só entram se você marcar." />
```

3. Caixa de seleção (linha 183): `Content="Selecionar pendentes"` → `Content="Selecionar recomendadas (risco Seguro)"`.

4. Substituir o `DataTemplate` do `ItemsControl ItemsSource="{Binding Gaming.Items}"` (linhas 191–225) por:

```xml
                                                    <DataTemplate>
                                                        <Border Background="{StaticResource SurfaceAltBrush}" CornerRadius="10" Padding="14" Margin="0,0,0,8">
                                                            <Grid>
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="Auto" />
                                                                    <ColumnDefinition Width="*" />
                                                                    <ColumnDefinition Width="Auto" />
                                                                </Grid.ColumnDefinitions>
                                                                <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" VerticalAlignment="Top" Margin="0,3,12,0" />
                                                                <StackPanel Grid.Column="1">
                                                                    <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="13" />
                                                                    <TextBlock Text="{Binding Description}" Style="{StaticResource MutedText}" FontSize="12" />
                                                                    <TextBlock Text="{Binding Caution}" FontSize="11" Foreground="{StaticResource WarningBrush}" Margin="0,4,0,0">
                                                                        <TextBlock.Style>
                                                                            <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding Caution}" Value="">
                                                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </TextBlock.Style>
                                                                    </TextBlock>
                                                                    <TextBlock FontSize="11" Foreground="{StaticResource DangerBrush}" Margin="0,4,0,0">
                                                                        <Run Text="Não recomendado para este PC: " /><Run Text="{Binding NotRecommendedReason, Mode=OneWay}" />
                                                                        <TextBlock.Style>
                                                                            <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding NotRecommendedReason}" Value="{x:Null}">
                                                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </TextBlock.Style>
                                                                    </TextBlock>
                                                                    <TextBlock Text="{Binding StatusMessage}" FontSize="11" Foreground="{StaticResource TextMutedBrush}" Margin="0,4,0,0">
                                                                        <TextBlock.Style>
                                                                            <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding StatusMessage}" Value="{x:Null}">
                                                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </TextBlock.Style>
                                                                    </TextBlock>
                                                                    <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                                                                        <Border Style="{StaticResource Chip}">
                                                                            <TextBlock FontSize="11" Foreground="{StaticResource AccentBrush}">
                                                                                <Run Text="Impacto: " /><Run Text="{Binding Impact, Mode=OneWay}" />
                                                                            </TextBlock>
                                                                        </Border>
                                                                        <Border Style="{StaticResource Chip}">
                                                                            <TextBlock FontSize="11">
                                                                                <Run Text="Risco: " /><Run Text="{Binding RiskDisplay, Mode=OneWay}" />
                                                                                <TextBlock.Style>
                                                                                    <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                                                        <Setter Property="Foreground" Value="{StaticResource SuccessBrush}" />
                                                                                        <Style.Triggers>
                                                                                            <DataTrigger Binding="{Binding Risk}" Value="Moderate">
                                                                                                <Setter Property="Foreground" Value="{StaticResource WarningBrush}" />
                                                                                            </DataTrigger>
                                                                                            <DataTrigger Binding="{Binding Risk}" Value="Advanced">
                                                                                                <Setter Property="Foreground" Value="{StaticResource DangerBrush}" />
                                                                                            </DataTrigger>
                                                                                        </Style.Triggers>
                                                                                    </Style>
                                                                                </TextBlock.Style>
                                                                            </TextBlock>
                                                                        </Border>
                                                                        <Border Style="{StaticResource Chip}">
                                                                            <TextBlock FontSize="11" Foreground="{StaticResource TextMutedBrush}" Text="{Binding CategoryDisplay}" />
                                                                        </Border>
                                                                        <Border Style="{StaticResource Chip}" Visibility="{Binding RequiresRestart, Converter={StaticResource BoolToVis}}">
                                                                            <TextBlock FontSize="11" Foreground="{StaticResource Accent2Brush}" Text="Requer reinício" />
                                                                        </Border>
                                                                    </StackPanel>
                                                                </StackPanel>
                                                                <StackPanel Grid.Column="2" VerticalAlignment="Center" Margin="12,0,0,0">
                                                                    <TextBlock Text="{Binding StateDisplay}" HorizontalAlignment="Right" FontSize="12" FontWeight="SemiBold">
                                                                        <TextBlock.Style>
                                                                            <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                                                                                <Setter Property="Foreground" Value="{StaticResource TextMutedBrush}" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding State}" Value="Applied">
                                                                                        <Setter Property="Foreground" Value="{StaticResource SuccessBrush}" />
                                                                                    </DataTrigger>
                                                                                    <DataTrigger Binding="{Binding State}" Value="Failed">
                                                                                        <Setter Property="Foreground" Value="{StaticResource DangerBrush}" />
                                                                                    </DataTrigger>
                                                                                    <DataTrigger Binding="{Binding IsAction}" Value="True">
                                                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </TextBlock.Style>
                                                                    </TextBlock>
                                                                    <Button Margin="0,6,0,0"
                                                                            Command="{Binding DataContext.Gaming.ApplyItemCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                                            CommandParameter="{Binding}">
                                                                        <Button.Style>
                                                                            <Style TargetType="Button" BasedOn="{StaticResource SecondaryButton}">
                                                                                <Setter Property="Content" Value="Aplicar" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding IsAction}" Value="True">
                                                                                        <Setter Property="Content" Value="Executar" />
                                                                                    </DataTrigger>
                                                                                    <DataTrigger Binding="{Binding State}" Value="Applied">
                                                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </Button.Style>
                                                                    </Button>
                                                                    <Button Content="Reverter" Margin="0,6,0,0"
                                                                            Command="{Binding DataContext.Gaming.RevertItemCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                                                            CommandParameter="{Binding}">
                                                                        <Button.Style>
                                                                            <Style TargetType="Button" BasedOn="{StaticResource DangerButton}">
                                                                                <Setter Property="Visibility" Value="Collapsed" />
                                                                                <Style.Triggers>
                                                                                    <DataTrigger Binding="{Binding State}" Value="Applied">
                                                                                        <Setter Property="Visibility" Value="Visible" />
                                                                                    </DataTrigger>
                                                                                </Style.Triggers>
                                                                            </Style>
                                                                        </Button.Style>
                                                                    </Button>
                                                                </StackPanel>
                                                            </Grid>
                                                        </Border>
                                                    </DataTemplate>
```

- [ ] **Step 5: Build e todos os testes**

```bash
dotnet build ProjectBoostX.sln -c Release
dotnet run --project tests/Backup.Tests -c Release
dotnet run --project tests/CleanupStartup.Tests -c Release
dotnet run --project tests/GameProfiles.Tests -c Release
dotnet run --project tests/Commands.Tests -c Release
dotnet run --project tests/SystemSettings.Tests -c Release
dotnet run --project tests/Catalog.Tests -c Release
```

Expected: build com 0 erros e 0 referências a `OptimizationStateDetector`, `GameOptimizationService`, `CreateAll` ou `SystemInfoService.GetCurrentPowerPlan`; `8/8`, `Failures: 0`, `16/16`, `14/14`, `18/18`, `57/57`.

- [ ] **Step 6: Verificação manual no app (feita pelo usuário; exige aceitar o UAC)**

```bash
dotnet run --project src/ProjectBoostX -c Release
```

Conferir, nesta ordem:

1. A janela abre e o rodapé mostra o mesmo nome de plano de energia que `powercfg /getactivescheme` (anotado na Task 6).
2. O cartão "Disco" e a barra lateral mostram o tipo anotado na Task 6 (nesta máquina, **SSD**; antes mostrava HD).
3. Em **Otimizações** aparecem 37 itens, incluindo Visual, Telemetria, Serviços, Memória e Sistema.
4. Só itens de risco **Seguro** vêm marcados; o chip de risco é verde, amarelo ou vermelho conforme o nível.
5. Em um item já aplicado aparece **Reverter**; em um não aplicado, **Aplicar**. "Limpar cache de DNS" mostra **Executar**.
6. Com "Exigir ponto de restauração" desmarcado, clicar **Aplicar** em "Menus sem atraso": o estado vira "Aplicado" em verde. Clicar **Reverter**: volta a "Não aplicado". Ir ao **Dashboard** e voltar: o estado é o mesmo nas duas telas.
7. Clicar **Aplicar** em "Desativar suspensão seletiva de USB" e depois **Reverter**: ambos concluem sem a mensagem "índices AC/DC" (era o erro em Windows em português).
8. `%APPDATA%\ProjectBoostX\log\actions.jsonl` tem uma linha para cada ação dos passos 6 e 7.

- [ ] **Step 7: Publicar e commit**

```bash
dotnet publish src/ProjectBoostX/ProjectBoostX.csproj -c Release -r win-x64 --self-contained false -o publish
git add -A
git commit -m "feat: telas usam o motor; todas as otimizações visíveis, com aplicar e reverter por item"
```

---

## Resultado da Fase 1

- 37 otimizações, todas visíveis, cada uma aplicável e reversível sozinha.
- Ajustes de energia funcionam em Windows em qualquer idioma.
- Itens contraindicados para o PC aparecem marcados e fora da seleção automática.
- Disco e plano de energia corretos, sem processos externos na abertura.
- Seis conjuntos de testes, 125 casos (eram 65 em cinco conjuntos).

O visual continua o da v2.1. A Fase 2 (interface) parte daqui: ver `2026-10-05-boost-x-v3-roteiro-fases-2-4.md`.
