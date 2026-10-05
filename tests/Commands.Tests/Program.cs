using System.Reflection;
using BoostParaPc.Services;
using BoostParaPc.ViewModels;

var failures = new List<string>();
var testCount = 0;
async Task Test(string name, Func<Task> run)
{
    testCount++;
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name); Console.WriteLine($"FAIL {name}: {ex.GetBaseException().Message}"); }
}
void Require(bool value, string message) { if (!value) throw new Exception(message); }
async Task ExpectFailure(Func<Task> run, string message)
{
    try { await run(); }
    catch (Exception ex) { Require(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), ex.Message); return; }
    throw new Exception("A falha foi tratada como sucesso");
}
await Test("PowerShell nonterminating error is failure", () => ExpectFailure(
    () => ProcessRunner.RunPowerShellAsync("Write-Error 'simulated denial'; 'OK'"), "simulated denial"));
await Test("PowerShell native exit is failure", () => ExpectFailure(
    () => ProcessRunner.RunPowerShellAsync("cmd.exe /c exit 9"), "9"));
await Test("Checked subprocess reports stderr", async () =>
{
    var method = typeof(ProcessRunner).GetMethod("RunCheckedAsync") ?? throw new Exception("RunCheckedAsync ausente");
    await ExpectFailure(async () => await (Task)method.Invoke(null, ["cmd.exe", "/c echo simulated-denial 1>&2 & exit /b 7", 5000, false, CancellationToken.None])!, "simulated-denial");
});
await Test("PowerShell timeout is failure", () => ExpectFailure(
    () => ProcessRunner.RunPowerShellAsync("Start-Sleep -Seconds 10", 200), "tempo"));
await Test("PowerShell cancellation propagates as cancellation", async () =>
{
    using var cancellation = new CancellationTokenSource(300);
    try { await ProcessRunner.RunPowerShellAsync("Start-Sleep -Seconds 10", cancellationToken: cancellation.Token); }
    catch (OperationCanceledException) { return; }
    throw new Exception("Cancelamento não propagado");
});
await Test("ProcessRunner retains complete stdout/stderr", async () =>
{
    var result = await ProcessRunner.RunPowerShellAsync("1..300 | ForEach-Object { Write-Output ('line-' + $_) }; [Console]::Error.WriteLine('diagnostic')");
    Require(result.EndsWith("line-300"), "stdout incompleto");
});

string RestoreScript(string description) => (string)(typeof(RestorePointService)
    .GetMethod("BuildCreateScript", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, [description])
    ?? throw new Exception("BuildCreateScript ausente"));

// These functions shadow every System Restore cmdlet. No machine settings are changed.
string Stubs(bool create, bool deny = false) => $$"""
    $script:points = @([pscustomobject]@{SequenceNumber=10; Description='old'})
    function Enable-ComputerRestore { param($Drive) if ($Drive -ne ($env:SystemDrive + '\')) { throw 'wrong drive' } }
    function Get-ComputerRestorePoint { $script:points }
    function Checkpoint-Computer {
        param($Description, $RestorePointType)
        {{(deny ? "Write-Error 'restore denied'" : "")}}
        {{(create ? "$script:points += [pscustomobject]@{SequenceNumber=11; Description=$Description}" : "Write-Warning 'frequency limit'")}}
    }
    """;
await Test("Restore point requires a new sequence", () => ExpectFailure(
    () => ProcessRunner.RunPowerShellAsync(Stubs(false) + RestoreScript("test")), "restauração"));
await Test("Restore point denial is failure", () => ExpectFailure(
    () => ProcessRunner.RunPowerShellAsync(Stubs(false, true) + RestoreScript("test")), "restore denied"));
await Test("Restore point verifies success and literal description", async () =>
{
    const string description = "Nico's $(throw 'expanded') $env:SystemDrive \"literal\"";
    var output = await ProcessRunner.RunPowerShellAsync(Stubs(true) + RestoreScript(description));
    Require(output == "PROJECT_BOOST_X_RESTORE_CREATED", "Token de sucesso inexato: " + output);
});
await Test("Restore point creation rejects empty or partial output", async () =>
{
    var method = typeof(RestorePointService).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .SingleOrDefault(m => m.Name == "CreateAsync" && m.GetParameters().Length == 2)
        ?? throw new Exception("Sobrecarga testável CreateAsync ausente");
    foreach (var output in new[] { "", "OK", "sucesso", "prefix PROJECT_BOOST_X_RESTORE_CREATED suffix" })
    {
        Func<string, int, Task<string>> execute = (_, _) => Task.FromResult(output);
        var success = await (Task<bool>)method.Invoke(null, ["test", execute])!;
        Require(!success, "Saída não verificada aceita: " + output);
    }
});
await Test("Operation failure clears busy and reports failure", async () =>
{
    var main = new MainViewModel();
    await main.RunOperationAsync("working", () => throw new InvalidOperationException("denied"));
    Require(!main.IsBusy, "Busy permaneceu ativo após exceção");
    Require(main.StatusMessage.Contains("denied"), "Falha não foi informada");
    Require(main.NavigateCommand.CanExecute("Optimizations"), "Navegação não reabilitada");
});
await Test("A write blocks another write, but navigation stays free", async () =>
{
    var main = new MainViewModel();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var running = main.RunOperationAsync("working", () => release.Task);
    Require(main.IsBusy, "Operação não marcou busy");
    Require(main.NavigateCommand.CanExecute("Optimizations"), "Navegação bloqueada durante gravação");
    main.NavigateCommand.Execute("Optimizations");
    Require(main.CurrentPage == "Optimizations", "Página não mudou durante a gravação");
    var secondRan = false;
    await main.RunOperationAsync("second", () => { secondRan = true; return Task.CompletedTask; });
    Require(!secondRan, "Gravações sobrepostas foram permitidas");
    release.SetResult();
    await running;
    Require(!main.IsBusy, "Busy permaneceu ativo");
});
await Test("Screen view models are built only on first navigation", async () =>
{
    Constructed.Names.Clear();
    var main = new MainViewModel();
    Require(Constructed.Names.SequenceEqual(["Home"]), "Abrir o app construiu mais do que a tela Início: " + string.Join(",", Constructed.Names));
    main.NavigateCommand.Execute("Optimizations");
    Require(Constructed.Names.SequenceEqual(["Home", "Optimizations"]), "Navegar não construiu a tela de destino");
    main.NavigateCommand.Execute("Home");
    main.NavigateCommand.Execute("Optimizations");
    Require(Constructed.Names.Count(n => n == "Optimizations") == 1, "A tela foi construída de novo");
    Require(ReferenceEquals(main.CurrentViewModel, main.Optimizations), "CurrentViewModel não é a tela ativa");
    await Task.Delay(50);
    Require(main.Optimizations.Activations == 2 && main.Optimizations.Deactivations == 1, "Ativação e desativação da tela não foram notificadas");
});
await Test("Navigating to an unknown page is ignored", () =>
{
    var main = new MainViewModel();
    main.NavigateCommand.Execute("Inexistente");
    Require(main.CurrentViewModel is HomeViewModel, "Página desconhecida trocou a tela");
    return Task.CompletedTask;
});
await Test("Cancellation remains busy until cleanup exits", async () =>
{
    var main = new MainViewModel();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelRequested = false;
    var running = main.RunOperationAsync("working", async () =>
    {
        main.SetCancellation(() => cancelRequested = true);
        await release.Task;
        throw new OperationCanceledException();
    });
    Require(main.CanCancelOperation, "Cancelamento indisponível");
    main.CancelOperationCommand.Execute(null);
    Require(cancelRequested && main.IsBusy, "Cancelamento liberou busy antes do encerramento");
    release.SetResult();
    await running;
    Require(!main.IsBusy && !main.CanCancelOperation, "Estado não foi liberado ao encerrar");
    Require(main.StatusMessage.Contains("cancelada"), "Cancelamento não informado");
});
await Test("Restore point explicit opt out skips Windows command", async () =>
{
    var main = new MainViewModel();
    var settings = BoostParaPc.Services.AppSettings.Current;
    settings.RequireRestorePoint = false;
    await main.RequireRestorePointAsync("This must never create a real restore point");
});
Console.WriteLine($"{testCount - failures.Count}/{testCount} passed");
return failures.Count == 0 ? 0 : 1;
