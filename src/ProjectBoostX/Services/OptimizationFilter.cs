using BoostParaPc.Models;

namespace BoostParaPc.Services;

/// <summary>Filtro da lista de Otimizações: categoria + risco + texto (nome, descrição e ressalva), sem acento nem caixa.</summary>
public static class OptimizationFilter
{
    public static bool Matches(OptimizationItem item, OptimizationCategory? category, RiskLevel? risk, string? text)
    {
        if (category is not null && item.Category != category) return false;
        if (risk is not null && item.Risk != risk) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var haystack = Normalize(item.Name + " " + item.Description + " " + item.Caution);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => haystack.Contains(Normalize(word)));
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant();
    }
}
