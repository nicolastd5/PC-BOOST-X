using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Modo Jogo: enquanto um jogo roda, dá prioridade a ele, põe apps de fundo em modo de eficiência e silencia notificações.
/// Toda alteração é gravada em session.json ANTES de ser feita, para poder ser desfeita mesmo se o programa fechar no meio.
/// </summary>
public static class GameSessionService
{
    private const string ToastPath = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    // Nunca tocados, mesmo que o usuário os adicione à lista.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Discord.exe", "obs64.exe", "obs32.exe", "audiodg.exe", "EasyAntiCheat.exe", "EasyAntiCheat_EOS.exe",
        "EasyAntiCheat_launcher.exe", "BEService.exe", "BEService_x64.exe", "vgc.exe", "vgtray.exe", "FACEITService.exe"
    };

    public sealed record Change(string Kind, int Pid, DateTime StartUtc, int Original, bool Efficiency, int? ToastOriginal);

    public sealed record Session(int GamePid, DateTime GameStartUtc, List<Change> Changes);

    public static string SessionFile => Path.Combine(AppPaths.DataDir, "session.json");

    public static bool IsProtected(string exeName) => Protected.Contains(exeName);

    public static Session? Load()
    {
        try { return File.Exists(SessionFile) ? JsonSerializer.Deserialize<Session>(File.ReadAllText(SessionFile)) : null; }
        catch { return null; }
    }

    private static void Save(Session session)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var temporary = SessionFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session));
        File.Move(temporary, SessionFile, overwrite: true);
    }

    public static Task StartAsync(int gamePid, string gameExe, IEnumerable<string> backgroundApps) => Task.Run(() =>
    {
        if (ProcessNative.StartTime(gamePid) is not { } gameStart) return;
        var session = new Session(gamePid, gameStart, []);
        Save(session);   // 1. antes de qualquer alteração

        // 2. jogo em prioridade Alta
        if (ProcessNative.GetPriority(gamePid) is { } gamePriority)
        {
            session.Changes.Add(new("priority", gamePid, gameStart, gamePriority, false, null));
            Save(session);
            ProcessNative.SetPriority(gamePid, ProcessNative.High);
        }

        // 3. apps de fundo: eficiência + prioridade abaixo do normal. Falha em um não impede os demais.
        foreach (var app in backgroundApps.Where(a => !IsProtected(a) && !a.Equals(gameExe, StringComparison.OrdinalIgnoreCase)))
            foreach (var pid in ProcessNative.FindByName(app))
            {
                if (pid == gamePid || ProcessNative.StartTime(pid) is not { } start || ProcessNative.GetPriority(pid) is not { } original) continue;
                session.Changes.Add(new("priority", pid, start, original, true, null));
                Save(session);
                ProcessNative.SetEfficiency(pid, true);
                ProcessNative.SetPriority(pid, ProcessNative.BelowNormal);
            }

        // 4. notificações
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ToastPath);
            var original = key.GetValue("ToastEnabled") as int?;
            session.Changes.Add(new("toast", 0, default, 0, false, original ?? -1));
            Save(session);
            key.SetValue("ToastEnabled", 0, RegistryValueKind.DWord);
        }
        catch { /* sem permissão: segue sem silenciar notificações */ }

        // 5. memória em espera
        try { MemoryNative.PurgeStandbyList(); } catch { /* precisa de administrador */ }
    });

    /// <summary>Desfaz tudo, na ordem inversa. Só toca em processo cujo pid e hora de início conferem.</summary>
    public static Task EndAsync() => Task.Run(() =>
    {
        if (Load() is not { } session) return;
        for (var i = session.Changes.Count - 1; i >= 0; i--)
        {
            var c = session.Changes[i];
            try
            {
                if (c.Kind == "toast")
                {
                    using var key = Registry.CurrentUser.CreateSubKey(ToastPath);
                    if (c.ToastOriginal is { } o and >= 0) key.SetValue("ToastEnabled", o, RegistryValueKind.DWord);
                    else key.DeleteValue("ToastEnabled", false);
                }
                else if (ProcessNative.StartTime(c.Pid) == c.StartUtc)
                {
                    if (c.Efficiency) ProcessNative.SetEfficiency(c.Pid, false);
                    ProcessNative.SetPriority(c.Pid, c.Original);
                }
            }
            catch { /* o restante continua sendo desfeito */ }
        }
        File.Delete(SessionFile);
    });

    /// <summary>Se o programa fechou no meio de uma sessão, restaura antes de qualquer outra coisa. Devolve true se havia sessão pendente.</summary>
    public static async Task<bool> RecoverAsync()
    {
        if (Load() is not { } session) return false;
        if (ProcessNative.StartTime(session.GamePid) == session.GameStartUtc) return false; // o jogo ainda roda
        await EndAsync();
        return true;
    }
}
