---
feature: netflix-games-ui
status: delivered
updated: 2026-09-10
branch: feature/netflix-games-ui
commits: 147c45b..6aee885
---

# Galeria de jogos estilo Netflix

## Report

**What was built** — Aba de jogos virou catálogo visual: carrossel horizontal de capas (148×200), clique abre painel com perfil recomendado, dicas, caminho do exe e botões **Aplicar recomendado** / **Reverter este jogo**. `CoverService` baixa arte do Steam CDN (library_600x900/header) com cache em `AppData\ProjectBoostX\covers\`; sem AppId gera gradiente + iniciais. Detecção e ações em lote continuam.

**Verification** — `dotnet publish` Release win-x64: 0 erros (warnings nullable pré-existentes). Revisão: spec compliance PASS após fixes (AppId Genshin removido; clicar não força seleção em lote).

**Journey log** — (1) AppId do Genshin apontava para Overwatch 2; removido (sem Steam). (2) `OpenGame` marcava `IsSelected=true` e engolia o jogo no apply em lote. (3) Abrir pasta ficou fora do MVP (spec atualizada). (4) NuGet `path1` ainda quebra restore em paths com espaço se faltarem env vars do Windows.

## [S1] Problem
Lista vertical de texto sem capas; usuário pediu UX tipo Netflix com logos e clique para ver otimizações.

## [S2] Design
- Carrossel horizontal de cards com capa + nome + plataforma
- Capa: Steam CDN + cache; fallback gradiente/iniciais
- Clique → painel: recomendação, dicas, aplicar/reverter
- Detecção de jogos inalterada

## [S3] Out of Scope
- Contas online, trailers, scrapers não-Steam
- Redesign de outras abas
- Botão “Abrir pasta” (fora do MVP)

## Tasks
- [x] T1: Worktree + spec — acceptance: branch `feature/netflix-games-ui` ativa (covers: S2)
- [x] T2: Serviço de capas — acceptance: `CoverService` CDN+cache+fallback (covers: S2; depends: T1)
- [x] T3: UI Netflix — acceptance: carrossel + painel de detalhes com dicas/otimizações (covers: S2; depends: T2)
- [x] T4: Build Release + publish — acceptance: `ProjectBoostX.exe` sem erros (covers: S2; depends: T3)
