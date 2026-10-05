namespace BoostParaPc.Services;

/// <summary>Inicia o programa com o Windows por uma tarefa agendada com privilégio máximo. Uma chave Run não serve: o programa exige administrador e pediria UAC a cada logon.</summary>
public static class StartupTaskService
{
    public const string TaskName = "ProjectBoostX";

    public static string BuildCreateArguments(string exePath) =>
        $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --tray\" /sc onlogon /rl highest /f";

    public static Task EnableAsync(string? exePath = null) =>
        ProcessRunner.RunCheckedAsync("schtasks.exe", BuildCreateArguments(exePath ?? Environment.ProcessPath!), 15_000, elevated: true);

    public static Task DisableAsync() =>
        ProcessRunner.RunCheckedAsync("schtasks.exe", $"/delete /tn \"{TaskName}\" /f", 15_000, elevated: true);
}
