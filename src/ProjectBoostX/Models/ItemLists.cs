using CommunityToolkit.Mvvm.ComponentModel;

namespace BoostParaPc.Models;

public partial class StartupItem : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Source { get; init; } // Registry, Startup folder, Task
    public required string Location { get; init; }

    [ObservableProperty]
    private bool _isEnabled = true;
}

public partial class WindowsServiceItem : ObservableObject
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string StartType { get; init; }
    public required string Status { get; init; }
    public required RiskLevel Risk { get; init; }
    public bool CanDisable { get; init; } = true;

    [ObservableProperty]
    private bool _isSelected;

    public string RiskDisplay => Risk switch
    {
        RiskLevel.Safe => "Seguro",
        RiskLevel.Moderate => "Moderado",
        RiskLevel.Advanced => "Avançado",
        _ => Risk.ToString()
    };

    public string StartTypeDisplay => StartType switch
    {
        "Automatic" => "Automático",
        "Manual" => "Manual",
        "Disabled" => "Desativado",
        _ => StartType
    };

    public string StatusDisplay => Status switch
    {
        "running" => "Em execução",
        "stopped" => "Parado",
        _ => Status
    };
}
