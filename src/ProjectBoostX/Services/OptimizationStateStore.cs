using System.IO;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>
/// Persiste quais otimizações o usuário já aplicou (não some ao reabrir o app).
/// </summary>
public static class OptimizationStateStore
{
    private static string FilePath => AppPaths.AppliedFile;

    private static readonly object Lock = new();

    public static HashSet<string> Load()
    {
        lock (Lock)
        {
            try
            {
                if (!File.Exists(FilePath)) return [];
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                return list is null ? [] : new HashSet<string>(list, StringComparer.Ordinal);
            }
            catch
            {
                return [];
            }
        }
    }

    public static void Save(IEnumerable<string> ids)
    {
        lock (Lock)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var temporary = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(ids.Distinct().OrderBy(x => x).ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream);
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
    }

    public static void MarkApplied(string id)
    {
        var set = Load();
        set.Add(id);
        Save(set);
    }

    public static void MarkMany(IEnumerable<string> ids)
    {
        var set = Load();
        foreach (var id in ids) set.Add(id);
        Save(set);
    }

    public static void Clear(string id)
    {
        var set = Load();
        set.Remove(id);
        Save(set);
    }

    public static void ClearAll()
    {
        Save([]);
    }
}
