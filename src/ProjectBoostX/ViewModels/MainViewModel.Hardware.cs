using BoostParaPc.Models;
using BoostParaPc.Services;

namespace BoostParaPc.ViewModels;

public partial class MainViewModel
{
    // Chamado pelo setter gerado de SystemInfo, na thread da interface.
    partial void OnSystemInfoChanged(SystemInfo? value)
    {
        if (value is null) return;
        OptimizationEngine.ApplyHardwareRules(OptimizationCatalog.All, value);
        foreach (var item in OptimizationCatalog.All.Where(i => !i.IsRecommended))
            item.IsSelected = false;
    }
}
