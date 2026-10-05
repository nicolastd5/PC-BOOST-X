using BoostParaPc.Services;
using Microsoft.Win32;

var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureRoot);
var failed = 0;
async Task Test(string name, Func<Task> body)
{
    AppPaths.Root = Path.Combine(fixtureRoot, name);
    Directory.CreateDirectory(AppPaths.Root);
    Registry.CurrentUser = new("HKEY_CURRENT_USER");
    Registry.LocalMachine = new("HKEY_LOCAL_MACHINE");
    ProcessRunner.Calls.Clear();
    OptimizationStateStore.Cleared = false;
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
string Backup(string id = "game") => RegistryBackupService.CreateBackup(id, [(Registry.CurrentUser, "fixture", "Value")]);

await Test("unique-snapshots", () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    var files = new HashSet<string>();
    for (int i = 0; i < 20; i++) { key.SetValue("Value", i, RegistryValueKind.DWord); files.Add(Backup()); }
    Assert(files.Count == 20, "Repeated saves overwrote original snapshots");
    return Task.CompletedTask;
});
await Test("lossless-value-types", () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    (object value, RegistryValueKind kind)[] cases = [
        (new byte[] { 0, 1, 255 }, RegistryValueKind.Binary),
        (4294967297L, RegistryValueKind.QWord),
        ("%USERPROFILE%\\Games", RegistryValueKind.ExpandString),
        (new[] { "one", "two" }, RegistryValueKind.MultiString),
        (-1, RegistryValueKind.DWord)];
    foreach (var (value, kind) in cases)
    {
        key.SetValue("Value", value, kind);
        var file = Backup();
        key.DeleteValue("Value");
        RegistryBackupService.RestoreBackup(file);
        Assert(key.GetValueKind("Value") == kind, $"Lost type {kind}");
        var actual = key.GetValue("Value");
        Assert(value is Array array ? actual is Array other && array.Cast<object>().SequenceEqual(other.Cast<object>()) : Equals(value, actual), $"Lost value {kind}");
    }
    return Task.CompletedTask;
});
await Test("reverse-order-original-state", async () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    key.SetValue("Value", 0, RegistryValueKind.DWord); var first = Backup("first");
    File.SetCreationTimeUtc(first, new DateTime(2020, 1, 1));
    key.SetValue("Value", 1, RegistryValueKind.DWord); var second = Backup("second");
    File.SetCreationTimeUtc(second, new DateTime(2020, 1, 2));
    key.SetValue("Value", 2, RegistryValueKind.DWord);
    await RevertAllService.RevertEverythingAsync();
    Assert(Equals(0, key.GetValue("Value")), "Restored an intermediate setting instead of the original");
});
await Test("completed-backups-not-replayed", async () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    key.SetValue("Value", 0, RegistryValueKind.DWord); Backup();
    key.SetValue("Value", 1, RegistryValueKind.DWord);
    await RevertAllService.RevertEverythingAsync();
    key.SetValue("Value", 99, RegistryValueKind.DWord);
    await RevertAllService.RevertEverythingAsync();
    Assert(Equals(99, key.GetValue("Value")), "Consumed backups overwrote a later user change");
});
await Test("partial-restore-keeps-history", async () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    key.SetValue("Value", 0, RegistryValueKind.DWord); Backup();
    key.SetValue("Value", 1, RegistryValueKind.DWord); key.DenyWrites = true;
    var result = await RevertAllService.RevertEverythingAsync();
    Assert(result.BackupsFailed > 0 && !OptimizationStateStore.Cleared, "Failed restores must remain retryable and keep applied history");
    Assert(RegistryBackupService.ListBackups().Count == 1, "Failed snapshot lost");
});
await Test("empty-revert-does-not-change-windows", async () =>
{
    await RevertAllService.RevertEverythingAsync();
    Assert(ProcessRunner.Calls.Count == 0, "Without snapshots, revert changed power/hibernation defaults");
});
await Test("writes-always-have-snapshots", () =>
{
    var key = Registry.CurrentUser.CreateSubKey("fixture");
    key.SetValue("Value", 19, RegistryValueKind.DWord);
    RegistryBackupService.SetDword(Registry.CurrentUser, "fixture", "Value", 0);
    Assert(RegistryBackupService.ListBackups().Count == 1, "Persistent change was made without a snapshot");
    RegistryBackupService.RestoreMatchingBackups("registry-value");
    Assert(Equals(19, key.GetValue("Value")), "Setter snapshot did not preserve the original");
    return Task.CompletedTask;
});
Console.WriteLine($"{7 - failed}/7 regression tests passed");
return failed == 0 ? 0 : 1;
