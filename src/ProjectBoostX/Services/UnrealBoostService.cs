using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoostParaPc.Services;

/// <summary>
/// Aplica e desfaz o preset leve (<see cref="UnrealIni"/>) no <c>GameUserSettings.ini</c> de um jogo em
/// Unreal Engine. Os valores originais ficam num journal por jogo, gravado antes de tocar o arquivo.
/// </summary>
public static class UnrealBoostService
{
    private sealed record Journal(string ExecutablePath, string ConfigPath, List<UnrealIni.Change> Changes);

    /// <summary>Raiz de <c>%LOCALAPPDATA%</c>, onde o Unreal guarda <c>&lt;Projeto&gt;\Saved\Config</c>.</summary>
    public static string LocalAppData { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Recebe a pasta de instalação e o executável detectado; o arquivo só é editado com o jogo fechado.</summary>
    public static Func<string, string, bool> IsRunning { get; set; } = IsGameRunning;

    private static string JournalDir => Path.Combine(AppPaths.BackupDir, "unreal");

    public static bool IsUnrealGame(string exePath) => FindInstallRoot(exePath) is not null;

    public static bool IsBoosted(string exePath) => File.Exists(JournalPath(exePath));

    /// <summary>O <c>GameUserSettings.ini</c> do jogo, ou <c>null</c> se ele nunca foi aberto (o arquivo nasce na primeira execução).</summary>
    public static string? FindConfig(string exePath, string gameName)
    {
        if (FindInstallRoot(exePath) is not { } root) return null;
        try
        {
            // A pasta em %LOCALAPPDATA% costuma ter o nome do projeto; alguns jogos usam o nome comercial.
            var names = Directory.EnumerateDirectories(root)
                .Where(d => !Path.GetFileName(d).Equals("Engine", StringComparison.OrdinalIgnoreCase)
                            && (Directory.Exists(Path.Combine(d, "Binaries")) || Directory.Exists(Path.Combine(d, "Content"))))
                .Select(d => Path.GetFileName(d))
                .Append(Regex.Replace(Path.GetFileNameWithoutExtension(exePath), "-Win(64|GDK)-Shipping$", "", RegexOptions.IgnoreCase))
                .Append(gameName);
            return names.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => n.Length > 0 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                .Select(n => Path.Combine(LocalAppData, n, "Saved", "Config"))
                .Where(Directory.Exists)
                .SelectMany(config => Directory.EnumerateFiles(config, "GameUserSettings.ini", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Devolve um resumo do que mudou. Lança com mensagem para o usuário quando não pode aplicar.</summary>
    public static string Apply(string exePath, string gameName)
    {
        var config = FindConfig(exePath, gameName)
            ?? throw new InvalidOperationException("Configuração gráfica não encontrada. Abra o jogo pelo menos uma vez, feche-o e tente de novo.");
        EnsureEditable(exePath, config);

        var (text, encoding) = ReadText(config);
        var (updated, changes) = UnrealIni.ApplyLightPreset(text);
        if (changes.Count == 0)
            return text.Contains("[ScalabilityGroups]", StringComparison.OrdinalIgnoreCase)
                ? "os gráficos já estão no nível do preset leve ou abaixo"
                : "este jogo não usa as opções gráficas padrão do Unreal; nada foi alterado";

        // Uma segunda aplicação não pode apagar os originais guardados na primeira.
        var known = ReadJournal(exePath)?.Changes ?? [];
        var merged = known.Concat(changes.Where(c => !known.Any(k => k.Key.Equals(c.Key, StringComparison.OrdinalIgnoreCase)))).ToList();
        WriteJournal(new Journal(exePath, config, merged));
        WriteText(config, updated, encoding);
        return $"preset leve aplicado ({changes.Count} opções reduzidas)";
    }

    /// <summary><c>false</c> quando não havia preset aplicado neste jogo.</summary>
    public static bool Revert(string exePath)
    {
        if (ReadJournal(exePath) is not { } journal) return false;
        if (File.Exists(journal.ConfigPath))
        {
            EnsureEditable(journal.ExecutablePath, journal.ConfigPath);
            var (text, encoding) = ReadText(journal.ConfigPath);
            WriteText(journal.ConfigPath, UnrealIni.Restore(text, journal.Changes), encoding);
        }
        // Jogo desinstalado ou configuração apagada: não há o que restaurar, só o registro a encerrar.
        var archive = Path.Combine(JournalDir, "restored");
        Directory.CreateDirectory(archive);
        File.Move(JournalPath(exePath), Path.Combine(archive, $"{Path.GetFileName(JournalPath(exePath))}.{Guid.NewGuid():N}"));
        return true;
    }

    /// <summary>Desfaz o preset em todos os jogos alterados. Devolve quantos foram desfeitos.</summary>
    public static int RevertAll()
    {
        if (!Directory.Exists(JournalDir)) return 0;
        var count = 0;
        foreach (var file in Directory.GetFiles(JournalDir, "*.json"))
        {
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(file))
                ?? throw new InvalidDataException("Registro de preset do Unreal inválido.");
            if (Revert(journal.ExecutablePath)) count++;
        }
        return count;
    }

    /// <summary>Jogos empacotados do Unreal têm a pasta <c>Engine</c> ao lado da pasta do projeto.</summary>
    private static string? FindInstallRoot(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        for (var depth = 0; dir is not null && depth < 4; depth++, dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, "Engine", "Binaries")) || Directory.Exists(Path.Combine(dir, "Engine", "Content")))
                return dir;
        return null;
    }

    private static void EnsureEditable(string exePath, string config)
    {
        if (IsRunning(FindInstallRoot(exePath) ?? Path.GetDirectoryName(exePath)!, exePath))
            throw new InvalidOperationException("Feche o jogo antes: ele regrava a configuração ao sair.");
        if (File.GetAttributes(config).HasFlag(FileAttributes.ReadOnly))
            throw new InvalidOperationException("O arquivo de configuração está como somente leitura (opções gráficas travadas). Nada foi alterado.");
    }

    private static bool IsGameRunning(string installRoot, string exePath)
    {
        // O executável detectado pode ser só o iniciador; o jogo de verdade fica em <Projeto>\Binaries\Win64.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(exePath) };
        try
        {
            foreach (var project in Directory.EnumerateDirectories(installRoot))
            {
                var binaries = Path.Combine(project, "Binaries", "Win64");
                if (!Directory.Exists(binaries)) continue;
                foreach (var exe in Directory.EnumerateFiles(binaries, "*.exe")) names.Add(Path.GetFileNameWithoutExtension(exe));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var running = false;
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            running |= processes.Length > 0;
            foreach (var process in processes) process.Dispose();
        }
        return running;
    }

    private static (string Text, Encoding Encoding) ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding = bytes is [0xFF, 0xFE, ..] ? new UnicodeEncoding(false, true)
            : bytes is [0xEF, 0xBB, 0xBF, ..] ? new UTF8Encoding(true)
            : new UTF8Encoding(false);
        var preamble = encoding.GetPreamble().Length;
        return (encoding.GetString(bytes, preamble, bytes.Length - preamble), encoding);
    }

    private static void WriteText(string path, string text, Encoding encoding)
    {
        var temporary = path + ".boostx.tmp";
        File.WriteAllBytes(temporary, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);
        File.Move(temporary, path, overwrite: true);
    }

    private static string JournalPath(string exePath) => Path.Combine(JournalDir,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())))[..16] + ".json");

    private static Journal? ReadJournal(string exePath) => File.Exists(JournalPath(exePath))
        ? JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath(exePath)))
        : null;

    private static void WriteJournal(Journal journal)
    {
        Directory.CreateDirectory(JournalDir);
        var file = JournalPath(journal.ExecutablePath);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal));
        File.Move(temporary, file, overwrite: true);
    }
}
