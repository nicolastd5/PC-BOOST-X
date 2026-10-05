# Roda todos os conjuntos de teste. Nenhum deles toca o Registro, processos ou serviços reais do Windows.
$ErrorActionPreference = 'Stop'
$failed = @()
foreach ($suite in 'Backup', 'CleanupStartup', 'GameProfiles', 'Commands', 'SystemSettings', 'Catalog', 'Diagnostics', 'GameSession') {
    Write-Host "== $suite"
    dotnet run --project "$PSScriptRoot/$suite.Tests" -c Release | Select-Object -Last 2
    if ($LASTEXITCODE -ne 0) { $failed += $suite }
}
if ($failed) { Write-Host "FALHOU: $($failed -join ', ')"; exit 1 }
Write-Host 'Todos os conjuntos passaram.'
