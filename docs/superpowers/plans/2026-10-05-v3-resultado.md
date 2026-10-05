# Boost X v3 — Resultado da execução (Fases 0 a 4)

Branch `feature/v3`. Testes: `tests/run-all.ps1` (8 conjuntos, nenhum toca o Windows real).

## Decisão do teste de viabilidade do tema Fluent (Tarefa 2.1)

Testado no Windows 11 com `ThemeMode="Dark"` em `App.xaml` (`WPF0001` suprimido), capturas de tela do app rodando.

1. **Janela e controles**: barra de título escura, cantos arredondados e CheckBox/ScrollBar/ComboBox/TextBox/ProgressBar/ToolTip saem no estilo Fluent. ✔
2. **Cor de destaque**: o ciano `#3DE0FF` **não** sobrescreve o acento do tema. Decisão: o Fluent cuida dos controles (acento = cor do sistema) e `DarkTheme.xaml` continua fornecendo as cores da marca (fundo, cartões, botões primários, chips), que passam por cima. ✔
3. **Fundo Mica**: não usado de propósito. O fundo sólido escuro da marca dá o mesmo resultado no Windows 10 e no 11.
4. **Estilos próprios herdam o Fluent**: os estilos implícitos de `DarkTheme.xaml` não definem `Template` para CheckBox/ScrollViewer/ListBox, então o template Fluent é mantido. ✔
5. **Windows 10 22H2**: **não testado** (decisão do usuário: sem máquina virtual).

## O que foi entregue

| Fase | Entregue |
|---|---|
| 0–1 Fundação | Catálogo declarativo (agora 45 itens), motor aplicar/detectar/reverter, backups com dono, energia lida pela API nativa, telas ligadas ao motor |
| 2 Interface | Tema Fluent, uma `View` por tela, `MainViewModel` com criação sob demanda (`Lazy`), navegação livre durante gravações (barra fina com Cancelar em vez de tela cheia bloqueada), componentes compartilhados em `Themes/Controls.xaml`, telas Início/Otimizações/Jogos/Limpeza/Inicialização/Monitor/Histórico/Configurações, primeira execução |
| 3 Recursos | Diagnóstico com nota, ferramentas (memória em espera, TRIM), itens novos, apps pré-instalados, Modo Jogo automático com bandeja, limpeza ampliada, tarefas agendadas na Inicialização, benchmark e troca de DNS |
| 4 Distribuição | Antes/depois, histórico + exportação de suporte, verificação de atualização, `.exe` único (82 MB, ReadyToRun, não exige .NET), README |

## Limites conhecidos (não testados em máquina real)

- **Windows 10**: sem teste.
- **Modo Jogo automático** (`ManagementEventWatcher`, prioridade e eficiência de processos reais): a lógica de restauração é coberta por testes com tabela de processos falsa, mas a execução real exige administrador e um jogo; **não foi exercitada**.
- **Limpar memória em espera** e **TRIM**: chamadas nativas/PowerShell reais, só testadas com stubs.
- **Troca de DNS**: lógica e journal testados com processo falso; o PowerShell real não foi executado.
- **Remoção de apps pré-instalados**: nome validado e testado; execução real não exercitada. Única ação sem reversão automática.
- **Lista de Otimizações**: está dentro de um `ScrollViewer` único (junto de Ferramentas, Apps e DNS), então a virtualização do `ListBox` não age; com 42 itens não faz diferença perceptível.
- **Escala de DPI 125%/150% e Narrador**: não conferidos. Janela em 960×620 conferida visualmente.
- Tempo de abertura medido no log: ~1 s (4,3 s na primeira execução do `.exe` único, que se extrai sozinho).

## Pendências do usuário

- Escolher a **licença** (`LICENSE`): a escolha é sua; o README diz que ainda não foi feita.
- Criar o **repositório no GitHub** e preencher `UpdateService.Repository` (`"dono/repo"`) para ligar a verificação de atualização.
- Conferência manual com administrador (UAC): aplicar/reverter itens, Modo Jogo com um jogo real, DNS, TRIM, antes/depois depois de reiniciar.
- Verificação final em Windows 10 e em instalação limpa (Tarefa 4.6).
