using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoostParaPc.Services;

// Models the external command boundary. No process is started by these tests.
public static class ProcessRunner
{
    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    { public bool Success => ExitCode == 0 && !TimedOut; }
    public const string OriginalPlan = "11111111-1111-4111-8111-111111111111";
    public const string ServicePath = @"SYSTEM\CurrentControlSet\Services\TestSvc";
    public const string HibernatePath = @"SYSTEM\CurrentControlSet\Control\Power";
    public static string ActivePlan { get; set; } = OriginalPlan;
    public static HashSet<string> Plans { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, (uint Ac, uint Dc)> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> Tcp { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, bool> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static bool ServiceRunning { get; set; }
    public static string[] DnsServers { get; set; } = ["192.168.0.1"];
    public static bool DnsStatic { get; set; }
    public static string? FailContains { get; set; }
    public static bool InvalidPowerQuery { get; set; }
    public static bool LocalizedPowerQuery { get; set; }
    public static bool InvalidServiceRead { get; set; }
    public static Action<string>? BeforeMutation { get; set; }
    public static List<string> Mutations { get; } = [];
    public static List<string> DuplicateTemplates { get; } = [];

    public static void Reset()
    {
        ActivePlan = OriginalPlan;
        Plans.Clear(); Plans.Add(OriginalPlan);
        Values.Clear(); Values["sub_sleep standbyidle"] = (1200, 600);
        Values["sub_sleep hibernateidle"] = (3600, 1800);
        Values["sub_video videoidle"] = (900, 300);
        Values["sub_processor PROCTHROTTLEMAX"] = (75, 50);
        Tcp.Clear(); Tcp["Internet"] = "Restricted"; Tcp["InternetCustom"] = "Disabled";
        Tasks.Clear(); Tasks["Consolidator"] = true; Tasks["UsbCeip"] = false;
        DnsServers = ["192.168.0.1"]; DnsStatic = false;
        ServiceRunning = true; FailContains = null; InvalidPowerQuery = false; InvalidServiceRead = false;
        LocalizedPowerQuery = false;
        BeforeMutation = null; Mutations.Clear(); DuplicateTemplates.Clear();
    }

    public static async Task<CommandResult> RunAsync(string fileName, string arguments, int timeoutMs = 30_000,
        bool elevated = false, CancellationToken cancellationToken = default)
    {
        try { return await RunCheckedAsync(fileName, arguments, timeoutMs, elevated, cancellationToken); }
        catch (Exception e) { return new(1, "", e.Message, false); }
    }

    public static Task<CommandResult> RunCheckedAsync(string fileName, string arguments, int timeoutMs = 30_000,
        bool elevated = false, CancellationToken cancellationToken = default)
    {
        var p = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fileName.StartsWith("powercfg", StringComparison.OrdinalIgnoreCase))
        {
            if (p[0] == "/getactivescheme") return Result($"Esquema de Energia: {ActivePlan} (Custom)");
            if (p[0] == "/list") return Result(string.Join('\n', Plans.Select(g => $"Power scheme GUID: {g}")));
            if (p[0] == "/query")
            {
                if (InvalidPowerQuery) return Result("query output missing indices");
                var v = Values[p[2] + " " + p[3]];
                return Result(LocalizedPowerQuery
                    ? $"Índice de Configurações de Correntes Alternadas Atuais: 0x{v.Ac:x8}\nÍndice de Configurações de Correntes Contínuas Atuais: 0x{v.Dc:x8}"
                    : $"Maximum: 0xffffffff\nCurrent AC: 0x{v.Ac:x8}\nCurrent DC: 0x{v.Dc:x8}");
            }
            Mutate(arguments);
            if (p[0] == "/setactive")
            {
                if (!Plans.Contains(p[1])) throw new InvalidOperationException("Plan not installed.");
                ActivePlan = p[1];
            }
            else if (p[0] == "/duplicatescheme")
            {
                DuplicateTemplates.Add(p[1]);
                var target = p.Length > 2 ? p[2] : Guid.NewGuid().ToString();
                Plans.Add(target); return Result(target);
            }
            else if (p[0] == "/delete") Plans.Remove(p[1]);
            else if (p[0] is "/setacvalueindex" or "/setdcvalueindex")
            {
                var key = p[2] + " " + p[3]; var old = Values[key]; var next = uint.Parse(p[4]);
                Values[key] = p[0] == "/setacvalueindex" ? (next, old.Dc) : (old.Ac, next);
            }
            else if (p[0] is "/hibernate" or "/h")
            {
                using var key = Registry.LocalMachine.CreateSubKey(HibernatePath);
                if (p[1] is "on" or "off") key.SetValue("HibernateEnabled", p[1] == "on" ? 1 : 0, RegistryValueKind.DWord);
                else if (p[1] == "/type") key.SetValue("HiberFileType", p[2] == "reduced" ? 1 : 2, RegistryValueKind.DWord);
                else if (p[1] == "/size") key.SetValue("HiberFileSizePercent", int.Parse(p[2]), RegistryValueKind.DWord);
                else throw new InvalidOperationException("Unexpected hibernate command.");
            }
            else throw new InvalidOperationException("Unexpected power command: " + arguments);
        }
        else if (fileName == "sc.exe")
        {
            Mutate(arguments);
            using var key = Registry.LocalMachine.CreateSubKey(ServicePath);
            var start = p[^1];
            key.SetValue("Start", start is "auto" or "delayed-auto" ? 2 : start == "demand" ? 3 : 4, RegistryValueKind.DWord);
            key.SetValue("DelayedAutoStart", start == "delayed-auto" ? 1 : 0, RegistryValueKind.DWord);
        }
        else throw new InvalidOperationException("Unexpected process: " + fileName);
        return Result("");
    }

    public static Task<string> RunPowerShellAsync(string script, int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        if (script.Contains("Get-NetTCPSetting") && !script.Contains("Set-NetTCPSetting"))
            return Task.FromResult(JsonSerializer.Serialize(Tcp.Select(p => new { SettingName = p.Key, AutoTuningLevelLocal = p.Value })));
        if (script.Contains("Get-ScheduledTask") && !script.Contains("Enable-ScheduledTask") && !script.Contains("Disable-ScheduledTask"))
            return Task.FromResult(JsonSerializer.Serialize(Tasks.Select(p => new { TaskName = p.Key, Enabled = p.Value })));
        if (script.Contains("Get-Service") && !script.Contains("WaitForStatus"))
            return Task.FromResult(InvalidServiceRead ? "Unknown" : ServiceRunning ? "Running" : "Stopped");
        if (script.Contains("Get-DnsClientServerAddress") && !script.Contains("Set-DnsClientServerAddress"))
            return Task.FromResult(JsonSerializer.Serialize(new { Servers = DnsServers, Static = DnsStatic }));
        Mutate(script);
        if (script.Contains("Set-DnsClientServerAddress"))
        {
            if (script.Contains("-ResetServerAddresses")) { DnsServers = ["192.168.0.1"]; DnsStatic = false; }
            else { DnsServers = Regex.Match(script, @"-ServerAddresses (\S+)").Groups[1].Value.Split(','); DnsStatic = true; }
        }
        else if (script.Contains("Set-NetTCPSetting"))
        {
            var name = Regex.Match(script, "-SettingName '([^']+)'").Groups[1].Value;
            var level = Regex.Match(script, "-AutoTuningLevelLocal '?([A-Za-z]+)'?").Groups[1].Value;
            if (!Tcp.ContainsKey(name)) throw new InvalidOperationException("Unexpected TCP profile.");
            Tcp[name] = level;
        }
        else if (script.Contains("Enable-ScheduledTask") || script.Contains("Disable-ScheduledTask"))
        {
            var name = Regex.Match(script, "-TaskName '([^']+)'").Groups[1].Value;
            if (!Tasks.ContainsKey(name)) throw new InvalidOperationException("Missing task.");
            Tasks[name] = script.Contains("Enable-ScheduledTask");
        }
        else if (script.Contains("WaitForStatus"))
            ServiceRunning = script.Contains("'Running'");
        else throw new InvalidOperationException("Unexpected PowerShell script.");
        return Task.FromResult("");
    }

    private static void Mutate(string command)
    {
        BeforeMutation?.Invoke(command);
        if (FailContains is { } failure && command.Contains(failure, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Injected command failure.");
        Mutations.Add(command);
    }

    private static Task<CommandResult> Result(string output) => Task.FromResult(new CommandResult(0, output, "", false));
}

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
