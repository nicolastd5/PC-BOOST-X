using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace BoostParaPc.Models;

/// <summary>Um valor de Registro que o item grava. Serve para aplicar, detectar e reverter.</summary>
public sealed record RegValue(RegistryKey Root, string Key, string Name, object Value, RegistryValueKind Kind);

public partial class OptimizationItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required OptimizationCategory Category { get; init; }
    public required RiskLevel Risk { get; init; }
    public string Impact { get; init; } = string.Empty;

    /// <summary>Quando não usar. Vazio se não houver ressalva.</summary>
    public string Caution { get; init; } = string.Empty;

    public IReadOnlyList<RegValue> Registry { get; init; } = [];

    /// <summary>Alterações que não são Registro (energia, serviços). Recebe o id, que é o dono dos backups.</summary>
    public Func<string, Task>? ApplyExtra { get; init; }

    /// <summary>Detecção do que <see cref="ApplyExtra"/> altera.</summary>
    public Func<bool>? IsAppliedExtra { get; init; }

    /// <summary>Devolve o motivo quando o item é contraindicado para o PC; senão <c>null</c>.</summary>
    public Func<SystemInfo, string?>? NotRecommended { get; init; }

    public bool RequiresRestart { get; init; }

    /// <summary>Ação pontual (ex.: limpar DNS): não tem estado nem reversão.</summary>
    public bool IsAction { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecommended))]
    private string? _notRecommendedReason;

    public bool IsRecommended => NotRecommendedReason is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateDisplay))]
    private ApplyState _state = ApplyState.NotApplied;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsReversible => !IsAction;

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
