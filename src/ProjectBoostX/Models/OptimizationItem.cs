using CommunityToolkit.Mvvm.ComponentModel;

namespace BoostParaPc.Models;

public partial class OptimizationItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required OptimizationCategory Category { get; init; }
    public required RiskLevel Risk { get; init; }
    public string Impact { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    private ApplyState _state = ApplyState.NotApplied;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsReversible => Id is not ("net.dns.flush" or "memory.standby" or "sys.flushdns");

    public string RiskDisplay => Risk switch
    {
        RiskLevel.Safe => "Seguro",
        RiskLevel.Moderate => "Moderado",
        RiskLevel.Advanced => "Avançado",
        _ => Risk.ToString()
    };

    public string CategoryDisplay => Category switch
    {
        OptimizationCategory.Power => "Energia",
        OptimizationCategory.Gaming => "Jogos",
        OptimizationCategory.Input => "Input",
        OptimizationCategory.Visual => "Visual",
        OptimizationCategory.Telemetry => "Telemetria",
        OptimizationCategory.Services => "Serviços",
        OptimizationCategory.Network => "Rede",
        OptimizationCategory.Memory => "Memória",
        OptimizationCategory.System => "Sistema",
        _ => Category.ToString()
    };

    public string StateDisplay => State switch
    {
        ApplyState.Applied => "Aplicado",
        ApplyState.Failed => "Falhou",
        ApplyState.Unsupported => "Não suportado",
        _ => "Não aplicado"
    };
}
