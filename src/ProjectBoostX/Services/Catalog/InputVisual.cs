using BoostParaPc.Models;

namespace BoostParaPc.Services;

public static partial class OptimizationCatalog
{
    private const string Mouse = @"Control Panel\Mouse";
    private const string Desktop = @"Control Panel\Desktop";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private static IEnumerable<OptimizationItem> InputVisualItems() =>
    [
        new()
        {
            Id = "input.mouseaccel", Name = "Desativar aceleração do mouse",
            Description = "Desliga 'Aprimorar precisão do ponteiro': o cursor anda sempre a mesma distância para o mesmo movimento.",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Mira consistente",
            Registry = [Sz(HKCU, Mouse, "MouseSpeed", "0"), Sz(HKCU, Mouse, "MouseThreshold1", "0"), Sz(HKCU, Mouse, "MouseThreshold2", "0")],
            ApplyExtra = _ => ProcessRunner.RunCheckedAsync("rundll32.exe", "user32.dll,UpdatePerUserSystemParameters")
        },
        new()
        {
            Id = "input.keyboard", Name = "Repetição de tecla mais rápida",
            Description = "Menor atraso inicial e maior taxa de repetição ao segurar uma tecla. É preferência, não desempenho.",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Teclado mais ágil ao segurar teclas",
            Registry = [Sz(HKCU, @"Control Panel\Keyboard", "KeyboardDelay", "0"), Sz(HKCU, @"Control Panel\Keyboard", "KeyboardSpeed", "31")]
        },
        new()
        {
            Id = "input.menudelay", Name = "Menus sem atraso",
            Description = "Abre submenus imediatamente (MenuShowDelay = 0).",
            Category = OptimizationCategory.Input, Risk = RiskLevel.Safe, Impact = "Interface mais ágil",
            Registry = [Sz(HKCU, Desktop, "MenuShowDelay", "0")]
        },
        new()
        {
            Id = "visual.effects", Name = "Efeitos visuais para desempenho",
            Description = "Desliga animações da barra de tarefas e o retângulo de seleção translúcido.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Interface mais leve",
            RequiresRestart = true,
            Registry =
            [
                Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3),
                Dw(HKCU, ExplorerAdvanced, "ListviewAlphaSelect", 0),
                Dw(HKCU, ExplorerAdvanced, "TaskbarAnimations", 0)
            ]
        },
        new()
        {
            Id = "visual.animations", Name = "Desativar animações de janela",
            Description = "Minimizar e maximizar sem animação; arrastar mostra só o contorno da janela.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Janelas instantâneas",
            RequiresRestart = true,
            Registry = [Sz(HKCU, Desktop + @"\WindowMetrics", "MinAnimate", "0"), Sz(HKCU, Desktop, "DragFullWindows", "0")]
        },
        new()
        {
            Id = "visual.transparency", Name = "Desativar transparência",
            Description = "Desliga o efeito translúcido de barra de tarefas, menu Iniciar e janelas.",
            Category = OptimizationCategory.Visual, Risk = RiskLevel.Safe, Impact = "Menos trabalho de composição para a GPU",
            Registry = [Dw(HKCU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)]
        },
    ];
}
