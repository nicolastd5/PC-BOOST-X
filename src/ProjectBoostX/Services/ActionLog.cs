using System.IO;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>Registro de tudo que o programa aplicou ou reverteu, uma linha JSON por ação.</summary>
public static class ActionLog
{
    private static readonly object Gate = new();

    public sealed record Entry(DateTime Utc, string ItemId, string Action, bool Ok, string? Message);

    public static string FilePath => Path.Combine(AppPaths.DataDir, "log", "actions.jsonl");

    public static void Write(string itemId, string action, bool ok, string? message = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(new Entry(DateTime.UtcNow, itemId, action, ok, message)) + "\n");
            }
        }
        catch (IOException) { /* o log nunca derruba a operação */ }
        catch (UnauthorizedAccessException) { }
    }
}
