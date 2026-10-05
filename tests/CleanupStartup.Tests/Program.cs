using System.Reflection;
using BoostParaPc.Models;
using BoostParaPc.Services;

var suiteRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/CleanupStartup.Tests/fixtures"));
var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
if (!suiteRoot.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Fixture root is not inside the workspace.");
Directory.CreateDirectory(suiteRoot);
var failed = 0;
await Test("cleanup preserves read-only files", async dir =>
{
    var file = Path.Combine(dir, "important.txt");
    File.WriteAllText(file, "keep me");
    File.SetAttributes(file, FileAttributes.ReadOnly);
    var result = await CleanupService.CleanAsync([new CleanupTarget("Fixture", dir, 7)]);
    Assert(File.Exists(file), "Read-only file was deleted.");
    Assert(result.FilesRemoved == 0, "Read-only file was counted as removed.");
});
await Test("startup folder disable survives reload and enables exact bytes", async dir =>
{
    var folder = Path.Combine(dir, "startup");
    Directory.CreateDirectory(folder);
    var original = Path.Combine(folder, "app.lnk");
    byte[] bytes = [1, 2, 3, 0, 255];
    File.WriteAllBytes(original, bytes);
    var item = new StartupItem { Name = "app", Command = original, Source = "Pasta Startup", Location = folder };
    await StartupService.DisableAsync(item);
    Assert(!File.Exists(original), "Startup file is still active after disable.");
    var enumerate = typeof(StartupService).GetMethod("GetFolderItems", BindingFlags.Static | BindingFlags.NonPublic);
    Assert(enumerate is not null, "Disabled startup files cannot be enumerated after reload.");
    var reloaded = ((IEnumerable<StartupItem>)enumerate!.Invoke(null, [folder])!).Single();
    Assert(!reloaded.IsEnabled, "Reloaded disabled startup item is shown as enabled.");
    await StartupService.EnableAsync(reloaded);
    Assert(File.Exists(original), "Startup file was not restored.");
    Assert(File.ReadAllBytes(original).SequenceEqual(bytes), "Restored startup bytes differ.");
});
await Test("legacy boostbak startup entries can be re-enabled", async dir =>
{
    var original = Path.Combine(dir, "legacy.cmd");
    File.WriteAllText(original + ".boostbak", "echo legacy");
    var item = new StartupItem { Name = "legacy", Command = original, Source = "Pasta Startup", Location = dir, IsEnabled = false };
    await StartupService.EnableAsync(item);
    Assert(File.Exists(original), "Legacy .boostbak was not restored.");
    Assert(File.ReadAllText(original) == "echo legacy", "Legacy contents changed.");
});
await Test("default cleanup scopes protect Explorer and Firefox profile files", async dir =>
{
    var local = Path.Combine(dir, "local");
    var windows = Path.Combine(dir, "windows");
    var explorer = Path.Combine(local, "Microsoft", "Windows", "Explorer");
    var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles", "abc.default");
    Directory.CreateDirectory(explorer);
    Directory.CreateDirectory(Path.Combine(firefox, "cache2"));
    File.WriteAllText(Path.Combine(explorer, "thumbcache_32.db"), "cache");
    File.WriteAllText(Path.Combine(explorer, "settings.dat"), "keep settings");
    File.WriteAllText(Path.Combine(firefox, "logins.json"), "keep passwords");
    File.WriteAllText(Path.Combine(firefox, "cache2", "cache.bin"), "cache");
    var build = typeof(CleanupService).GetMethod("BuildTargets", BindingFlags.Static | BindingFlags.NonPublic);
    Assert(build is not null, "Cleanup does not expose explicit scoped target policies.");
    var targets = ((IEnumerable<CleanupTarget>)build!.Invoke(null, [windows, local, Path.Combine(dir, "temp")])!).ToList();
    Assert(targets.Count(t => t.Path.EndsWith("D3DSCache")) == 1, "DirectX cache is duplicated.");
    Assert(!targets.Single(t => t.IsRecycleBin).Selected, "Recycle Bin is selected by default.");
    var scoped = targets.Where(t => t.Path == explorer || t.Path.StartsWith(firefox)).ToList();
    var scan = await CleanupService.ScanTargetsAsync(scoped);
    Assert(scan.Sum(t => t.SizeBytes) == 10, "Scan did not measure exactly the selected cache files.");
    var result = await CleanupService.CleanAsync(scoped);
    Assert(result.FilesRemoved == 2 && result.BytesFreed == 10, "Scoped cleanup did not remove exactly the two cache files.");
    Assert(File.Exists(Path.Combine(explorer, "settings.dat")), "Explorer non-cache data was deleted.");
    Assert(File.Exists(Path.Combine(firefox, "logins.json")), "Firefox profile data was deleted.");
});
await Test("cleanup refuses dangerous root directories before enumeration", dir =>
{
    var safeRoot = typeof(CleanupService).GetMethod("IsSafeRoot", BindingFlags.Static | BindingFlags.NonPublic);
    Assert(safeRoot is not null, "Cleanup does not validate dangerous roots.");
    foreach (var root in new[] { Path.GetPathRoot(dir)!, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        Assert(!(bool)safeRoot!.Invoke(null, [root])!, "Dangerous root was accepted: " + root);
    return Task.CompletedTask;
});
await Test("cleanup cancellation does not report success", async dir =>
{
    File.WriteAllText(Path.Combine(dir, "untouched.txt"), "keep");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try { await CleanupService.CleanAsync([new CleanupTarget("Fixture", dir, 4)], ct: cancellation.Token); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Cancellation was swallowed.");
});
await Test("cleanup scan and deletion share file age and pattern filters", async dir =>
{
    File.WriteAllText(Path.Combine(dir, "thumbcache_old.db"), "old");
    File.SetLastWriteTimeUtc(Path.Combine(dir, "thumbcache_old.db"), DateTime.UtcNow.AddDays(-4));
    File.WriteAllText(Path.Combine(dir, "thumbcache_new.db"), "new");
    File.WriteAllText(Path.Combine(dir, "unrelated.db"), "keep");
    var target = new CleanupTarget("Filtered", dir, 0, onlyRecent: true, searchPattern: "thumbcache_*.db");
    var scan = await CleanupService.ScanTargetsAsync([target]);
    Assert(scan.Single().SizeBytes == 3, "Scan ignored the age or filename filter.");
    var result = await CleanupService.CleanAsync(scan);
    Assert(result.FilesRemoved == 1 && result.BytesFreed == 3, "Delete scope differs from scan.");
    Assert(File.Exists(Path.Combine(dir, "thumbcache_new.db")), "Recent cache was removed.");
    Assert(File.Exists(Path.Combine(dir, "unrelated.db")), "Unmatched file was removed.");
});
await Test("cleanup budget exhaustion marks estimates partial", async dir =>
{
    File.WriteAllText(Path.Combine(dir, "cache.bin"), "cache");
    var target = new CleanupTarget("Budget", dir, 0);
    var scan = await CleanupService.ScanTargetsAsync([target], budgetMs: 0);
    Assert(scan.Single().IsPartial, "Timed-out estimate is presented as complete.");
    var deletion = CleanupService.ProcessTarget(target, true, default, budgetMs: 0, maxFiles: 100);
    Assert(deletion.Incomplete && deletion.Files == 0, "Deletion timeout is not explicitly reported.");
    Assert(File.Exists(Path.Combine(dir, "cache.bin")), "Budget-exhausted deletion continued.");
});
await Test("cleanup never follows a junction at root or within a target", async dir =>
{
    var external = Path.Combine(dir, "outside-scope");
    var scope = Path.Combine(dir, "scope");
    Directory.CreateDirectory(external);
    Directory.CreateDirectory(scope);
    var sentinel = Path.Combine(external, "keep.txt");
    File.WriteAllText(sentinel, "preserve");
    File.WriteAllText(Path.Combine(scope, "cache.txt"), "cache");
    var link = Path.Combine(scope, "redirect");
    CreateJunction(link, external);
    var rootResult = await CleanupService.CleanAsync([new CleanupTarget("Redirect", link, 8)]);
    Assert(rootResult.FilesRemoved == 0 && File.Exists(sentinel), "Root junction target was traversed.");
    var nestedResult = await CleanupService.CleanAsync([new CleanupTarget("Scoped", scope, 5)]);
    Assert(nestedResult.FilesRemoved == 1 && File.Exists(sentinel), "Nested junction target was traversed.");
    var scan = await CleanupService.ScanTargetsAsync([new CleanupTarget("Scoped", scope, 0)]);
    Assert(scan.Sum(t => t.SizeBytes) == 0, "Scan followed the nested junction.");
});
await Test("failed Recycle Bin command cannot report success", async dir =>
{
    var result = await CleanupService.CleanAsync([new CleanupTarget("Lixeira", "Recycle Bin", 0, isRecycleBin: true)]);
    Assert(result.Errors == 1 && result.FilesRemoved == 0, "Recycle Bin command failure was ignored.");
    Assert(!result.Details.Any(d => d.StartsWith("Lixeira esvaziada")), "Failure claimed that the Recycle Bin was emptied.");
});
await Test("startup backups disambiguate identical names in different folders", async dir =>
{
    var items = new List<StartupItem>();
    foreach (var folderName in new[] { "user", "common" })
    {
        var folder = Path.Combine(dir, folderName);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "same.cmd");
        File.WriteAllText(file, folderName);
        var item = new StartupItem { Name = "same", Command = file, Source = "Pasta Startup", Location = folder };
        items.Add(item);
        await StartupService.DisableAsync(item);
    }
    await StartupService.EnableAsync(items[0]);
    Assert(File.ReadAllText(items[0].Command) == "user", "Wrong same-name backup was restored.");
    Assert(!File.Exists(items[1].Command), "Restoring one item restored another folder.");
    await StartupService.EnableAsync(items[1]);
    Assert(File.ReadAllText(items[1].Command) == "common", "Second backup contents changed.");
});
await Test("startup restore preserves conflicting new file and retains its backup", async dir =>
{
    var file = Path.Combine(dir, "app.cmd");
    File.WriteAllText(file, "original");
    var item = new StartupItem { Name = "app", Command = file, Source = "Pasta Startup", Location = dir };
    await StartupService.DisableAsync(item);
    File.WriteAllText(file, "new file");
    var rejected = false;
    try { await StartupService.EnableAsync(item); }
    catch (IOException) { rejected = true; }
    Assert(rejected, "Restore silently overwrote a conflicting file.");
    Assert(File.ReadAllText(file) == "new file", "Existing file was overwritten.");
    Assert(Directory.GetFiles(Path.Combine(AppPaths.BackupDir, "startup-files"), "*.backup").Length == 1,
        "Backup was lost after failed restore.");
    File.Delete(file);
    await StartupService.EnableAsync(item);
    Assert(File.ReadAllText(file) == "original", "Retry could not restore original bytes.");
});
Console.WriteLine($"Failures: {failed}");
return failed == 0 ? 0 : 1;

async Task Test(string name, Func<string, Task> test)
{
    var dir = Path.Combine(suiteRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    AppPaths.DataDir = Path.Combine(dir, "app-data");
    try { await test(dir); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}"); }
    // Fixtures are intentionally retained; no recursive deletion is performed by this runner.
}
void Assert(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

void CreateJunction(string link, string target)
{
    if (!Path.GetFullPath(link).StartsWith(suiteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFullPath(target).StartsWith(suiteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Junction paths must remain within the verified fixture root.");
    var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        Arguments = $"/c mklink /J \"{link}\" \"{target}\""
    };
    using var process = System.Diagnostics.Process.Start(start)!;
    process.WaitForExit();
    Assert(process.ExitCode == 0, "Unable to create fixture junction: " + process.StandardError.ReadToEnd());
}
