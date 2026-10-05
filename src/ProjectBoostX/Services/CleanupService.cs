using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BoostParaPc.Services;

public static class CleanupService
{
    private const int MaxFilesPerTarget = 80_000;
    private const int ScanBudgetMs = 8_000;

    internal static IReadOnlyList<CleanupTarget> BuildTargets(string windows, string local, string temporary)
    {
        var targets = new List<CleanupTarget>
        {
            new("Temporários do usuário", temporary, 0),
            new("Temporários do Windows", Path.Combine(windows, "Temp"), 0),
            new("Prefetch", Path.Combine(windows, "Prefetch"), 0, onlyRecent: true),
            new("Cache do Windows Update", Path.Combine(windows, "SoftwareDistribution", "Download"), 0),
            new("Cache de thumbnails", Path.Combine(local, "Microsoft", "Windows", "Explorer"), 0,
                searchPattern: "thumbcache_*.db", recursive: false),
            new("Cache do DirectX", Path.Combine(local, "D3DSCache"), 0),
            new("Logs de erro", Path.Combine(windows, "Minidump"), 0),
            new("CrashDumps", Path.Combine(local, "CrashDumps"), 0)
        };

        var profiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(profiles) && IsSafeRoot(profiles))
        {
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(profiles))
                    if (IsSafeRoot(profile))
                        targets.Add(new CleanupTarget($"Cache do Firefox ({Path.GetFileName(profile)})",
                            Path.Combine(profile, "cache2"), 0));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        targets.Add(new CleanupTarget("Lixeira", "Recycle Bin", 0, selected: false, isRecycleBin: true));
        return targets.DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly string[] ChromiumCaches = [@"Cache\Cache_Data", "Code Cache", "GPUCache"];

    /// <summary>Navegadores Chromium (todos os perfis), shaders, Otimização de Entrega, relatórios de erro e apps.</summary>
    internal static IReadOnlyList<CleanupTarget> BuildExtraTargets(string local, string roaming, string common, string steamShaderCache)
    {
        var targets = new List<CleanupTarget>();
        (string Name, string Root)[] browsers =
        [
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"))
        ];
        foreach (var (name, root) in browsers)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root)
                             .Where(d => Path.GetFileName(d) is var n && (n == "Default" || n.StartsWith("Profile ", StringComparison.Ordinal))))
                    foreach (var cache in ChromiumCaches)
                        targets.Add(new CleanupTarget($"Cache do {name} ({Path.GetFileName(profile)}: {Path.GetFileName(cache)})", Path.Combine(profile, cache), 0));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        string[] shaders = [@"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"Intel\ShaderCache"];
        foreach (var shader in shaders)
            targets.Add(new CleanupTarget("Cache de shaders (" + shader.Replace('\\', ' ') + ")", Path.Combine(local, shader), 0));
        targets.Add(new CleanupTarget("Cache de shaders da Steam", steamShaderCache, 0, selected: false));

        targets.Add(new CleanupTarget("Relatórios de erro (fila)", Path.Combine(common, "Microsoft", "Windows", "WER", "ReportQueue"), 0));
        targets.Add(new CleanupTarget("Relatórios de erro (arquivo)", Path.Combine(common, "Microsoft", "Windows", "WER", "ReportArchive"), 0));
        foreach (var folder in new[] { "Cache", "Code Cache", "GPUCache" })
            targets.Add(new CleanupTarget($"Cache do Discord ({folder})", Path.Combine(roaming, "discord", folder), 0));
        targets.Add(new CleanupTarget("Cache do Spotify", Path.Combine(local, "Spotify", "Data"), 0));
        targets.Add(new CleanupTarget("Cache de Otimização de Entrega", "Delivery Optimization", 0, isDeliveryOptimization: true));
        return targets;
    }

    public static Task<IReadOnlyList<CleanupTarget>> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var targets = BuildTargets(Environment.GetFolderPath(Environment.SpecialFolder.Windows), local, Path.GetTempPath())
            .Concat(BuildExtraTargets(local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "shadercache")))
            .DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return ScanTargetsAsync(targets, progress, ct);
    }

    internal static async Task<IReadOnlyList<CleanupTarget>> ScanTargetsAsync(IEnumerable<CleanupTarget> targets,
        IProgress<string>? progress = null, CancellationToken ct = default, int budgetMs = ScanBudgetMs,
        int maxFiles = MaxFilesPerTarget)
    {
        return await Task.Run<IReadOnlyList<CleanupTarget>>(() =>
        {
            var found = new List<CleanupTarget>();
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                if (target.IsDeliveryOptimization) { found.Add(target); continue; }
                if (target.IsRecycleBin) { target.SizeBytes = RecycleBinBytes(); found.Add(target); continue; }
                progress?.Report($"Analisando {target.Name}…");
                var result = ProcessTarget(target, delete: false, ct, budgetMs, maxFiles);
                if (result.Bytes > 0 || result.Errors > 0 || result.Incomplete)
                {
                    target.SizeBytes = result.Bytes;
                    target.IsPartial = result.Incomplete || result.Errors > 0;
                    found.Add(target);
                }
                if (target.IsPartial) progress?.Report($"{target.Name}: análise parcial; alguns arquivos não foram verificados.");
            }
            return found.OrderBy(t => t.Name).ToList();
        }, ct).ConfigureAwait(false);
    }

    public static async Task<Models.CleanupResult> CleanAsync(IEnumerable<CleanupTarget> selected,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        long freed = 0;
        int files = 0, errors = 0;
        var details = new List<string>();
        foreach (var target in selected.Where(t => t.Selected).DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (target.IsDeliveryOptimization)
            {
                progress?.Report("Limpando cache de Otimização de Entrega…");
                try
                {
                    await ProcessRunner.RunPowerShellAsync("Delete-DeliveryOptimizationCache -Force", 60_000, ct).ConfigureAwait(false);
                    details.Add("Cache de Otimização de Entrega limpo; o tamanho não está incluído no total.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors++; details.Add($"Otimização de Entrega não concluída: {ex.Message}"); }
                continue;
            }
            if (target.IsRecycleBin)
            {
                progress?.Report("Esvaziando lixeira…");
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessRunner.RunPowerShellAsync("Clear-RecycleBin -Force -ErrorAction Stop", timeoutMs: 30_000,
                        cancellationToken: ct)
                        .ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    details.Add("Lixeira esvaziada; seu tamanho não está incluído no total liberado.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors++; details.Add($"Lixeira não concluída: {ex.Message}"); }
                continue;
            }
            progress?.Report($"Limpando {target.Name}…");
            Outcome result;
            try
            {
                result = await Task.Run(() => ProcessTarget(target, delete: true, ct, 60_000, MaxFilesPerTarget), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                progress?.Report("Limpeza cancelada. Os arquivos já removidos não são restaurados; o total final não foi calculado.");
                throw;
            }
            freed += result.Bytes;
            files += result.Files;
            errors += result.Errors + (result.Incomplete ? 1 : 0);
            var message = $"{target.Name}: {FormatBytes(result.Bytes)}, {result.Files} arquivos";
            if (result.Incomplete) message += "; interrompido pelo limite de tempo ou quantidade de arquivos";
            if (result.Errors > 0) message += $"; {result.Errors} falhas ou caminhos bloqueados";
            details.Add(message);
            progress?.Report(message);
        }
        return new Models.CleanupResult(freed, files, errors, details);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RecycleBinInfo { public int Size; public long Bytes; public long Items; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref RecycleBinInfo info);

    private static long RecycleBinBytes()
    {
        try
        {
            var info = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
            return SHQueryRecycleBin(null, ref info) == 0 ? info.Bytes : 0;
        }
        catch { return 0; }
    }

    internal sealed record Outcome(long Bytes, int Files, int Errors, bool Incomplete);

    // Scan and deletion use exactly the same file policy, including age, pattern and redirection checks.
    internal static Outcome ProcessTarget(CleanupTarget target, bool delete, CancellationToken ct,
        int budgetMs, int maxFiles)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsSafeRoot(target.Path)) return new(0, 0, 1, false);
        if (!Directory.Exists(target.Path)) return new(0, 0, 0, false);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path));
        var boundary = root + Path.DirectorySeparatorChar;
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var stopwatch = Stopwatch.StartNew();
        long bytes = 0;
        int files = 0, errors = 0, visited = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (stopwatch.ElapsedMilliseconds >= budgetMs || visited >= maxFiles)
                return new(bytes, files, errors, true);
            var directory = pending.Pop();
            if (!IsSafeRoot(directory)) { errors++; continue; }
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    if (stopwatch.ElapsedMilliseconds >= budgetMs || visited >= maxFiles)
                        return new(bytes, files, errors, true);
                    visited++;
                    try
                    {
                        var path = Path.GetFullPath(entry);
                        if (!path.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) { errors++; continue; }
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (target.Recursive) pending.Push(path);
                            continue;
                        }
                        if ((attributes & FileAttributes.ReadOnly) != 0 ||
                            !FileSystemName.MatchesSimpleExpression(target.SearchPattern, Path.GetFileName(path), ignoreCase: true)) continue;
                        var info = new FileInfo(path);
                        if (target.OnlyRecent && info.LastWriteTimeUtc > cutoff) continue;
                        var length = info.Length;
                        // Revalidate ancestors immediately before the operation, not just when queuing directories.
                        if (!HasNoReparseAncestors(path)) { errors++; continue; }
                        if (delete) File.Delete(path);
                        bytes += length;
                        files++;
                    }
                    catch (IOException) { errors++; }
                    catch (UnauthorizedAccessException) { errors++; }
                }
            }
            catch (IOException) { errors++; }
            catch (UnauthorizedAccessException) { errors++; }
        }
        return new(bytes, files, errors, false);
    }

    internal static bool IsSafeRoot(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!), StringComparison.OrdinalIgnoreCase))
                return false;
            var protectedRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (protectedRoots.Where(p => !string.IsNullOrEmpty(p)).Any(p =>
                string.Equals(full, Path.TrimEndingDirectorySeparator(p), StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return false;
            return HasNoReparseAncestors(full);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool HasNoReparseAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}

public sealed class CleanupTarget : INotifyPropertyChanged
{
    private bool _selected;
    public CleanupTarget(string name, string path, long sizeBytes, bool selected = true,
        bool isRecycleBin = false, bool onlyRecent = false, string searchPattern = "*", bool recursive = true,
        bool isDeliveryOptimization = false)
    {
        Name = name; Path = path; SizeBytes = sizeBytes; _selected = selected;
        IsRecycleBin = isRecycleBin; OnlyRecent = onlyRecent; IsDeliveryOptimization = isDeliveryOptimization;
        SearchPattern = searchPattern; Recursive = recursive;
    }
    public string Name { get; }
    public string Path { get; }
    public long SizeBytes { get; internal set; }
    public bool IsRecycleBin { get; }
    public bool IsDeliveryOptimization { get; }
    public bool OnlyRecent { get; }
    internal string SearchPattern { get; }
    internal bool Recursive { get; }
    public bool IsPartial { get; internal set; }
    public string SizeDisplay => IsDeliveryOptimization || (IsRecycleBin && SizeBytes == 0) ? "Tamanho não calculado" :
        CleanupService.FormatBytes(SizeBytes) + (IsPartial ? " (parcial)" : "");
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
