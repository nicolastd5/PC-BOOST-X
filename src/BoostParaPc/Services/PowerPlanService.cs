namespace BoostParaPc.Services;

public static class PowerPlanService
{
    // GUIDs padrão do Windows
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public static async Task<bool> ActivateHighPerformanceAsync()
    {
        // Tenta Alto Desempenho; se não existir, tenta Desempenho Máximo
        var r1 = await ProcessRunner.RunAsync("powercfg", $"/setactive {HighPerformance}");
        if (r1.Success) return true;

        var r2 = await ProcessRunner.RunAsync("powercfg", $"/setactive {UltimatePerformance}");
        if (r2.Success) return true;

        // Cria um plano custom baseado no Equilibrado
        var duplicate = await ProcessRunner.RunAsync("powercfg", $"/duplicatescheme {Balanced}");
        if (duplicate.Success)
        {
            // Extrai GUID do output
            var guid = ExtractGuid(duplicate.StdOut);
            if (guid is not null)
            {
                await ProcessRunner.RunAsync("powercfg", $"/setactive {guid}");
                return true;
            }
        }
        return false;
    }

    public static async Task<bool> ActivateBalancedAsync()
    {
        var result = await ProcessRunner.RunAsync("powercfg", $"/setactive {Balanced}");
        return result.Success;
    }

    public static async Task DisableSleepTimeoutsAsync()
    {
        // AC (tomada): não dormir
        await ProcessRunner.RunAsync("powercfg", "/change standby-timeout-ac 0");
        await ProcessRunner.RunAsync("powercfg", "/change hibernate-timeout-ac 0");
        await ProcessRunner.RunAsync("powercfg", "/change monitor-timeout-ac 0");
    }

    public static async Task SetGpuPreferenceAsync()
    {
        // Preferência de GPU de alto desempenho no plano ativo
        // SUB_VIDEO 7516b95f-f776-4464-8c53-06167f40cc99
        // PROCTHROTTLEMAX 75b0ae3f-dce0-47c1-8f8a-a1b8a1b0e0e1 — skip, use powercfg overlays
        await ProcessRunner.RunAsync("powercfg", "/setacvalueindex scheme_current sub_processor PROCTHROTTLEMAX 100");
        await ProcessRunner.RunAsync("powercfg", "/setdcvalueindex scheme_current sub_processor PROCTHROTTLEMAX 100");
        await ProcessRunner.RunAsync("powercfg", "/setactive scheme_current");
    }

    private static string? ExtractGuid(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : null;
    }
}
