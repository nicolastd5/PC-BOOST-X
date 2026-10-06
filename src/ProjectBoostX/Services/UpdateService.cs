using System.Net.Http;
using System.Text.Json;

namespace BoostParaPc.Services;

/// <summary>Verifica se há versão nova no GitHub. Só avisa e dá o link; nunca baixa nem instala.</summary>
public static class UpdateService
{
    // Vazio desliga a verificação.
    public const string Repository = "nicolastd5/PC-BOOST-X";

    public sealed record Update(string Version, string Url);

    /// <summary>Aceita "v3.1.0" ou "3.1.0". Devolve null se o texto não for uma versão.</summary>
    public static Version? ParseTag(string? tag) =>
        Version.TryParse(tag?.TrimStart('v', 'V'), out var v) ? v : null;

    public static bool IsNewer(string? tag, Version current) => ParseTag(tag) is { } latest && latest > current;

    /// <summary>Falha de rede, limite da API ou resposta estranha: devolve null, sem erro para o usuário.</summary>
    public static async Task<Update?> CheckAsync(Version current, string repository = Repository, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        try
        {
            using var http = handler is null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(8);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ProjectBoostX");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{repository}/releases/latest", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            var url = doc.RootElement.GetProperty("html_url").GetString();
            return IsNewer(tag, current) && url is not null ? new Update(tag!, url) : null;
        }
        catch { return null; }
    }
}
