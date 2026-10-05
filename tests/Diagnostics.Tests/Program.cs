using BoostParaPc.Services;

var cases = new (string Name, Action Run)[]
{
    ("Dado ausente não vira achado", () => Equal(0, Diagnostics.Evaluate(new DiagnosticInputs()).Count)),
    ("Monitor abaixo da taxa máxima", () => Has("display.refresh", new() { DisplayCurrentHz = 60, DisplayMaxHz = 144 })),
    ("Monitor na taxa máxima não gera achado", () => Lacks("display.refresh", new() { DisplayCurrentHz = 144, DisplayMaxHz = 144 })),
    ("DDR4 em 2133 sugere XMP; 3600 não", () =>
    {
        Has("ram.basespeed", new() { RamType = "DDR4", RamSpeedMhz = 2133 });
        Lacks("ram.basespeed", new() { RamType = "DDR4", RamSpeedMhz = 3600 });
    }),
    ("DDR5 em 4800 sugere EXPO; 6000 não", () =>
    {
        Has("ram.basespeed", new() { RamType = "DDR5", RamSpeedMhz = 4800 });
        Lacks("ram.basespeed", new() { RamType = "DDR5", RamSpeedMhz = 6000 });
    }),
    ("Canal único", () => { Has("ram.single", new() { RamModuleCount = 1 }); Lacks("ram.single", new() { RamModuleCount = 2 }); }),
    ("Driver com mais de 180 dias", () => { Has("gpu.driver", new() { GpuDriverAgeDays = 181 }); Lacks("gpu.driver", new() { GpuDriverAgeDays = 180 }); }),
    ("TRIM desligado", () => { Has("disk.trim", new() { TrimDisabled = true }); Lacks("disk.trim", new() { TrimDisabled = false }); }),
    ("C: com menos de 15% livre é crítico", () =>
    {
        Equal(Severity.Critical, Diagnostics.Evaluate(new() { SystemFreePercent = 10 }).Single(x => x.Id == "disk.free").Severity);
        Lacks("disk.free", new() { SystemFreePercent = 15 });
    }),
    ("Paginação desativada", () => Has("ram.paging", new() { PagingDisabled = true })),
    ("Reinício pendente", () => Has("system.reboot", new() { RebootPending = true })),
    ("Notebook na bateria", () => Has("power.battery", new() { OnBattery = true })),
    ("Jogos em HD citam os nomes", () =>
    {
        var f = Diagnostics.Evaluate(new() { GamesOnHdd = ["Fortnite"] }).Single(x => x.Id == "games.hdd");
        Require(f.Detail.Contains("Fortnite"), "Nome do jogo ausente.");
        Lacks("games.hdd", new() { GamesOnHdd = [] });
    }),
    ("TCP autotuning desligado", () => Has("net.tcp", new() { TcpAutoTuningDisabled = true })),
    ("Integridade de Memória ligada", () => Has("sec.memoryintegrity", new() { MemoryIntegrityOn = true })),
    ("Nota: 100 − 15×críticos − 7×avisos, mínimo 0", () =>
    {
        Equal(100, Diagnostics.Score([]));
        Equal(78, Diagnostics.Score([new("a", Severity.Critical, "", ""), new("b", Severity.Warning, "", "")]));
        Equal(0, Diagnostics.Score(Enumerable.Repeat(new Finding("c", Severity.Critical, "", ""), 10)));
    }),
};

var failures = 0;
foreach (var (name, run) in cases)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} passed");
return failures == 0 ? 0 : 1;

static void Has(string id, DiagnosticInputs i) => Require(Diagnostics.Evaluate(i).Any(f => f.Id == id), $"{id} não foi gerado.");
static void Lacks(string id, DiagnosticInputs i) => Require(!Diagnostics.Evaluate(i).Any(f => f.Id == id), $"{id} foi gerado indevidamente.");
static void Equal(object? e, object? a) { if (!Equals(e, a)) throw new Exception($"Esperado '{e}', obtido '{a}'."); }
static void Require(bool c, string m) { if (!c) throw new Exception(m); }
