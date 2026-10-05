using BoostParaPc.Services;
using Microsoft.Win32;
using System.Reflection;
using System.Text.Json.Nodes;

var cases = new (string Name, Func<Task> Run)[]
{
    ("High performance never duplicates Balanced", async () =>
    {
        await PowerPlanService.ActivateHighPerformanceAsync();
        Equal(PowerPlanService.UltimatePerformance, ProcessRunner.DuplicateTemplates.Single());
    }),
    ("A duplicated plan restores the original and removes only the created plan", async () =>
    {
        await PowerPlanService.ActivateHighPerformanceAsync();
        var created = ProcessRunner.ActivePlan;
        Require(created != ProcessRunner.OriginalPlan, "New plan was not activated.");
        await Call("RevertAllAsync");
        Equal(ProcessRunner.OriginalPlan, ProcessRunner.ActivePlan);
        Require(!ProcessRunner.Plans.Contains(created), "Created plan leaked after revert.");
    }),
    ("Power mutations have a durable journal before execution", async () =>
    {
        ProcessRunner.BeforeMutation = _ => Require(Pending().Length > 0, "Missing journal before mutation.");
        await PowerPlanService.ActivateHighPerformanceAsync();
    }),
    ("Power indices round trip both AC and DC on the original plan", async () =>
    {
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true);
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 1u, false);
        Equal((0u, 1u), ProcessRunner.Values["sub_sleep standbyidle"]);
        await Call("RevertAllAsync");
        Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        Equal(ProcessRunner.OriginalPlan, ProcessRunner.ActivePlan);
        Equal(0, Pending().Length);
    }),
    ("Unparseable power state refuses mutation", async () =>
    {
        ProcessRunner.InvalidPowerQuery = true;
        await Throws(() => Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true));
        Equal(0, ProcessRunner.Mutations.Count);
    }),
    ("Sleep and processor helpers restore their exact previous values", async () =>
    {
        await PowerPlanService.DisableSleepTimeoutsAsync();
        await PowerPlanService.SetGpuPreferenceAsync();
        Equal((0u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        Equal((100u, 100u), ProcessRunner.Values["sub_processor PROCTHROTTLEMAX"]);
        await Call("RevertAllAsync");
        Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        Equal((3600u, 1800u), ProcessRunner.Values["sub_sleep hibernateidle"]);
        Equal((900u, 300u), ProcessRunner.Values["sub_video videoidle"]);
        Equal((75u, 50u), ProcessRunner.Values["sub_processor PROCTHROTTLEMAX"]);
    }),
    ("Service restores delayed automatic start and running state", async () =>
    {
        SeedService(2, 1);
        await Call("DisableServiceAsync", "TestSvc");
        Equal(false, ProcessRunner.ServiceRunning); Equal(4, Read(ProcessRunner.ServicePath, "Start"));
        await Call("RevertAllAsync");
        Equal(true, ProcessRunner.ServiceRunning); Equal(2, Read(ProcessRunner.ServicePath, "Start"));
        Equal(1, Read(ProcessRunner.ServicePath, "DelayedAutoStart"));
    }),
    ("Service restores a stopped manual service and absent delayed marker", async () =>
    {
        SeedService(3, null); ProcessRunner.ServiceRunning = false;
        await Call("DisableServiceAsync", "TestSvc");
        await Call("RevertAllAsync");
        Equal(false, ProcessRunner.ServiceRunning); Equal(3, Read(ProcessRunner.ServicePath, "Start"));
        Equal(null, Read(ProcessRunner.ServicePath, "DelayedAutoStart"));
    }),
    ("Unknown service state refuses mutation", async () =>
    {
        SeedService(2, 1); ProcessRunner.InvalidServiceRead = true;
        await Throws(() => Call("DisableServiceAsync", "TestSvc"));
        Equal(0, ProcessRunner.Mutations.Count);
    }),
    ("Hibernation restores reduced file type", async () =>
    {
        using var key = Registry.LocalMachine.CreateSubKey(ProcessRunner.HibernatePath);
        key.SetValue("HibernateEnabled", 1, RegistryValueKind.DWord);
        key.SetValue("HiberFileType", 1, RegistryValueKind.DWord);
        await Call("SetHibernationAsync", false);
        Equal(0, Read(ProcessRunner.HibernatePath, "HibernateEnabled"));
        await Call("RevertAllAsync");
        Equal(1, Read(ProcessRunner.HibernatePath, "HibernateEnabled"));
        Equal(1, Read(ProcessRunner.HibernatePath, "HiberFileType"));
    }),
    ("TCP restores each profile's distinct original level", async () =>
    {
        // O catálogo usa minúsculas; a API normaliza para o valor aceito pelo Windows.
        await Call("SetTcpAutoTuningAsync", "normal");
        Equal("Normal", ProcessRunner.Tcp["Internet"]); Equal("Normal", ProcessRunner.Tcp["InternetCustom"]);
        await Call("RevertAllAsync");
        Equal("Restricted", ProcessRunner.Tcp["Internet"]); Equal("Disabled", ProcessRunner.Tcp["InternetCustom"]);
    }),
    ("Telemetry reversion enables only tasks that were originally enabled", async () =>
    {
        await Call("DisableTelemetryTasksAsync");
        Equal(false, ProcessRunner.Tasks["Consolidator"]); Equal(false, ProcessRunner.Tasks["UsbCeip"]);
        Equal(1, Pending().Length);
        await Call("RevertAllAsync");
        Equal(true, ProcessRunner.Tasks["Consolidator"]); Equal(false, ProcessRunner.Tasks["UsbCeip"]);
    }),
    ("Scheduled startup task is disabled per owner and restored with its path", async () =>
    {
        ProcessRunner.Tasks["MyUpdater"] = true;
        await Call("DisableScheduledTaskAsync", @"\Vendor\", "MyUpdater", "startup.task.MyUpdater");
        Equal(false, ProcessRunner.Tasks["MyUpdater"]);
        Require(ProcessRunner.Mutations.Any(m => m.Contains(@"-TaskPath '\Vendor\'")), "Task path was not passed to Disable-ScheduledTask.");
        await Call("RevertAsync", "other.owner");
        Equal(false, ProcessRunner.Tasks["MyUpdater"]);
        await Call("RevertAsync", "startup.task.MyUpdater");
        Equal(true, ProcessRunner.Tasks["MyUpdater"]);
    }),
    ("Scheduled task path and name injection is rejected before any mutation", async () =>
    {
        ProcessRunner.Tasks["MyUpdater"] = true;
        await Throws(() => Call("DisableScheduledTaskAsync", @"\Vendor'; Stop-Computer; '\", "MyUpdater"));
        await Throws(() => Call("DisableScheduledTaskAsync", @"\Vendor\", "x'; Stop-Computer; '"));
        Equal(0, ProcessRunner.Mutations.Count);
    }),
    ("DNS change is journaled and reverts to automatic or to the previous static servers", async () =>
    {
        await Call("SetDnsAsync", 7, new[] { "1.1.1.1", "1.0.0.1" }, "tools.dns");
        Equal("1.1.1.1,1.0.0.1", string.Join(",", ProcessRunner.DnsServers)); Equal(true, ProcessRunner.DnsStatic);
        await Call("RevertAsync", "tools.dns");
        Equal(false, ProcessRunner.DnsStatic);
        ProcessRunner.DnsServers = ["8.8.8.8"]; ProcessRunner.DnsStatic = true;
        await Call("SetDnsAsync", 7, new[] { "9.9.9.9" }, "tools.dns");
        await Call("RevertAsync", "tools.dns");
        Equal("8.8.8.8", string.Join(",", ProcessRunner.DnsServers)); Equal(true, ProcessRunner.DnsStatic);
    }),
    ("DNS server addresses are validated before any mutation", async () =>
    {
        await Throws(() => Call("SetDnsAsync", 7, new[] { "1.1.1.1; Stop-Computer" }, null!));
        Equal(0, ProcessRunner.Mutations.Count);
    }),
    ("Failed restore preserves pending journals and stops before older entries", async () =>
    {
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true);
        await Call("SetPowerValueAsync", "sub_video", "videoidle", 0u, true);
        ProcessRunner.FailContains = "videoidle";
        await Throws(() => Call("RevertAllAsync"));
        Equal(2, Pending().Length); Equal((0u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        ProcessRunner.FailContains = null;
        await Call("RevertAllAsync");
        Equal(0, Pending().Length); Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
    }),
    ("Failed apply retains its recovery journal", async () =>
    {
        ProcessRunner.FailContains = "/setacvalueindex";
        await Throws(() => Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true));
        Equal(1, Pending().Length);
    }),
    ("Unwritable journal prevents every mutation", async () =>
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        File.WriteAllText(Path.Combine(AppPaths.BackupDir, "commands"), "blocks directory");
        await Throws(() => Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true));
        Equal(0, ProcessRunner.Mutations.Count);
    }),
    ("Localized powercfg text does not block power changes", async () =>
    {
        ProcessRunner.LocalizedPowerQuery = true;
        await Call("SetPowerValueAsync", "sub_sleep", "standbyidle", 0u, true);
        Equal((0u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
        await Call("RevertAllAsync");
        Equal((1200u, 600u), ProcessRunner.Values["sub_sleep standbyidle"]);
    }),
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
    ("Journal data cannot inject a service command", async () =>
    {
        SeedService(2, 1);
        await Call("DisableServiceAsync", "TestSvc");
        var path = Pending().Single();
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Service"]!["Name"] = "TestSvc'; Stop-Computer; #";
        File.WriteAllText(path, json.ToJsonString());
        var before = ProcessRunner.Mutations.Count;
        await Throws(() => Call("RevertAllAsync"));
        Equal(before, ProcessRunner.Mutations.Count); Equal(1, Pending().Length);
    })
};
var root = Path.Combine(Path.GetTempPath(), "BoostSystemSettingsTests", Guid.NewGuid().ToString("N"));
var failures = 0;
for (var i = 0; i < cases.Length; i++)
{
    RegistryKey.Reset(); ProcessRunner.Reset();
    AppPaths.DataDir = Path.Combine(root, i.ToString()); Directory.CreateDirectory(AppPaths.DataDir);
    try { await cases[i].Run(); Console.WriteLine("PASS " + cases[i].Name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + cases[i].Name + ": " + e.Message); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} passed; no Windows commands were executed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

static Task Call(string name, params object[] args)
{
    var type = typeof(PowerPlanService).Assembly.GetType("BoostParaPc.Services.SystemSettingsBackupService")
        ?? throw new Exception("System settings journal has not been implemented.");
    var method = type.GetMethod(name) ?? throw new Exception($"Method {name} is missing.");
    var full = args.Concat(Enumerable.Repeat(Type.Missing, method.GetParameters().Length - args.Length)).ToArray();
    try { return (Task)method.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod, null, full, null)!; }
    catch (TargetInvocationException error) { throw error.InnerException!; }
}
static string[] Pending()
{
    var dir = Path.Combine(AppPaths.BackupDir, "commands");
    return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : [];
}
static object? Read(string path, string name)
{ using var key = Registry.LocalMachine.OpenSubKey(path); return key?.GetValue(name); }
static void SeedService(int start, int? delayed)
{
    using var key = Registry.LocalMachine.CreateSubKey(ProcessRunner.ServicePath);
    key.SetValue("Start", start, RegistryValueKind.DWord);
    if (delayed is not null) key.SetValue("DelayedAutoStart", delayed.Value, RegistryValueKind.DWord);
}
static void Equal(object? expected, object? actual)
{ if (!Equals(expected, actual)) throw new Exception($"Expected {expected ?? "<missing>"}, got {actual ?? "<missing>"}."); }
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Throws(Func<Task> action)
{ try { await action(); } catch { return; } throw new Exception("Expected failure, operation reported success."); }
