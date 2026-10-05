# Boost X v3 — Roteiro das Fases 2 a 4

**Spec:** `docs/superpowers/specs/2026-10-05-boost-x-v3-design.md`
**Depende de:** `2026-10-05-boost-x-v3-fase-0-1-fundacao.md` concluído.

Este documento define, para cada fase, as tarefas, os arquivos, as interfaces, os detalhes técnicos que não são óbvios (chaves, APIs, comandos) e o critério de aceite. O passo a passo com código de cada fase é escrito quando a fase começa, por dois motivos: o código depende das interfaces reais que a fase anterior entregar, e a Fase 2 depende do resultado de um teste de viabilidade (Tarefa 2.1).

Restrições globais: as mesmas do plano da fundação (nenhuma dependência NuGet nova, testes nunca tocam o Windows real, backup com dono antes de qualquer gravação, itens avançados fora de presets, pt-BR).

Cada fase termina com: build Release sem erros, todos os conjuntos de testes passando, lista de verificação manual da fase executada no app e `dotnet publish`.

---

## Fase 2 — Interface

**Objetivo:** aparência nativa do Windows 11, uma tela por arquivo, criação sob demanda, nada bloqueando a janela inteira.

### 2.1 Teste de viabilidade do tema Fluent

- **Arquivos:** `App.xaml`, `ProjectBoostX.csproj` (branch descartável).
- **O que testar:** `ThemeMode="Dark"` em `App.xaml` e `<NoWarn>$(NoWarn);WPF0001</NoWarn>` (a API ainda é experimental no .NET 10); remover `Themes/DarkTheme.xaml` dos dicionários.
- **Perguntas a responder, com captura de tela de cada uma:**
  1. Fundo Mica aparece no Windows 11? E no Windows 10 o fundo é sólido e legível?
  2. CheckBox, ScrollBar, ToolTip, ProgressBar, TextBox e ComboBox saem estilizados?
  3. O ciano (`#3DE0FF`) pode ser a cor de destaque sobrescrevendo os recursos de acento do tema? Se a sobrescrita for frágil, usar a cor de destaque do sistema e manter o ciano só no logotipo e nos gráficos.
  4. Estilos próprios herdam o Fluent com `BasedOn="{StaticResource {x:Type Button}}"`?
- **Aceite:** decisão registrada no topo do plano detalhado da Fase 2. Se 1 ou 2 falharem, plano B: manter `DarkTheme.xaml` e escrever templates próprios para CheckBox, ScrollBar e ToolTip, com barra de título escura via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)`.

### 2.2 Estrutura, navegação e modelo de operação

- **Arquivos:** `MainWindow.xaml` (vira só a moldura), `ViewModels/MainViewModel.cs`, `Views/*.xaml` (uma `UserControl` por tela), `ViewModels/*.cs` (um arquivo por viewmodel; `FeatureViewModels.cs` deixa de existir), `tests/Commands.Tests/*`.
- **Interfaces:**
  - `record NavItem(string Key, string Label, string Glyph)`; lista fixa em `MainViewModel.NavItems`.
  - `MainViewModel.CurrentViewModel : object`, resolvido por `Dictionary<string, Lazy<object>>`. A tela é escolhida por `DataTemplate` com `DataType`. `PageToVisibilityConverter` é apagado.
  - `MainViewModel.RunOperationAsync` continua sendo o bloqueio global, agora só para **gravações**. Varreduras usam `IsLoading` do próprio viewmodel e não impedem a navegação.
- **Ícones:** fonte `Segoe Fluent Icons, Segoe MDL2 Assets`. Glifos: Início `E80F`, Otimizações `EC4A`, Jogos `E7FC`, Limpeza `EA99`, Inicialização `E7E8`, Monitor `E9D9`, Histórico `E81C`, Configurações `E713`. Conferir cada um no Mapa de Caracteres antes de usar.
- **Testes:** em `Commands.Tests`, o caso "Busy blocks navigation and overlapping operations" passa a afirmar que uma gravação bloqueia outra gravação e que a navegação continua livre. Novo caso: viewmodel de tela só é construído na primeira navegação.
- **Aceite:** abrir o app não constrói nenhum viewmodel além do de Início; navegar durante uma varredura de limpeza funciona.

### 2.3 Componentes compartilhados

- **Arquivos:** `Themes/Controls.xaml`, `Converters/*.cs`.
- **Conteúdo:** cartão, etiqueta de risco (verde/amarelo/vermelho por `DataTrigger`), texto de estado, barra de aviso (sucesso/erro/informação, substitui a mensagem só na barra de status), cabeçalho de seção, barra de progresso da operação na área de conteúdo (substitui a sobreposição de tela inteira).
- **Aceite:** nenhuma tela define cor ou raio de borda localmente.

### 2.4 Tela Otimizações

- **Arquivos:** `Views/OptimizationsView.xaml`, `ViewModels/OptimizationsViewModel.cs`. Apaga `DebloatViewModel` e a tela Serviços (os serviços já são itens do catálogo).
- **Comportamento:** filtro por categoria e por risco, busca por texto (`CollectionViewSource.Filter`), lista virtualizada (`ListBox` com `VirtualizingStackPanel`), aplicar/reverter por item, aplicar selecionados, botões de preset "Seguro" e "Equilibrado" (`OptimizationCatalog.Preset`), seção Ferramentas com os itens `IsAction`. Cada item mostra "O que faz" (`Description`) e "Quando não usar" (`Caution`).
- **Testes:** função pura de filtro (categoria + risco + texto) em `Catalog.Tests`.
- **Aceite:** os 37 itens alcançáveis; preset nunca marca item avançado nem contraindicado.

### 2.5 Tela Início

- **Arquivos:** `Views/HomeView.xaml`, `ViewModels/HomeViewModel.cs` (substitui `DashboardViewModel`).
- **Conteúdo nesta fase:** cartões de hardware, contagem "X de Y otimizações aplicadas", botão "Otimizar agora" (preset Seguro, com resumo do que será alterado antes de confirmar), quatro mini-indicadores ao vivo alimentados pelo serviço do Monitor. A nota de saúde e o antes/depois entram nas Fases 3 e 4.

### 2.6 Tela Jogos

- **Arquivos:** `Views/GamesView.xaml`, `ViewModels/GamesViewModel.cs`, `Services/CoverService.cs`.
- **Comportamento:** grade de capas (`WrapPanel`) em vez de carrossel com altura fixa; os cartões aparecem imediatamente com a capa provisória e as reais chegam com `Parallel.ForEachAsync` (4 em paralelo), sem bloquear a tela; painel de detalhe lateral; as quatro caixas "(manual)" viram opções dentro do painel do jogo.
- **Aceite:** com 30 jogos detectados, a grade aparece em menos de 1 s e nenhuma capa fica presa na provisória quando há rede.

### 2.7 Telas Limpeza e Inicialização

- **Arquivos:** `Views/CleanupView.xaml`, `Views/StartupView.xaml` e os viewmodels correspondentes.
- **Mudança:** só estrutura e estilo; carregamento local em vez de bloqueio global. Funcionalidade nova fica para a Fase 3.

### 2.8 Monitor

- **Arquivos:** `Services/PerformanceMonitorService.cs`, `Views/MonitorView.xaml`, `ViewModels/MonitorViewModel.cs`, `Controls/Sparkline.cs`.
- **Contadores:**

  | Medida | Categoria \ contador \ instância |
  |---|---|
  | CPU | `Processor Information \ % Processor Utility \ _Total` (cai para `% Processor Time` se ausente) |
  | RAM | `Memory \ Available MBytes` |
  | Disco | `PhysicalDisk \ % Disk Time \ _Total` |
  | Rede | soma de `Network Interface \ Bytes Total/sec` em todas as interfaces |
  | GPU | soma de `GPU Engine \ Utilization Percentage` nas instâncias `*engtype_3D`, limitada a 100 |
  | VRAM | soma de `GPU Adapter Memory \ Dedicated Usage` |
  | Processos | `System \ Processes` |

- **Regras:** contadores criados em `Task.Run` no primeiro `Start()`, nunca no construtor; instâncias de GPU mudam conforme processos abrem e fecham, então ler a categoria inteira por amostra com `PerformanceCounterCategory.ReadCategory()`; atualização da interface com `Dispatcher.BeginInvoke`; histórico de 60 amostras por medida.
- **Testes:** `Sparkline.ToPoints(valores, largura, altura, máximo)` é função pura, testada em `Catalog.Tests`.
- **Aceite:** abrir o app não cria `PerformanceCounter`; CPU difere do Gerenciador de Tarefas em no máximo 3 pontos.

### 2.9 Configurações e primeira execução

- **Arquivos:** `Services/AppSettings.cs` (JSON em `AppData\ProjectBoostX\settings.json`), `Views/SettingsView.xaml`, `Views/FirstRunDialog.xaml`.
- **Campos:** `RequireRestorePoint` (sai das cinco telas e fica só aqui), `FirstRunAcknowledged`. As Fases 3 e 4 acrescentam os seus.
- **Conteúdo:** ponto de restauração obrigatório, criar ponto agora, reverter tudo, abrir pasta de dados, sobre (versão, licença, link do projeto).
- **Primeira execução:** três frases — o que o programa altera, que tudo tem backup e pode ser revertido, e que itens avançados são opcionais.
- **Testes:** `AppSettings` com arquivo ausente, corrompido e de versão futura devolve os padrões sem lançar.

### 2.10 Revisão de acessibilidade e de layout

Lista de verificação manual: ordem de Tab em cada tela; todo botão de ícone com `AutomationProperties.Name`; Narrador lê nome, estado e risco de um item; contraste AA nos textos secundários; escala 125% e 150%; janela em 960×620; Windows 10 22H2.

---

## Fase 3 — Recursos

**Objetivo:** otimizações que mudam o desempenho de forma mensurável, em vez de mais chaves de Registro.

### 3.1 Diagnóstico e nota de saúde

- **Arquivos:** `Services/Diagnostics/DiagnosticInputs.cs`, `DiagnosticsProbe.cs` (coleta, fala com o Windows), `Diagnostics.cs` (`Evaluate(DiagnosticInputs) : IReadOnlyList<Finding>`, função pura), `tests/Diagnostics.Tests/*`; cartão na tela Início.
- **Interface:** `record Finding(string Id, Severity Severity, string Title, string Detail, string? FixLabel, string? FixUri)`; `Severity` = `Info | Warning | Critical`. Nota = `100 − 15×críticos − 7×avisos`, mínimo 0.
- **Verificações e de onde vem cada dado:**

  | Verificação | Fonte | Correção oferecida |
  |---|---|---|
  | Monitor abaixo da taxa máxima | `EnumDisplaySettings`: modo atual vs. maior frequência na mesma resolução | `ms-settings:display-advanced` |
  | RAM em velocidade base | `Win32_PhysicalMemory.ConfiguredClockSpeed` + `SMBIOSMemoryType` (DDR4 ≤ 2666, DDR5 ≤ 4800). Texto diz "pode estar"; é heurística | Explicação de XMP/EXPO na BIOS |
  | RAM em canal único | um único módulo em `Win32_PhysicalMemory` | Explicação |
  | Driver de GPU antigo | `Win32_VideoController.DriverDate` > 180 dias | Link do fabricante pelo nome da GPU |
  | TRIM desligado | `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\DisableDeleteNotification` ≠ 0 | Ferramenta TRIM (3.2) |
  | C: com menos de 15% livre | `DriveInfo` | Tela Limpeza |
  | Paginação desativada | `…\Session Manager\Memory Management\PagingFiles` vazio | `SystemPropertiesPerformance.exe` |
  | Reinício pendente | `…\Component Based Servicing\RebootPending`, `…\WindowsUpdate\Auto Update\RebootRequired`, `PendingFileRenameOperations` | Aviso |
  | Notebook na bateria | `GetSystemPowerStatus` | Aviso |
  | Jogo instalado em HD | letra do executável → `MSFT_Partition` → `MSFT_PhysicalDisk.MediaType` (generalizar `GetSystemDiskType`) | Aviso |
  | TCP autotuning desligado | `Get-NetTCPSetting` (em segundo plano) | `SetTcpAutoTuningAsync("normal", "diag.tcp")` |
  | Integridade de Memória ligada | `…\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` = 1 | Informativo; leva ao item `sec.memoryintegrity` |

- **Testes:** um caso por verificação com `DiagnosticInputs` falsos, mais "dado ausente não vira achado" para cada campo anulável.
- **Aceite:** nesta máquina, o resultado bate com a conferência manual de cada linha.

### 3.2 Ferramentas nativas

- **Arquivos:** `Services/Native/MemoryNative.cs`, `Services/Catalog/Tools.cs`.
- `tools.standby`: `NtSetSystemInformation(SystemMemoryListInformation = 80, MemoryPurgeStandbyList = 4)`, depois de habilitar `SeProfileSingleProcessPrivilege` com `AdjustTokenPrivileges`. Mostra a RAM em espera antes e depois.
- `tools.trim`: `Optimize-Volume -DriveLetter X -ReTrim` para cada volume em SSD; cancelável.
- **Aceite:** RAMMap mostra a lista Standby reduzida após `tools.standby`.

### 3.3 Itens novos do catálogo

| Id | Gravação | Risco | Observações |
|---|---|---|---|
| `gpu.windowed` | `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, valor `DirectXUserGlobalSettings`: acrescentar `SwapEffectUpgradeEnable=1;` preservando os outros campos (mesma lógica de `GameProfileService.MergeGpuPreferences`) | Seguro | Só Windows 11 |
| `net.deliveryopt` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` = 0 | Seguro | |
| `ui.bingsearch` | `HKCU\Software\Policies\Microsoft\Windows\Explorer\DisableSearchBoxSuggestions` = 1 | Seguro | |
| `ui.widgets` | `HKLM\SOFTWARE\Policies\Microsoft\Dsh\AllowNewsAndInterests` = 0 | Seguro | Só Windows 11 |
| `edge.background` | `HKLM\SOFTWARE\Policies\Microsoft\Edge\StartupBoostEnabled` = 0 e `BackgroundModeEnabled` = 0 | Seguro | |
| `sec.memoryintegrity` | `HKLM\…\HypervisorEnforcedCodeIntegrity\Enabled` = 0 | Avançado | Exige reinício e confirmação com o texto do risco. Não recomendado quando `Locked` = 1 (travado pelo firmware) |

- **Apps pré-instalados:** seção própria em Otimizações. Lista fixa de nomes de pacote (Notícias, Clima, Solitaire, Obter Ajuda, Dicas, Hub de Comentários, Filmes e TV, Groove, Office Hub, Clipchamp, Power Automate, To Do, Cortana). `Get-AppxPackage` mostra só os instalados; `Remove-AppxPackage` remove os marcados, para o usuário atual. Cada nome é validado por expressão regular antes de entrar no comando. Não há reversão; a tela oferece o link da Store de cada app.
- **Testes:** os itens novos entram automaticamente no teste de ida e volta do catálogo; teste de fusão de `DirectXUserGlobalSettings` preservando campos existentes; teste de rejeição de nome de pacote inválido.

### 3.4 Modo Jogo automático

- **Arquivos:** `Services/GameSessionService.cs`, `Services/Native/ProcessNative.cs`, `tests/GameSession.Tests/*`, campos em `AppSettings`, ícone de bandeja em `App.xaml.cs`.
- **Detecção:** `ManagementEventWatcher` em `Win32_ProcessStartTrace`, filtrando pelos nomes de executável dos jogos detectados. Fim da sessão por `Process.WaitForExitAsync`.
- **Ao iniciar o jogo, nesta ordem:**
  1. Gravar `session.json` (pid e hora de início do jogo; lista vazia de alterações).
  2. Jogo em prioridade Alta (`SetPriorityClass`).
  3. Para cada processo da lista de apps de fundo: guardar prioridade original, aplicar modo eficiência (`SetProcessInformation` com `ProcessPowerThrottling`, `ControlMask = StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED`) e prioridade Abaixo do normal. Cada alteração é acrescentada a `session.json` antes de ser feita.
  4. Notificações: guardar `ToastEnabled` e gravar 0.
  5. Limpar a standby (3.2).
- **Ao fechar o jogo:** desfazer na ordem inversa a partir de `session.json` e apagar o arquivo. Processos são identificados por pid **e** hora de início, para não tocar em um processo que reutilizou o pid.
- **Recuperação:** se `session.json` existe ao abrir o programa e o jogo não está mais rodando, restaurar antes de qualquer outra coisa.
- **Lista de apps de fundo:** editável em Configurações. Padrão conservador: `OneDrive.exe`, `ms-teams.exe`, `Teams.exe`. Nunca tocados, mesmo que o usuário os adicione: o próprio jogo, processos de anti-cheat conhecidos, `Discord.exe`, `obs64.exe`, `audiodg.exe`.
- **Bandeja:** `<UseWindowsForms>true</UseWindowsForms>` e `System.Windows.Forms.NotifyIcon`. Fechar a janela minimiza para a bandeja só quando o Modo Jogo automático está ligado.
- **Iniciar com o Windows:** `schtasks /create /tn "ProjectBoostX" /tr "\"<exe>\" --tray" /sc onlogon /rl highest /f`. Uma chave `Run` não serve: o programa exige administrador e pediria UAC a cada logon.
- **Testes (`GameSession.Tests`, com `ProcessNative` substituído na compilação, como os outros conjuntos):** sessão normal restaura tudo; sessão interrompida é restaurada na abertura seguinte; pid reutilizado não é tocado; processo da lista de proteção nunca é alterado; falha ao alterar um processo não impede os demais.
- **Aceite:** abrir e fechar um jogo real deixa prioridades e notificações exatamente como estavam (conferido no Gerenciador de Tarefas).

### 3.5 Limpeza ampliada

- **Arquivos:** `Services/CleanupService.cs`, `tests/CleanupStartup.Tests/*`.
- **Alvos novos:**
  - Navegadores Chromium, todos os perfis (`Default` e `Profile N`): `Cache\Cache_Data`, `Code Cache`, `GPUCache` em `%LOCALAPPDATA%\Google\Chrome\User Data`, `Microsoft\Edge\User Data`, `BraveSoftware\Brave-Browser\User Data`.
  - Shaders: `%LOCALAPPDATA%\NVIDIA\DXCache`, `NVIDIA\GLCache`, `AMD\DxCache`, `AMD\DxcCache`, `Intel\ShaderCache`. Steam `steamapps\shadercache` entra **desmarcado**.
  - Otimização de Entrega: `Delete-DeliveryOptimizationCache -Force`.
  - Relatório de Erros: `%ProgramData%\Microsoft\Windows\WER\ReportQueue` e `ReportArchive`.
  - Apps: `%APPDATA%\discord\Cache`, `Code Cache`, `GPUCache`; `%LOCALAPPDATA%\Spotify\Data`.
  - Lixeira: tamanho por `SHQueryRecycleBin`.
- **Testes:** `BuildTargets` com pastas de fixture gera um alvo por perfil; as proteções existentes (`IsSafeRoot`, junções) valem para os alvos novos.

### 3.6 Inicialização: tarefas agendadas

- **Arquivos:** `Services/StartupService.cs`, `Services/SystemSettingsBackupService.cs` (`TasksState` passa a guardar o caminho da tarefa), `tests/SystemSettings.Tests/*`.
- **Fonte:** `Get-ScheduledTask` com gatilho de logon e `TaskPath` fora de `\Microsoft\`. Desativar e reativar pelo journal, dono `startup.task.<nome>`.

### 3.7 DNS

- **Arquivos:** `Services/DnsBenchmark.cs`, `Services/Catalog/Tools.cs`, journal ganha o tipo `Dns`.
- **Medição:** consulta UDP direta a cada servidor (atual, 1.1.1.1, 8.8.8.8, 9.9.9.9), 3 domínios × 3 repetições, mediana.
- **Troca:** `Set-DnsClientServerAddress -InterfaceIndex N -ServerAddresses a,b`; o journal guarda os endereços anteriores ou "automático" (`-ResetServerAddresses`).
- **Testes:** montagem e leitura do pacote DNS (função pura); ida e volta do journal com o processo falso.

---

## Fase 4 — Medição e distribuição

**Objetivo:** provar o resultado e entregar um arquivo que qualquer pessoa consiga rodar.

### 4.1 Antes e depois

- **Arquivos:** `Services/SystemSnapshot.cs`, cartão na tela Início.
- **Interface:** `record SystemSnapshot(DateTime TakenUtc, int? BootMs, int RamUsedMb, int Processes, int RunningServices, int StartupItems)`.
- **Tempo de boot:** último evento 100 do log `Microsoft-Windows-Diagnostics-Performance/Operational` (`EventLogReader`), campo `BootTime`.
- **Regra:** o primeiro retrato é salvo em `baseline.json` antes da primeira otimização. O cartão compara o retrato atual com ele e avisa que o tempo de boot só muda depois de reiniciar.
- **Testes:** comparação com campo ausente; `baseline.json` nunca é sobrescrito.

### 4.2 Histórico e exportação

- **Arquivos:** `Views/HistoryView.xaml`, `ViewModels/HistoryViewModel.cs`, `Services/ActionLog.cs` (`Read()`).
- **Comportamento:** lista do `actions.jsonl`, mais recentes primeiro, filtro sucesso/falha, botão Reverter nas linhas de aplicação ainda ativas. Linhas corrompidas são ignoradas.
- **Exportar para suporte:** zip com o log e as informações de hardware. O caminho do perfil do usuário é trocado por `%USERPROFILE%`; nada é enviado.
- **Erros não tratados:** o manipulador de `App.xaml.cs` grava no log antes de mostrar a mensagem.

### 4.3 Publicação em arquivo único

- **Arquivos:** `ProjectBoostX.csproj`, `app.manifest`, `Assets/app.ico`.
- **Propriedades:** `PublishSingleFile`, `SelfContained`, `RuntimeIdentifier=win-x64`, `PublishReadyToRun`, `IncludeNativeLibrariesForSelfExtract`, `EnableCompressionInSingleFile`, `ApplicationIcon`, `Version=3.0.0`. WPF não suporta trimming; não usar `PublishTrimmed`.
- **Limpeza:** apagar de `publish/` os arquivos antigos `BoostParaPc.*`.
- **Medição:** tempo entre o início do processo e o evento `Loaded`, gravado no log; comparar antes e depois do ReadyToRun.
- **Aceite:** um único `.exe` roda em uma máquina sem .NET instalado.

### 4.4 Verificação de atualização

- **Arquivos:** `Services/UpdateService.cs`, opção em Configurações.
- **Comportamento:** `GET https://api.github.com/repos/<dono>/<repo>/releases/latest` com cabeçalho `User-Agent`; compara a tag com a versão do assembly; mostra aviso com link. Sem download nem instalação automática. Pode ser desligada.
- **Pré-requisito do usuário:** criar o repositório no GitHub (hoje o projeto não tem remoto) e publicar releases com tag `vX.Y.Z`.
- **Testes:** comparação de versões (função pura); falha de rede não mostra erro.

### 4.5 Documentação

`README.md` em pt-BR: o que o programa faz e o que não faz, como reverter, por que o SmartScreen avisa (executável sem assinatura) e como prosseguir, o que fazer se o antivírus acusar, declaração de que nenhum dado é enviado. `LICENSE`: a escolha da licença é do usuário e é perguntada nesta tarefa.

### 4.6 Verificação final

Em uma instalação limpa de Windows 11 e outra de Windows 10 22H2, uma em pt-BR e outra em en-US: abrir, aplicar o preset Seguro, reiniciar, conferir o antes/depois, reverter tudo, conferir que o Diagnóstico e o Registro voltaram ao estado inicial. Esta máquina é Windows 11 Home, sem Hyper-V nem Sandbox; usar VirtualBox ou um segundo computador.

---

## Cobertura da spec

| Seção da spec | Onde é implementada |
|---|---|
| 3.1–3.5 Arquitetura | Fundação, Tarefas 2–7 |
| 3.6 Estrutura de arquivos | Fundação (Catalog, Native) e 2.2 (Views, ViewModels) |
| 4 Catálogo revisado | Fundação, Tarefa 5; itens novos em 3.3; ferramentas em 3.2 |
| 5.1 Navegação | 2.2 |
| 5.2 Comportamento | 2.2, 2.3, 2.4, 2.6, 2.9, 2.10 |
| 5.3 Monitor | 2.8 |
| 6.1 Diagnóstico | 3.1 |
| 6.2 Modo Jogo automático | 3.4 |
| 6.3 Limpeza ampliada | 3.5 |
| 6.4 Inicialização | 3.6 |
| 6.5 DNS | 3.7 |
| 6.6 Antes e depois | 4.1 |
| 7 Distribuição | 4.2–4.6 |
| 9 Testes | cada tarefa; conjuntos novos em Fundação (Catalog), 3.1 (Diagnostics), 3.4 (GameSession) |
