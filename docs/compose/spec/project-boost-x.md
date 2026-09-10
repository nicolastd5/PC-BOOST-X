---
feature: project-boost-x
status: designed
updated: 2026-09-10
branch: feature/project-boost-x
commits: 
---

# Project Boost X

## Report

## [S1] Problem
O produto é vendido e executado com o nome legado **Boost Para PC** / assembly `BoostParaPc`. O usuário pediu rebrand para **Project Boost X**, inspirado no visual/nome **MiMo X** — nome de produto, título da janela, sidebar, manifesto e binário de publish devem refletir a nova marca.

## [S2] Design
- Nome de produto visível: **Project Boost X**
- Tagline UI: otimizador de PC para jogos e tarefas (mantém PT-BR)
- Assembly / nome do `.exe` de publish: `ProjectBoostX`
- Namespace C#: **mantém** `BoostParaPc` (evita refactor massivo e regressões); só muda branding e assembly
- Paths de dados: migrar `AppData\BoostParaPc` → `AppData\ProjectBoostX` com fallback de leitura do diretório antigo (não perder backups/applied.json)
- Solução: `ProjectBoostX.sln`, projeto pasta `src/ProjectBoostX` (rename físico do projeto)
- Visual: manter tema dark navy/cyan; ajustar rótulo do logo/sidebar para **X** e título “Project Boost X”
- `app.manifest` assemblyIdentity name → `ProjectBoostX.app`
- Product/Description/Version no csproj atualizados

## [S3] Out of Scope
- Não renomear namespaces/classes internas
- Não mudar lógica de otimizações/jogos
- Não criar instalador/MSI
- Não alterar modelo de negócio/preços

## Tasks
- [ ] T1: Git worktree + spec — acceptance: branch `feature/project-boost-x` ativa (covers: S2)
- [ ] T2: Renomear projeto/solução/csproj/manifest — acceptance: `ProjectBoostX.sln` e `src/ProjectBoostX/ProjectBoostX.csproj` constroem (covers: S2; depends: T1)
- [ ] T3: Branding UI (título, sidebar, status) — acceptance: janela mostra “Project Boost X” (covers: S2; depends: T2)
- [ ] T4: Paths AppData com migração — acceptance: backups/estado leem de ProjectBoostX e ainda encontram BoostParaPc legado (covers: S2; depends: T2)
- [ ] T5: Publish + verificação de build — acceptance: `publish/ProjectBoostX.exe` existe e build Release sem erros (covers: S2; depends: T2,T3,T4)
