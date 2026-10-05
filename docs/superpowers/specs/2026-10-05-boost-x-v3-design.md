# Project Boost X v3 — design

Data: 2026-10-05 · Estado: aguardando revisão

## 1. Objetivo

Transformar o Project Boost X (WPF, .NET 8, v2.1) num otimizador gratuito que outras pessoas possam baixar e usar com segurança: todo ajuste é honesto sobre o que faz, reversível individualmente e adequado ao hardware; a interface tem aparência nativa do Windows 11; e o programa mede o resultado em vez de prometer.

### Decisões do usuário (05/10/2026)

| Tema | Decisão |
|---|---|
| Público | Distribuição gratuita para outras pessoas |
| Risco | Seguros e moderados por padrão; avançados opcionais, desmarcados, com aviso |
| Visual | Fluent do Windows 11 (Mica, ícones Segoe Fluent), ciano como destaque |
| Sensores | Sem temperatura; só contadores nativos, sem dependência nova |

### Critérios de sucesso

1. Todas as otimizações do catálogo aparecem na interface e cada uma pode ser aplicada e revertida sozinha.
2. Nenhum ajuste do catálogo grava um valor que já é o padrão do Windows nem descreve algo que não faz.
3. Em notebook, CPU híbrida ou X3D, os ajustes contraindicados aparecem marcados como "não recomendado para este PC" e nunca entram em preset.
4. A janela abre sem criar contadores de desempenho nem iniciar processos externos na thread da UI.
5. O executável publicado é um único arquivo que roda sem .NET instalado, em Windows 10 22H2 e Windows 11, em qualquer idioma do sistema.
6. Os 5 conjuntos de testes existentes continuam passando; cada subsistema novo ganha o seu.

## 2. Abordagem

Três caminhos foram considerados:

- **A. Refatorar no lugar, em .NET 10 (escolhido).** Mantém WPF, MVVM e os serviços já testados. O WPF do .NET 9+ traz tema Fluent nativo (`ThemeMode`), que aplica Mica e estiliza todos os controles sem biblioteca externa. O .NET 8 sai de suporte em novembro de 2026.
- **B. Reescrever em WinUI 3.** Fluent mais completo, mas joga fora todo o XAML e complica um app que exige administrador. Custo alto, ganho só visual.
- **C. Ficar no .NET 8 com biblioteca de UI de terceiros.** Adiciona dependência e mantém o runtime prestes a perder suporte.

Risco conhecido de A: `ThemeMode` ainda é API experimental no .NET 10 (aviso `WPF0001`) e estilos customizados perdem a aparência Fluent se não herdarem do estilo do tema. Por isso a Fase 2 começa com um teste de viabilidade; se falhar, o plano B é manter `DarkTheme.xaml` e escrever templates próprios para CheckBox, ScrollBar e ToolTip.

## 3. Arquitetura

### 3.1 Catálogo declarativo

Hoje cada ajuste está espalhado em três lugares que já divergem: o `switch` de aplicar, as listas de backup e o `switch` de detecção. Passa a ser descrito uma vez:

```csharp
public sealed record RegValue(RegistryKey Root, string Key, string Name, object Value, RegistryValueKind Kind);

public partial class OptimizationItem
{
    public IReadOnlyList<RegValue> Registry { get; init; } = [];
    public Func<string, Task>? ApplyExtra { get; init; }      // recebe o Id (dono dos backups)
    public Func<bool>? IsAppliedExtra { get; init; }
    public Func<SystemInfo, string?>? NotRecommended { get; init; } // texto do motivo, ou null
    public bool RequiresRestart { get; init; }
    public bool IsAction { get; init; }                       // ação pontual, sem estado
}
```

- **Aplicar**: grava cada `RegValue`, depois chama `ApplyExtra`.
- **Detectar**: todos os `RegValue` iguais ao valor atual e `IsAppliedExtra` verdadeiro. O arquivo `applied.json` deixa de ser fonte de verdade para itens detectáveis.
- **Reverter**: restaura os backups cujo dono é o `Id` do item.

Dois delegates cobrem os casos que não são Registro (energia, serviços, tarefas); não há hierarquia de classes.

`OptimizationCatalog.All` é uma lista única compartilhada. Dashboard e demais telas passam a ver as mesmas instâncias, o que elimina o estado dessincronizado.

### 3.2 Backups com dono

- `RegistryBackupService.SetDword/SetString` recebem o id do dono; o backup automático já existente passa a ser nomeado `{id}_…json`. `RestoreMatchingBackups(id)` já existe.
- `SystemSettingsBackupService.Journal` ganha o campo `Owner`; novo `RevertAsync(owner)`.
- As listas explícitas `BackupExtra(...)` do catálogo são apagadas: todo `Set*` já tira snapshot antes de gravar.
- Backups antigos sem dono continuam restauráveis por "Reverter tudo".

### 3.3 Motor

`OptimizationEngine` com `ApplyAsync(item)`, `RevertAsync(item)` e `Refresh(items)`. Cada chamada grava uma linha no `ActionLog` (JSONL em `AppData\ProjectBoostX\log`): data, id, ação, resultado, mensagem.

### 3.4 Regras de hardware

`SystemInfo` ganha `IsHybridCpu` (núcleos com classes de eficiência diferentes, via `GetLogicalProcessorInformationEx`) e `IsX3D` (nome da CPU). Regras:

| Condição | Itens marcados como não recomendados |
|---|---|
| Notebook | CPU mínima 100%, ASPM desligado, core parking, desativar suspensão |
| CPU híbrida ou X3D | core parking |
| Disco do sistema é HD | desativar SysMain (já existe) |
| Windows 10 | itens exclusivos do Windows 11 ficam ocultos |

### 3.5 APIs nativas no lugar de processos

| Hoje | Passa a ser | Motivo |
|---|---|---|
| `Win32_DiskDrive.MediaType` | `MSFT_PhysicalDisk` do disco que contém C: | Hoje todo SSD aparece como "HD" |
| `powercfg /query` para ler valores (detecção e journal) | `powrprof.dll` (`PowerGetActiveScheme`, `PowerReadFriendlyName`, `PowerReadACValueIndex`, `PowerReadDCValueIndex`) | **Bug confirmado**: o texto do `powercfg` é traduzido, a leitura procura "Current AC" e por isso os 6 ajustes de valor de energia falham em Windows em português. `/query` também não mostra ajustes ocultos (core parking). Além disso são ~10 processos na abertura |
| 22 chamadas `sc qc`/`sc query` | Tipo de inicialização lido do Registro (`Services\<nome>\Start`); a tela Serviços deixa de existir, pois os serviços são itens do catálogo | Lento, bloqueante e dependente do idioma |
| PowerShell para `Get-PhysicalDisk` | A mesma consulta de disco acima | ~500 ms por chamada |

Gravações continuam usando `powercfg`/`sc`, que já estão cobertas por testes. Todo `.GetAwaiter().GetResult()` sai.

### 3.6 Estrutura de arquivos

```
src/ProjectBoostX/
  Views/            uma UserControl por tela
  ViewModels/       um arquivo por viewmodel (FeatureViewModels.cs é dividido)
  Services/Catalog/ um arquivo por categoria do catálogo
  Services/Native/  P/Invoke (energia, memória, monitor, processos)
```

## 4. Catálogo revisado

**Removidos** (placebo, obsoleto ou duplicado):

- `memory.pagedpool`, `memory.large`, `sys.priority.sep`, `services.remoteregistry`: gravam o que já é o padrão do Windows (conferido nesta máquina: `Win32PrioritySeparation=2`, Remote Registry já desativado).
- `net.tcp`: "normal" é o padrão; vira verificação do Diagnóstico (só age se outro programa tiver desligado).
- `telemetry.cortana`: a Cortana foi removida do Windows 11.
- `input.mousespeed`: fundido em `input.mouseaccel`.
- `net.throttle.off`: fundido em `game.mmprofile`, que perde as chaves `Tasks\Games` (quase nenhum jogo se registra nessa tarefa).
- `memory.standby`, `sys.flushdns`: esvaziavam working sets, não a standby list.

O catálogo vai de 48 para 37 itens na Fase 1. Backups de itens removidos continuam restauráveis por "Reverter tudo".

**Viram ações em "Ferramentas"**: limpar DNS (Fase 1); limpar memória standby reimplementada com `NtSetSystemInformation`, a limpeza real, e otimizar/TRIM dos SSDs (Fase 3).

**Corrigidos**:

- Nenhum valor de Registro é gravado por dois itens (hoje `game.mode`, `game.fso`, `game.dvr` e `game.bartips` se sobrepõem).
- `visual.animations` grava `DragFullWindows` como texto; `visual.effects` deixa de gravá-lo.
- `memory.ntfs` grava só o último acesso (`0x80000001`); a parte de nomes 8.3 sai.
- `telemetry.backgroundapps` usa a política `LetAppsRunInBackground`, que o Windows 11 respeita.
- `sys.corepark` usa o GUID do ajuste (o apelido `CPMINCORES` não existe em todas as máquinas) e passa para a categoria Energia.
- `game.mmprofile` grava `SystemResponsiveness=10`, o mínimo que o Windows aceita (0 é tratado como 10).
- `game.notifications` sai do catálogo permanente e passa a ser parte do Modo Jogo (temporário).

**Novos**

| Id | O que faz | Risco |
|---|---|---|
| `gpu.windowed` | Otimizações para jogos em janela (Win11): `SwapEffectUpgradeEnable=1` | Seguro |
| `net.deliveryopt` | Desliga envio P2P do Windows Update | Seguro |
| `ui.bingsearch` | Remove resultados da web do menu Iniciar | Seguro |
| `ui.widgets` | Desativa Widgets | Seguro |
| `edge.background` | Edge sem "inicialização rápida" nem execução em segundo plano | Seguro |
| `sec.memoryintegrity` | Desliga Integridade de Memória (recomendação da própria Microsoft para jogos; reduz segurança; exige reinício) | Avançado |
| `apps.preinstalled` | Remove apps pré-instalados de uma lista fixa, escolhidos um a um; reinstalação só pela Store | Avançado |

**Presets**: "Seguro" (risco Seguro, recomendado para o PC) e "Equilibrado" (acrescenta Moderado). Itens avançados nunca entram em preset.

## 5. Interface

### 5.1 Navegação

Barra lateral com ícones Segoe Fluent Icons (fallback Segoe MDL2 Assets no Windows 10):

1. **Início** — nota de saúde, 4 mini-indicadores ao vivo, "Otimizar agora" (preset), diagnóstico resumido, comparação antes/depois.
2. **Otimizações** — todas as categorias, filtro por categoria e risco, busca, botão aplicar/reverter por item, etiqueta "requer reinício", Ferramentas.
3. **Jogos** — grade de capas, painel de detalhe, perfil por jogo, chave do Modo Jogo automático.
4. **Limpeza**
5. **Inicialização**
6. **Monitor**
7. **Histórico**
8. **Configurações** — ponto de restauração obrigatório (uma vez só, não em cada tela), iniciar com o Windows, bandeja, reverter tudo, exportar log, verificar atualização, sobre.

### 5.2 Comportamento

- Telas e viewmodels são criados na primeira visita.
- Varreduras (limpeza, jogos, inicialização, serviços) mostram carregamento na própria tela e não bloqueiam a navegação. Só gravações passam pelo bloqueio global, com barra de progresso na área de conteúdo em vez de sobrepor a janela.
- Cores com significado: risco Seguro verde, Moderado amarelo, Avançado vermelho; estado Aplicado verde, Falhou vermelho.
- Capas dos jogos: os cartões aparecem na hora com capa provisória e as reais chegam em paralelo (4 por vez).
- Primeira execução: aviso curto sobre o que o programa altera e como reverter.
- Cada otimização tem dois textos para leigos: "O que faz" e "Quando não usar".
- Acessibilidade: tudo navegável por teclado, nome acessível em botões de ícone, contraste AA.

### 5.3 Monitor

CPU (`% Processor Utility`), RAM, GPU e VRAM (`GPU Engine`, `GPU Adapter Memory`), disco, rede (soma das interfaces), cada um com gráfico dos últimos 60 s desenhado com `Polyline`. Contadores criados fora da thread da UI, só quando a tela abre; contagem de processos pelo contador `System\Processes`.

## 6. Recursos novos

### 6.1 Diagnóstico

Verificações somente leitura, cada uma com gravidade, explicação e atalho para corrigir:

- Monitor abaixo da taxa de atualização máxima
- RAM na velocidade base (XMP/EXPO provavelmente desligado — heurística, descrita como tal)
- Driver de GPU com mais de 6 meses
- TRIM desligado
- Menos de 15% livre em C:
- Arquivo de paginação desativado
- Reinício pendente
- Notebook na bateria
- Jogos instalados em HD
- Integridade de Memória ligada (informativo)

A nota de saúde do Início é derivada dessas verificações.

### 6.2 Modo Jogo automático

`GameSessionService` observa início e fim de processos (`Win32_ProcessStartTrace`/`StopTrace`) para os executáveis dos jogos detectados. Ao iniciar o jogo: prioridade alta para o jogo, modo eficiência (EcoQoS + prioridade baixa) para uma lista configurável de apps de fundo, notificações desligadas, limpeza da standby. Ao fechar: restaura exatamente o estado anterior.

O estado da sessão é gravado em disco antes de qualquer alteração; se o programa fechar no meio, a próxima abertura restaura. Exige o programa na bandeja (`NotifyIcon`) e, opcionalmente, iniciar com o Windows por tarefa agendada com privilégio elevado.

### 6.3 Limpeza ampliada

Todos os perfis de Chrome/Edge/Brave (hoje só "Default"), caches de shader de NVIDIA/AMD/Intel, cache de Otimização de Entrega, fila do Relatório de Erros, caches de Discord/Teams/Spotify. Cache de shader do Steam entra desmarcado. Tamanho da lixeira passa a ser exibido.

### 6.4 Inicialização

Além de Registro e pasta Inicializar, lista tarefas agendadas de logon que não são da Microsoft, com desativar/reativar.

### 6.5 DNS

Ferramenta que mede o tempo de resposta do DNS atual e de Cloudflare, Google e Quad9, e permite trocar; o valor anterior fica no journal.

### 6.6 Antes e depois

Retrato do sistema (tempo de boot pelo log de eventos, RAM em uso, processos, serviços ativos, itens de inicialização) tirado antes da primeira otimização e comparável a qualquer momento.

## 7. Distribuição

- Publicação em arquivo único, autocontido, win-x64, com ReadyToRun.
- Ícone, versão 3.0.0, tela Sobre, README e licença.
- Verificação de atualização: consulta à última release no GitHub e link para baixar. Exige criar o repositório remoto, que hoje não existe.
- Erros não tratados vão para o log, que pode ser exportado para suporte.
- O programa não envia nenhum dado.

## 8. Fora do escopo

- Temperaturas e sobreposição de FPS dentro do jogo.
- Instalador e assinatura de código. Sem assinatura, o SmartScreen avisará no primeiro uso; o README explica.
- Atualização automática que se instala sozinha.
- Ajustes de painel de NVIDIA/AMD, modo MSI, resolução de timer.
- Outros idiomas de interface além de pt-BR.
- Licenciamento ou versão paga.

## 9. Testes

Mantém o padrão atual: executáveis de console sem dependências, que compilam o código real e substituem só o limite do sistema (Registro em memória, processos falsos).

- **Catalog.Tests**: motor e catálogo. Ids únicos; nenhum valor gravado por dois itens; para cada item, aplicar → detectado como aplicado → reverter → sistema idêntico ao inicial; reversão por dono não toca outro item; falha no meio desfaz o que foi gravado ou preserva o backup.
- **Diagnostics.Tests**: cada verificação com entradas falsas.
- **GameSession.Tests**: sessão interrompida é restaurada na abertura seguinte.
- Interface: lista de verificação manual por tela, executada no app real.

Nenhum teste aplica otimização na máquina de desenvolvimento.

## 10. Fases

Cada fase termina com o programa funcionando e publicável.

| Fase | Conteúdo |
|---|---|
| 0. Base | Commit do trabalho pendente, .NET 10 |
| 1. Fundação | Catálogo declarativo, backups com dono, motor, log, regras de hardware, APIs nativas, catálogo revisado |
| 2. Interface | Tema Fluent, navegação, telas separadas, carregamento sob demanda, Monitor novo |
| 3. Recursos | Diagnóstico, Modo Jogo automático, itens novos, limpeza e inicialização ampliadas, DNS |
| 4. Medição e distribuição | Antes/depois, Histórico, arquivo único, atualização, README |
