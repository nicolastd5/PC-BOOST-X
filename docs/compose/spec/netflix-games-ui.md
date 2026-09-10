---
feature: netflix-games-ui
status: designed
updated: 2026-09-10
branch: feature/netflix-games-ui
commits: 
---

# Galeria de jogos estilo Netflix

## Report

## [S1] Problem
A aba **Perfis por jogo** é uma lista vertical monótona de texto. O usuário quer catálogo visual tipo Netflix: capas/logos dos jogos em carrossel horizontal; ao clicar na capa, abre o painel com otimizações recomendadas daquele título.

## [S2] Design
- **Linha horizontal rolável** de cards ~160×220 (capa) com nome e plataforma sob a arte.
- **Capa**: baixa `header.jpg`/`library_600x900` do Steam CDN quando houver `SteamAppId`; cache em `AppData\ProjectBoostX\covers\{appid}.jpg`. Sem imagem: gradiente determinístico + iniciais + nome do jogo.
- **Clique no card** → painel de detalhes (mesma aba ou overlay): nome, plataforma, perfil recomendado, dicas, botões Aplicar recomendado / Reverter / Abrir pasta.
- **Seleção em lote** continua possível (checkbox pequeno no card ou “aplicar tudo visível”).
- Detecção de jogos inalterada (Steam ACF / Epic / registro / pastas).
- UI dark consistente com o tema Project Boost X (navy/cyan).

## [S3] Out of Scope
- Contas online, trailers, achivement APIs.
- Scraping de logos fora do Steam CDN.
- Redesign completo das outras abas.

## Tasks
- [ ] T1: Worktree + spec — acceptance: branch `feature/netflix-games-ui` ativa (covers: S2)
- [ ] T2: Serviço de capas (Steam CDN + cache + fallback gradiente) — acceptance: `CoverService` retorna caminho local ou null; cache criado (covers: S2; depends: T1)
- [ ] T3: UI Netflix na aba Perfis — acceptance: carrossel horizontal de capas; clique abre detalhes com dicas/otimizações (covers: S2; depends: T2)
- [ ] T4: Build Release + publish — acceptance: `ProjectBoostX.exe` builda sem erros (covers: S2; depends: T3)
