using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Journal durável para mudanças que não são representadas por um backup de Registro simples.
/// Cada operação grava o estado observado antes de alterar o Windows. O journal só é movido
/// para <c>restored</c> depois que toda a reversão da operação termina sem erro.
/// </summary>
public static class SystemSettingsBackupService
{
    private const string JournalVersion = "1";
    private static readonly object Gate = new();
    private static long _lastJournalTimestamp;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex SafeName = new(@"\A[A-Za-z0-9_.-]+\z", RegexOptions.Compiled);

    private sealed record Journal(string Version, DateTime CreatedUtc, string Kind,
        PowerValueState? PowerValue = null, PlanState? Plan = null, ServiceState? Service = null,
        HibernationState? Hibernation = null, TcpState? Tcp = null, TasksState? Tasks = null,
        string? Owner = null, TaskState? Task = null, DnsState? Dns = null);
    private sealed record PowerValueState(string Plan, string Subgroup, string Setting, uint Ac, uint Dc);
    private sealed record PlanState(string OriginalPlan, string TargetPlan, string? CreatedPlan);
    private sealed record ServiceState(string Name, int Start, int? DelayedAutoStart, bool Running);
    private sealed record HibernationState(int? HibernateEnabled, int? HiberFileType, int? HiberFileSizePercent);
    private sealed record TcpState(Dictionary<string, string> Profiles);
    private sealed record TasksState(Dictionary<string, bool> Enabled);
    private sealed record DnsState(int InterfaceIndex, string[] Servers, bool Static);
    private sealed record TaskState(string Path, string Name, bool Enabled);

    private static string JournalDir => Path.Combine(AppPaths.BackupDir, "commands");

    public static async Task SetPowerValueAsync(string subgroup, string setting, uint value, bool ac = true, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        ValidatePowerToken(subgroup, nameof(subgroup));
        ValidatePowerToken(setting, nameof(setting));
        var plan = await GetActivePlanAsync(cancellationToken).ConfigureAwait(false);
        var state = await QueryPowerValueAsync(plan, subgroup, setting, cancellationToken).ConfigureAwait(false);
        var journal = WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "PowerValue",
            PowerValue: new(plan, subgroup, setting, state.Ac, state.Dc), Owner: owner));
        try
        {
            var verb = ac ? "/setacvalueindex" : "/setdcvalueindex";
            await ProcessRunner.RunCheckedAsync("powercfg.exe", $"{verb} {plan} {subgroup} {setting} {value}",
                timeoutMs: 8_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch { _ = journal; throw; }
    }

    public static async Task ActivatePowerPlanAsync(string requestedPlan, bool duplicate = false, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        ValidateGuid(requestedPlan, nameof(requestedPlan));
        var original = await GetActivePlanAsync(cancellationToken).ConfigureAwait(false);
        string target = requestedPlan;
        string? created = null;
        if (duplicate)
        {
            created = Guid.NewGuid().ToString();
            var journal = WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Plan",
                Plan: new(original, created, created), Owner: owner));
            try
            {
                var result = await ProcessRunner.RunCheckedAsync("powercfg.exe",
                    $"/duplicatescheme {requestedPlan} {created}", timeoutMs: 8_000,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var outputGuid = ExtractGuid(result.StdOut);
                if (outputGuid is not null && !string.Equals(outputGuid, created, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O powercfg retornou um GUID diferente do plano solicitado.");
                target = created;
            }
            catch { _ = journal; throw; }
        }
        else
        {
            WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Plan",
                Plan: new(original, target, null), Owner: owner));
        }

        await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/setactive {target}", timeoutMs: 8_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task DisableServiceAsync(string name, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        ValidateServiceName(name);
        var path = $@"SYSTEM\CurrentControlSet\Services\{name}";
        var (start, delayed) = ReadServiceConfiguration(path);
        var status = await ProcessRunner.RunPowerShellAsync(
            $"$s = Get-Service -Name '{EscapePowerShell(name)}' -ErrorAction Stop; $s.Status.ToString()",
            timeoutMs: 10_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (status is not ("Running" or "Stopped"))
            throw new InvalidDataException($"Estado do serviço {name} não pôde ser confirmado: {status}");

        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Service",
            Service: new(name, start, delayed, status == "Running"), Owner: owner));
        await ProcessRunner.RunCheckedAsync("sc.exe", $"config \"{name}\" start= disabled", timeoutMs: 8_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await ProcessRunner.RunPowerShellAsync(
            $"$s = Get-Service -Name '{EscapePowerShell(name)}' -ErrorAction Stop; if ($s.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {{ $s.Stop(); $s.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped) }}",
            timeoutMs: 30_000, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task SetHibernationAsync(bool enabled, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        var path = @"SYSTEM\CurrentControlSet\Control\Power";
        var state = new HibernationState(ReadDword(Registry.LocalMachine, path, "HibernateEnabled"),
            ReadDword(Registry.LocalMachine, path, "HiberFileType"),
            ReadDword(Registry.LocalMachine, path, "HiberFileSizePercent"));
        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Hibernation", Hibernation: state, Owner: owner));
        await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/hibernate {(enabled ? "on" : "off")}",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task SetTcpAutoTuningAsync(string level, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        level = NormalizeTcpLevel(level);
        var output = await ProcessRunner.RunPowerShellAsync(
            "Get-NetTCPSetting | Select-Object SettingName,AutoTuningLevelLocal | ConvertTo-Json -Compress",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        var profiles = ParseTcpProfiles(output);
        if (profiles.Count == 0) throw new InvalidDataException("Nenhum perfil TCP válido foi retornado.");
        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Tcp", Tcp: new(profiles), Owner: owner));
        foreach (var name in profiles.Keys)
        {
            ValidateServiceName(name);
            await ProcessRunner.RunPowerShellAsync(
                $"Set-NetTCPSetting -SettingName '{EscapePowerShell(name)}' -AutoTuningLevelLocal '{EscapePowerShell(level)}'",
                timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task DisableTelemetryTasksAsync(string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        var output = await ProcessRunner.RunPowerShellAsync(
            "Get-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Customer Experience Improvement Program\\' | Select-Object TaskName,State | ConvertTo-Json -Compress",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        var tasks = ParseTaskStates(output);
        if (tasks.Count == 0) return;
        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Tasks", Tasks: new(tasks), Owner: owner));
        foreach (var task in tasks.Where(p => p.Value).Select(p => p.Key))
        {
            ValidateServiceName(task);
            await ProcessRunner.RunPowerShellAsync(
                $"Disable-ScheduledTask -TaskName '{EscapePowerShell(task)}' -ErrorAction Stop | Out-Null",
                timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Desativa uma tarefa agendada guardando o estado anterior (e o caminho dela) no journal.</summary>
    public static async Task DisableScheduledTaskAsync(string taskPath, string taskName, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        ValidateTaskPath(taskPath); ValidateServiceName(taskName);
        var output = await ProcessRunner.RunPowerShellAsync(
            $"Get-ScheduledTask -TaskPath '{EscapePowerShell(taskPath)}' -TaskName '{EscapePowerShell(taskName)}' | Select-Object TaskName,State | ConvertTo-Json -Compress",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!ParseTaskStates(output).TryGetValue(taskName, out var enabled))
            throw new InvalidDataException("A tarefa agendada não foi encontrada.");
        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Task", Task: new(taskPath, taskName, enabled), Owner: owner));
        await ProcessRunner.RunPowerShellAsync(
            $"Disable-ScheduledTask -TaskPath '{EscapePowerShell(taskPath)}' -TaskName '{EscapePowerShell(taskName)}' -ErrorAction Stop | Out-Null",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Troca o DNS IPv4 de uma interface. O journal guarda os endereços anteriores ou "automático".</summary>
    public static async Task SetDnsAsync(int interfaceIndex, string[] servers, string? owner = null)
    {
        var cancellationToken = CancellationToken.None;
        ValidateDnsServers(servers);
        var output = await ProcessRunner.RunPowerShellAsync(
            $"$g = (Get-NetAdapter -InterfaceIndex {interfaceIndex}).InterfaceGuid; " +
            "$ns = (Get-ItemProperty -Path ('HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\' + $g) -Name NameServer -ErrorAction SilentlyContinue).NameServer; " +
            $"[pscustomobject]@{{ Servers = @((Get-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -AddressFamily IPv4).ServerAddresses); Static = [bool]$ns }} | ConvertTo-Json -Compress",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(output);
        var previous = doc.RootElement.TryGetProperty("Servers", out var list)
            ? (list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(e => e.GetString()!).ToArray() : [list.GetString()!])
            : [];
        var isStatic = doc.RootElement.TryGetProperty("Static", out var st) && st.ValueKind == JsonValueKind.True;
        ValidateDnsServers(previous);
        WriteJournal(new Journal(JournalVersion, DateTime.UtcNow, "Dns", Dns: new(interfaceIndex, previous, isStatic), Owner: owner));
        await ProcessRunner.RunPowerShellAsync(
            $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses {string.Join(",", servers)}",
            timeoutMs: 15_000, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

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

    private static async Task RestoreJournalAsync(Journal journal, CancellationToken ct)
    {
        if (journal.Version != JournalVersion) throw new InvalidDataException("Versão de journal não suportada.");
        switch (journal.Kind)
        {
            case "PowerValue":
            {
                var s = journal.PowerValue ?? throw new InvalidDataException("Journal de energia incompleto.");
                ValidatePowerToken(s.Plan, nameof(s.Plan)); ValidatePowerToken(s.Subgroup, nameof(s.Subgroup)); ValidatePowerToken(s.Setting, nameof(s.Setting));
                await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/setacvalueindex {s.Plan} {s.Subgroup} {s.Setting} {s.Ac}", 8_000, cancellationToken: ct).ConfigureAwait(false);
                await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/setdcvalueindex {s.Plan} {s.Subgroup} {s.Setting} {s.Dc}", 8_000, cancellationToken: ct).ConfigureAwait(false);
                break;
            }
            case "Plan":
            {
                var s = journal.Plan ?? throw new InvalidDataException("Journal de plano incompleto.");
                ValidateGuid(s.OriginalPlan, nameof(s.OriginalPlan)); ValidateGuid(s.TargetPlan, nameof(s.TargetPlan));
                await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/setactive {s.OriginalPlan}", 8_000, cancellationToken: ct).ConfigureAwait(false);
                if (s.CreatedPlan is not null)
                {
                    ValidateGuid(s.CreatedPlan, nameof(s.CreatedPlan));
                    await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/delete {s.CreatedPlan}", 8_000, cancellationToken: ct).ConfigureAwait(false);
                }
                break;
            }
            case "Service":
            {
                var s = journal.Service ?? throw new InvalidDataException("Journal de serviço incompleto.");
                ValidateServiceName(s.Name);
                var start = s.DelayedAutoStart == 1 && s.Start == 2 ? "delayed-auto" : s.Start switch
                {
                    0 => "boot", 1 => "system", 2 => "auto", 3 => "demand", 4 => "disabled",
                    _ => throw new InvalidDataException("Tipo de serviço inválido")
                };
                await ProcessRunner.RunCheckedAsync("sc.exe", $"config \"{s.Name}\" start= {start}", 8_000, cancellationToken: ct).ConfigureAwait(false);
                var path = $@"SYSTEM\CurrentControlSet\Services\{s.Name}";
                using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
                if (s.DelayedAutoStart is int delayed) key?.SetValue("DelayedAutoStart", delayed, RegistryValueKind.DWord);
                else key?.DeleteValue("DelayedAutoStart", false);
                if (s.Running)
                    await ProcessRunner.RunPowerShellAsync($"$s = Get-Service -Name '{EscapePowerShell(s.Name)}'; if ($s.Status.ToString() -ne 'Running') {{ $s.Start(); $s.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running) }}", 30_000, ct).ConfigureAwait(false);
                break;
            }
            case "Hibernation":
            {
                var s = journal.Hibernation ?? throw new InvalidDataException("Journal de hibernação incompleto.");
                await ProcessRunner.RunCheckedAsync("powercfg.exe", $"/hibernate {(s.HibernateEnabled == 1 ? "on" : "off")}", 15_000, cancellationToken: ct).ConfigureAwait(false);
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power", writable: true);
                RestoreDword(key, "HibernateEnabled", s.HibernateEnabled); RestoreDword(key, "HiberFileType", s.HiberFileType); RestoreDword(key, "HiberFileSizePercent", s.HiberFileSizePercent);
                break;
            }
            case "Tcp":
            {
                var s = journal.Tcp ?? throw new InvalidDataException("Journal TCP incompleto.");
                foreach (var pair in s.Profiles)
                {
                    ValidateServiceName(pair.Key); var level = NormalizeTcpLevel(pair.Value);
                    await ProcessRunner.RunPowerShellAsync($"Set-NetTCPSetting -SettingName '{EscapePowerShell(pair.Key)}' -AutoTuningLevelLocal '{EscapePowerShell(level)}'", 15_000, ct).ConfigureAwait(false);
                }
                break;
            }
            case "Tasks":
            {
                var s = journal.Tasks ?? throw new InvalidDataException("Journal de tarefas incompleto.");
                foreach (var pair in s.Enabled)
                {
                    ValidateServiceName(pair.Key);
                    var command = pair.Value ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
                    await ProcessRunner.RunPowerShellAsync($"{command} -TaskName '{EscapePowerShell(pair.Key)}' -ErrorAction Stop | Out-Null", 15_000, ct).ConfigureAwait(false);
                }
                break;
            }
            case "Task":
            {
                var s = journal.Task ?? throw new InvalidDataException("Journal de tarefa incompleto.");
                ValidateTaskPath(s.Path); ValidateServiceName(s.Name);
                var command = s.Enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
                await ProcessRunner.RunPowerShellAsync($"{command} -TaskPath '{EscapePowerShell(s.Path)}' -TaskName '{EscapePowerShell(s.Name)}' -ErrorAction Stop | Out-Null", 15_000, ct).ConfigureAwait(false);
                break;
            }
            case "Dns":
            {
                var s = journal.Dns ?? throw new InvalidDataException("Journal de DNS incompleto.");
                ValidateDnsServers(s.Servers);
                var script = s.Static && s.Servers.Length > 0
                    ? $"Set-DnsClientServerAddress -InterfaceIndex {s.InterfaceIndex} -ServerAddresses {string.Join(",", s.Servers)}"
                    : $"Set-DnsClientServerAddress -InterfaceIndex {s.InterfaceIndex} -ResetServerAddresses";
                await ProcessRunner.RunPowerShellAsync(script, 15_000, ct).ConfigureAwait(false);
                break;
            }
            default: throw new InvalidDataException($"Tipo de journal desconhecido: {journal.Kind}");
        }
    }

    private static string WriteJournal(Journal journal)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(JournalDir);
            var ticks = Math.Max(DateTime.UtcNow.Ticks, _lastJournalTimestamp + 1);
            _lastJournalTimestamp = ticks;
            var file = Path.Combine(JournalDir, $"{ticks}_{Guid.NewGuid():N}.json");
            var temp = file + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, journal, JsonOptions); stream.Flush(true);
            }
            File.Move(temp, file);
            return file;
        }
    }
    private static Journal ReadJournal(string file)
        => JsonSerializer.Deserialize<Journal>(File.ReadAllText(file)) ?? throw new InvalidDataException("Journal vazio.");
    private static IReadOnlyList<string> GetPendingJournals() => Directory.GetFiles(JournalDir, "*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
    private static void ArchiveJournal(string file)
    {
        var dir = Path.Combine(JournalDir, "restored"); Directory.CreateDirectory(dir);
        File.Move(file, Path.Combine(dir, Path.GetFileName(file) + "." + Guid.NewGuid().ToString("N")));
    }
    private static void ValidateTaskPath(string path) { if (!Regex.IsMatch(path, @"\A\\([A-Za-z0-9 _.-]+\\)*\z")) throw new InvalidDataException("Caminho de tarefa inválido."); }
    private static void ValidateDnsServers(IEnumerable<string> servers) { foreach (var s in servers) if (!System.Net.IPAddress.TryParse(s, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) throw new InvalidDataException("Endereço DNS inválido."); }
    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static void ValidateServiceName(string name) { if (!SafeName.IsMatch(name)) throw new InvalidDataException("Nome de serviço/tarefa inválido."); }
    private static void ValidatePowerToken(string value, string parameter) { if (!Regex.IsMatch(value, @"\A[A-Za-z0-9_.-]+\z")) throw new ArgumentException("Token powercfg inválido", parameter); }
    private static void ValidateGuid(string value, string parameter) { if (!Guid.TryParse(value, out _)) throw new InvalidDataException($"GUID inválido em {parameter}."); }
    private static string NormalizeTcpLevel(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "normal" => "Normal",
            "restricted" => "Restricted",
            "highlyrestricted" => "HighlyRestricted",
            "experimental" => "Experimental",
            "disabled" => "Disabled",
            _ => throw new ArgumentException("Nível TCP inválido", nameof(value))
        };
    }
    private static string? ExtractGuid(string text) { var m = Regex.Match(text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"); return m.Success ? m.Value : null; }

    private static async Task<string> GetActivePlanAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunCheckedAsync("powercfg.exe", "/getactivescheme", 8_000, cancellationToken: ct).ConfigureAwait(false);
        var guid = ExtractGuid(result.StdOut); if (guid is null) throw new InvalidDataException("Não foi possível identificar o plano de energia ativo."); return guid;
    }
    private static Task<(uint Ac, uint Dc)> QueryPowerValueAsync(string plan, string subgroup, string setting, CancellationToken ct)
    {
        // Leitura nativa: o texto do powercfg é traduzido pelo Windows e omite ajustes ocultos.
        var ac = PowerNative.ReadAc(subgroup, setting, plan);
        var dc = PowerNative.ReadDc(subgroup, setting, plan);
        if (ac is null || dc is null)
            throw new InvalidDataException("Este ajuste de energia não existe ou não pôde ser lido neste PC.");
        return Task.FromResult((ac.Value, dc.Value));
    }
    private static (int Start, int? Delayed) ReadServiceConfiguration(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path, false) ?? throw new InvalidDataException("Configuração do serviço não encontrada.");
        if (key.GetValue("Start") is not int start || start is < 0 or > 4) throw new InvalidDataException("Tipo de inicialização inválido.");
        int? delayed = key.GetValue("DelayedAutoStart") is int i ? i : null;
        return (start, delayed);
    }
    private static int? ReadDword(RegistryKey root, string path, string name) { using var key = root.OpenSubKey(path, false); return key?.GetValue(name) is int i ? i : null; }
    private static void RestoreDword(RegistryKey? key, string name, int? value) { if (key is null) throw new IOException("Chave de energia inacessível."); if (value is int i) key.SetValue(name, i, RegistryValueKind.DWord); else key.DeleteValue(name, false); }
    private static Dictionary<string, string> ParseTcpProfiles(string json)
    {
        using var doc = JsonDocument.Parse(json); var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement }.AsEnumerable(); var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list) { var name = item.TryGetProperty("SettingName", out var n) ? n.GetString() : null; var level = item.TryGetProperty("AutoTuningLevelLocal", out var l) ? l.GetString() : null; if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(level)) { ValidateServiceName(name); result[name] = NormalizeTcpLevel(level); } }
        return result;
    }
    private static Dictionary<string, bool> ParseTaskStates(string json)
    {
        using var doc = JsonDocument.Parse(json); var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement }.AsEnumerable(); var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list) { var name = item.TryGetProperty("TaskName", out var n) ? n.GetString() : null; var enabled = item.TryGetProperty("Enabled", out var e) ? e.ValueKind == JsonValueKind.True : item.TryGetProperty("State", out var s) && !string.Equals(s.GetString(), "Disabled", StringComparison.OrdinalIgnoreCase); if (!string.IsNullOrWhiteSpace(name)) { ValidateServiceName(name); result[name] = enabled; } }
        return result;
    }
}
