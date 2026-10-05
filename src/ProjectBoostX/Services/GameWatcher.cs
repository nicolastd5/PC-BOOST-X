using System.Diagnostics;
using System.Management;

namespace BoostParaPc.Services;

/// <summary>Observa a abertura de processos e liga o Modo Jogo quando um jogo conhecido inicia. Exige administrador.</summary>
public sealed class GameWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private int _busy;

    public event Action<string>? Message;

    public void Start(Func<IReadOnlyCollection<string>> gameExecutables)
    {
        if (_watcher is not null) return;
        _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName, ProcessID FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += (_, e) =>
        {
            var name = e.NewEvent["ProcessName"]?.ToString();
            var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
            if (name is null || !gameExecutables().Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            _ = RunSessionAsync(pid, name);
        };
        _watcher.Start();
    }

    private async Task RunSessionAsync(int pid, string name)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;   // uma sessão por vez
        try
        {
            Message?.Invoke($"Modo Jogo ligado: {name}");
            await GameSessionService.StartAsync(pid, name, AppSettings.Current.BackgroundApps);
            try { using var game = Process.GetProcessById(pid); await game.WaitForExitAsync(); }
            catch (ArgumentException) { /* já fechou */ }
        }
        catch (Exception ex) { ActionLog.Write("session", "modo-jogo", false, ex.Message); }
        finally
        {
            await GameSessionService.EndAsync();
            Message?.Invoke("Modo Jogo desligado");
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        _watcher?.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }
}
