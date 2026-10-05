using BoostParaPc.Services;
using Microsoft.Win32;

var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
const string Toast = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

void World()
{
    ProcessNative.Table[100] = new("game.exe", t0, ProcessNative.Normal);
    ProcessNative.Table[200] = new("OneDrive.exe", t0, ProcessNative.Normal);
    ProcessNative.Table[201] = new("Teams.exe", t0, ProcessNative.Idle);
    ProcessNative.Table[300] = new("Discord.exe", t0, ProcessNative.Normal);
    using var key = Registry.CurrentUser.CreateSubKey(Toast);
    key.SetValue("ToastEnabled", 1, RegistryValueKind.DWord);
}

string Snapshot() => string.Join(",", ProcessNative.Table.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.Priority}:{p.Value.Efficiency}"))
    + "|" + RegistryKey.Dump();

string[] apps = ["OneDrive.exe", "Teams.exe", "Discord.exe"];

var cases = new (string Name, Func<Task> Run)[]
{
    ("Sessão normal restaura prioridades, eficiência e notificações", async () =>
    {
        World();
        var before = Snapshot();
        await GameSessionService.StartAsync(100, "game.exe", apps);
        Equal(ProcessNative.High, ProcessNative.Table[100].Priority);
        Equal(ProcessNative.BelowNormal, ProcessNative.Table[200].Priority);
        Require(ProcessNative.Table[200].Efficiency, "Modo de eficiência não aplicado.");
        Equal(0, Registry.CurrentUser.OpenSubKey(Toast)!.GetValue("ToastEnabled"));
        Equal(1, MemoryNative.Purges);
        await GameSessionService.EndAsync();
        Equal(before, Snapshot());
        Require(!File.Exists(GameSessionService.SessionFile), "session.json não foi apagado.");
    }),
    ("Sessão interrompida é restaurada na abertura seguinte", async () =>
    {
        World();
        await GameSessionService.StartAsync(100, "game.exe", apps);
        ProcessNative.Table.Remove(100);                 // o jogo fechou enquanto o programa estava fechado
        Require(await GameSessionService.RecoverAsync(), "Sessão pendente não foi detectada.");
        Equal(ProcessNative.Normal, ProcessNative.Table[200].Priority);
        Equal(ProcessNative.Idle, ProcessNative.Table[201].Priority);
        Require(!ProcessNative.Table[200].Efficiency, "Eficiência não foi desfeita.");
        Equal(1, Registry.CurrentUser.OpenSubKey(Toast)!.GetValue("ToastEnabled"));
        Require(!File.Exists(GameSessionService.SessionFile), "session.json não foi apagado.");
    }),
    ("Recuperação não mexe se o jogo ainda está rodando", async () =>
    {
        World();
        await GameSessionService.StartAsync(100, "game.exe", apps);
        Require(!await GameSessionService.RecoverAsync(), "Restaurou uma sessão ativa.");
        Require(File.Exists(GameSessionService.SessionFile), "session.json foi apagado com o jogo rodando.");
    }),
    ("Pid reutilizado por outro processo não é tocado", async () =>
    {
        World();
        await GameSessionService.StartAsync(100, "game.exe", apps);
        ProcessNative.Table[200] = new("outro.exe", t0.AddHours(1), ProcessNative.High); // mesmo pid, outro processo
        ProcessNative.Touched.Clear();
        await GameSessionService.EndAsync();
        Require(!ProcessNative.Touched.Contains(200), "Tocou em um processo que reutilizou o pid.");
        Equal(ProcessNative.High, ProcessNative.Table[200].Priority);
    }),
    ("Processo protegido nunca é alterado, mesmo na lista do usuário", async () =>
    {
        World();
        await GameSessionService.StartAsync(100, "game.exe", apps);
        Equal(ProcessNative.Normal, ProcessNative.Table[300].Priority);
        Require(!ProcessNative.Table[300].Efficiency, "Discord entrou em modo de eficiência.");
        Require(GameSessionService.IsProtected("EasyAntiCheat.exe"), "Anti-cheat não é protegido.");
    }),
    ("Falha ao alterar um processo não impede os demais", async () =>
    {
        World();
        ProcessNative.Table[200].FailSet = true;
        await GameSessionService.StartAsync(100, "game.exe", apps);
        Equal(ProcessNative.BelowNormal, ProcessNative.Table[201].Priority);
        Equal(ProcessNative.High, ProcessNative.Table[100].Priority);
        ProcessNative.Table[200].FailSet = false;
        await GameSessionService.EndAsync();
        Equal(ProcessNative.Idle, ProcessNative.Table[201].Priority);
        Equal(ProcessNative.Normal, ProcessNative.Table[100].Priority);
    }),
    ("Notificações que não existiam voltam a não existir", async () =>
    {
        World();
        using (var key = Registry.CurrentUser.CreateSubKey(Toast)) key.DeleteValue("ToastEnabled");
        await GameSessionService.StartAsync(100, "game.exe", apps);
        await GameSessionService.EndAsync();
        Require(Registry.CurrentUser.OpenSubKey(Toast)!.GetValue("ToastEnabled") is null, "ToastEnabled deveria ter sido removido.");
    }),
};

var root = Path.Combine(Path.GetTempPath(), "BoostSessionTests", Guid.NewGuid().ToString("N"));
var failures = 0;
for (var i = 0; i < cases.Length; i++)
{
    RegistryKey.Reset();
    ProcessNative.Reset();
    MemoryNative.Purges = 0;
    AppPaths.DataDir = Path.Combine(root, i.ToString());
    Directory.CreateDirectory(AppPaths.DataDir);
    try { await cases[i].Run(); Console.WriteLine("PASS " + cases[i].Name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + cases[i].Name + ": " + e.Message); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} passed; nenhum processo real foi alterado.");
return failures == 0 ? 0 : 1;

static void Equal(object? e, object? a) { if (!Equals(e, a)) throw new Exception($"Esperado '{e}', obtido '{a}'."); }
static void Require(bool c, string m) { if (!c) throw new Exception(m); }
