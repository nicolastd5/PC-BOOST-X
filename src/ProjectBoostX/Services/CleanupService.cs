using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace BoostParaPc.Services;

public static class CleanupService
{
    private const int MaxFilesPerTarget = 80_000;
    private const int ScanBudgetMs = 8_000;

    private static readonly (string Name, string Path, bool OnlyRecent)[] DefaultTargets =
    [
        ("Temporários do usuário", Path.GetTempPath(), false),
        ("Temporários do Windows", @"C:\Windows\Temp", false),
        ("Prefetch", @"C:\Windows\Prefetch", true),
        ("Cache do Windows Update", @"C:\Windows\SoftwareDistribution\Download", false),
        ("Cache de thumbnails", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer"), false),
        ("Cache do DirectX", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"), false),
        ("Logs de erro", @"C:\Windows\Minidump", false),
        ("CrashDumps", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"), false),
        ("Cache do Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data", "Default", "Cache"), false),
        ("Cache do Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data", "Default", "Cache"), false),
        ("Cache do Firefox", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozilla", "Firefox", "Profiles"), false),
        ("DirectX Shader Cache (D3D)", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"), false),
    ];

    public static async Task<IReadOnlyList<CleanupTarget>> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var targets = new List<CleanupTarget>();

        await Task.Run(() =>
        {
            // Escaneia alvos em paralelo com orçamento de tempo por alvo
            var bag = new System.Collections.Concurrent.ConcurrentBag<CleanupTarget>();
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            Parallel.ForEach(DefaultTargets, new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = ct
            }, t =>
            {
                ct.ThrowIfCancellationRequested();
                if (swTotal.ElapsedMilliseconds > 25_000) return;

                progress?.Report($"Analisando {t.Name}…");
                var size = t.Name.Contains("thumbnails", StringComparison.OrdinalIgnoreCase)
                    ? MeasureThumbnails(t.Path, ct)
                    : MeasureDirectory(t.Path, ScanBudgetMs, MaxFilesPerTarget, ct);

                if (size > 0)
                    bag.Add(new CleanupTarget(t.Name, t.Path, size, selected: true, onlyRecent: t.OnlyRecent));
            });

            targets.AddRange(bag.OrderBy(x => x.Name));

            // Lixeira: tamanho real é caro; mostra 0 e limpa de verdade
            targets.Add(new CleanupTarget("Lixeira", "Recycle Bin", 0, selected: true, isRecycleBin: true));
        }, ct);

        return targets;
    }

    public static async Task<Models.CleanupResult> CleanAsync(
        IEnumerable<CleanupTarget> selected,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        long freed = 0;
        int files = 0;
        int errors = 0;
        var details = new List<string>();

        foreach (var target in selected.Where(t => t.Selected))
        {
            ct.ThrowIfCancellationRequested();

            if (target.IsRecycleBin)
            {
                progress?.Report("Esvaziando lixeira…");
                try
                {
                    await ClearRecycleBinAsync(ct).ConfigureAwait(false);
                    details.Add("Lixeira esvaziada");
                }
                catch (Exception ex)
                {
                    errors++;
                    details.Add($"Lixeira: {ex.Message}");
                }
                continue;
            }

            progress?.Report($"Limpando {target.Name}…");
            var (freedBytes, removed, errs) = await Task.Run(
                () => CleanDirectory(target.Path, target.OnlyRecent, ct), ct).ConfigureAwait(false);

            freed += freedBytes;
            files += removed;
            errors += errs;
            details.Add($"{target.Name}: {FormatBytes(freedBytes)}, {removed} arquivos");
        }

        return new Models.CleanupResult(freed, files, errors, details);
    }

    private static long MeasureDirectory(string path, int budgetMs, int maxFiles, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return 0;

        long size = 0;
        int count = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested) break;
                if (sw.ElapsedMilliseconds > budgetMs || count >= maxFiles) break;

                var dir = stack.Pop();

                // Arquivos
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        if (++count > maxFiles || sw.ElapsedMilliseconds > budgetMs) break;
                        try { size += new FileInfo(file).Length; } catch { }
                    }
                }
                catch { }

                // Subpastas (pula reparse points pra não entrar em loop)
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        try
                        {
                            var attrs = File.GetAttributes(sub);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }

                        stack.Push(sub);
                    }
                }
                catch { }
            }
        }
        catch { }

        return size;
    }

    private static long MeasureThumbnails(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "thumbcache_*.db"))
            {
                ct.ThrowIfCancellationRequested();
                try { size += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    private static (long freed, int removed, int errors) CleanDirectory(string path, bool onlyRecent, CancellationToken ct)
    {
        long freed = 0;
        int removed = 0;
        int errors = 0;

        if (!Directory.Exists(path)) return (0, 0, 0);

        var minAge = onlyRecent ? DateTime.Now.AddDays(-3) : DateTime.MinValue;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > 60_000) break;
                var dir = stack.Pop();

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > 60_000) break;
                        try
                        {
                            var info = new FileInfo(file);
                            if (onlyRecent && info.LastWriteTime > minAge) continue;
                            var len = info.Length;
                            try { info.IsReadOnly = false; } catch { }
                            File.Delete(file);
                            freed += len;
                            removed++;
                        }
                        catch
                        {
                            errors++;
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        try
                        {
                            var attrs = File.GetAttributes(sub);
                            if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }
                        stack.Push(sub);
                    }
                }
                catch { }
            }

            // Remove pastas vazias no nível raiz
            try
            {
                foreach (var child in Directory.EnumerateDirectories(path))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(child).Any())
                            Directory.Delete(child);
                    }
                    catch { }
                }
            }
            catch { }
        }
        catch
        {
            errors++;
        }

        return (freed, removed, errors);
    }

    private static async Task ClearRecycleBinAsync(CancellationToken ct)
    {
        await ProcessRunner.RunPowerShellAsync(
            "Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
            timeoutMs: 30_000).ConfigureAwait(false);
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}

public sealed class CleanupTarget : INotifyPropertyChanged
{
    private bool _selected;

    public CleanupTarget(string name, string path, long sizeBytes, bool selected = true,
        bool isRecycleBin = false, bool onlyRecent = false)
    {
        Name = name;
        Path = path;
        SizeBytes = sizeBytes;
        _selected = selected;
        IsRecycleBin = isRecycleBin;
        OnlyRecent = onlyRecent;
    }

    public string Name { get; }
    public string Path { get; }
    public long SizeBytes { get; }
    public bool IsRecycleBin { get; }
    public bool OnlyRecent { get; }
    public string SizeDisplay => CleanupService.FormatBytes(SizeBytes);

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
