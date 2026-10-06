using System.Globalization;

namespace BoostParaPc.Services;

/// <summary>
/// Preset gráfico leve para jogos em Unreal Engine: edita só a seção <c>[ScalabilityGroups]</c> do
/// <c>GameUserSettings.ini</c>, a mesma que o menu de gráficos do jogo grava. Só baixa valores que já
/// existem e estão acima do alvo; nunca cria chaves nem sobe qualidade.
/// </summary>
public static class UnrealIni
{
    /// <summary>Uma chave alterada e o valor que ela tinha antes.</summary>
    public sealed record Change(string Key, string Old);

    private const string Section = "[ScalabilityGroups]";
    private const string ResolutionKey = "sg.ResolutionQuality";
    private const double MaxResolution = 80; // % da resolução de renderização; 0 é "automático" e fica como está
    private const double Medium = 1;         // escala do Unreal: 0 Baixo, 1 Médio, 2 Alto, 3 Épico

    // Sombras, efeitos, pós-processamento, iluminação global, reflexos, folhagem e shading.
    // Texturas, distância de visão e antialiasing ficam de fora: pesam pouco no FPS ou mudam a jogabilidade.
    private static readonly string[] QualityKeys =
    [
        "sg.ShadowQuality", "sg.EffectsQuality", "sg.PostProcessQuality", "sg.GlobalIlluminationQuality",
        "sg.ReflectionQuality", "sg.FoliageQuality", "sg.ShadingQuality"
    ];

    public static (string Text, IReadOnlyList<Change> Changes) ApplyLightPreset(string text)
    {
        var changes = new List<Change>();
        var result = Rewrite(text, (key, value) =>
        {
            var target = Target(key, value);
            if (target is not null) changes.Add(new(key, value));
            return target;
        });
        return (result, changes);
    }

    /// <summary>
    /// Devolve os valores anteriores. Uma chave que o jogador mudou depois (não está mais no valor
    /// que o preset gravou) fica como ele deixou.
    /// </summary>
    public static string Restore(string text, IEnumerable<Change> changes)
    {
        var old = changes.ToDictionary(c => c.Key, c => c.Old, StringComparer.OrdinalIgnoreCase);
        return Rewrite(text, (key, value) => old.TryGetValue(key, out var previous) && Target(key, previous) == value ? previous : null);
    }

    /// <summary>O valor que o preset grava no lugar de <paramref name="value"/>, ou <c>null</c> se não mexe.</summary>
    private static string? Target(string key, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return null;
        if (key.Equals(ResolutionKey, StringComparison.OrdinalIgnoreCase))
            return number > MaxResolution ? Format(MaxResolution, value) : null;
        return QualityKeys.Contains(key, StringComparer.OrdinalIgnoreCase) && number > Medium ? Format(Medium, value) : null;
    }

    /// <summary>Mantém o estilo do valor original ("100.000000" ou "3").</summary>
    private static string Format(double number, string like) =>
        number.ToString(like.Contains('.') ? "F6" : "F0", CultureInfo.InvariantCulture);

    /// <summary>Percorre as linhas da seção; <paramref name="replace"/> devolve o novo valor ou <c>null</c> para manter a linha.</summary>
    private static string Rewrite(string text, Func<string, string, string?> replace)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(newline);
        var inSection = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('['))
            {
                inSection = trimmed.Equals(Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var equals = lines[i].IndexOf('=');
            if (!inSection || equals <= 0) continue;
            var key = lines[i][..equals].Trim();
            if (replace(key, lines[i][(equals + 1)..].Trim()) is { } next) lines[i] = $"{key}={next}";
        }
        return string.Join(newline, lines);
    }
}
