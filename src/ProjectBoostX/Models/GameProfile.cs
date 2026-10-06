using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BoostParaPc.Models;

public partial class GameProfile : ObservableObject
{
    public required string Name { get; init; }
    public required string ExecutablePath { get; init; }
    public string Platform { get; init; } = "Desconhecido";
    public string? AppId { get; init; }
    public int? SteamAppId { get; set; }
    public string RecommendationKey { get; init; } = "casual";

    [ObservableProperty]
    private bool _isFullscreenOptDisabled;

    [ObservableProperty]
    private bool _isHighDpiOverridden;

    [ObservableProperty]
    private bool _isHighPriority;

    [ObservableProperty]
    private bool _isGpuPreferred;

    [ObservableProperty]
    private string _status = "—";

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _recommendationSummary = "";

    [ObservableProperty]
    private string _recommendationTips = "";

    [ObservableProperty]
    private ImageSource? _coverImage;

    [ObservableProperty]
    private string _coverPath = "";

    [ObservableProperty]
    private bool _isDetailOpen;

    /// <summary>Jogo em Unreal Engine: pode receber o preset gráfico leve.</summary>
    public bool IsUnrealEngine { get; set; }

    [ObservableProperty]
    private bool _isUnrealBoosted;

    public string FileName => Path.GetFileName(ExecutablePath);

    public void LoadRecommendationInfo()
    {
        var rec = Services.GameProfileService.GetRecommendation(RecommendationKey);
        RecommendationSummary = $"{rec.Title} — {rec.Summary}";
        RecommendationTips = string.Join("\n", rec.Tips.Select(t => "• " + t));
    }
}
