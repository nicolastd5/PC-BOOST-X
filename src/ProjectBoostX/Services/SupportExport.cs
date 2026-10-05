using System.IO;
using System.IO.Compression;

namespace BoostParaPc.Services;

/// <summary>Zip para suporte: log de ações e informações do PC. Nada é enviado; o caminho do perfil do usuário é trocado por %USERPROFILE%.</summary>
public static class SupportExport
{
    public static string Sanitize(string text, string userProfile) =>
        string.IsNullOrEmpty(userProfile) ? text : text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(userProfile.Replace("\\", "\\\\"), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

    public static void Create(string zipPath, string hardwareInfo, string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        Add(zip, "hardware.txt", Sanitize(hardwareInfo, userProfile));
        var log = File.Exists(ActionLog.FilePath) ? File.ReadAllText(ActionLog.FilePath) : "";
        Add(zip, "actions.jsonl", Sanitize(log, userProfile));
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
