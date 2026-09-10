---
feature: project-boost-x
status: delivered
updated: 2026-09-10
branch: feature/project-boost-x
commits: e923977..799a555
---

# Project Boost X

## Report

**What was built** — Rebrand completo de **Boost Para PC** para **Project Boost X**: solução `ProjectBoostX.sln`, projeto em `src/ProjectBoostX`, assembly/exe `ProjectBoostX`, Product/Company/AssemblyTitle, título da janela, sidebar com logo “X”, MessageBox e labels de ponto de restauração. Dados migraram de `AppData\BoostParaPc` para `AppData\ProjectBoostX` via `AppPaths` (copia backups e `applied.json` se faltar no destino). Namespace C# permanece `BoostParaPc`.

**Verification** — `dotnet build ProjectBoostX.sln -c Release` PASS (0 erros; 4 warnings nullable pré-existentes em `steamPath`). `dotnet publish` → `publish/ProjectBoostX.exe` presente. Revisão independente: spec compliance PASS após fix de mojibake UTF-8; paths centralizados em `AppPaths`; sem `src/BoostParaPc` residual.

**Journey log** — (1) PowerShell `-replace`/`Set-Content` em massa corrompeu UTF-8 (mojibake `Ã§`) e pior: regex comeu identificadores em `RestorePointService`. (2) Fix: restaurar arquivos do commit baseline com `git show` e reaplicar só diffs pontuais via `File.ReadAllText`/`WriteAllText` UTF-8. (3) Sempre byte-check `Ã§`/`ooostrararc` após rename+edit em lote no Windows.

## [S1] Problem
O produto era executado com o nome legado **Boost Para PC** / assembly `BoostParaPc`. O usuário pediu rebrand para **Project Boost X**, inspirado no visual/nome **MiMo X**.

## [S2] Design
- Nome de produto visível: **Project Boost X**
- Assembly / nome do `.exe` de publish: `ProjectBoostX`
- Namespace C#: **mantém** `BoostParaPc`
- Paths: `AppData\ProjectBoostX` com migração do legado `BoostParaPc`
- Solução: `ProjectBoostX.sln`, `src/ProjectBoostX`
- Logo sidebar **X**; manifesto `ProjectBoostX.app` v2.1.0

## [S3] Out of Scope
- Não renomear namespaces/classes internas
- Não mudar lógica de otimizações/jogos
- Não criar instalador/MSI
- Não alterar modelo de negócio/preços

## Tasks
- [x] T1: Git worktree + spec — acceptance: branch `feature/project-boost-x` ativa (covers: S2)
- [x] T2: Renomear projeto/solução/csproj/manifest — acceptance: `ProjectBoostX.sln` e `src/ProjectBoostX/ProjectBoostX.csproj` constroem (covers: S2; depends: T1)
- [x] T3: Branding UI (título, sidebar, status) — acceptance: janela mostra “Project Boost X” (covers: S2; depends: T2)
- [x] T4: Paths AppData com migração — acceptance: backups/estado leem de ProjectBoostX e ainda encontram BoostParaPc legado (covers: S2; depends: T2)
- [x] T5: Publish + verificação de build — acceptance: `publish/ProjectBoostX.exe` existe e build Release sem erros (covers: S2; depends: T2,T3,T4)
