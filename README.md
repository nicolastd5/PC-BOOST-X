# Project Boost X

Otimizador de Windows 10 e 11 para jogos e uso diário, em português. Cada ajuste é descrito, aplicado e revertido individualmente, com backup antes de qualquer alteração.

## O que faz

- **Otimizações** — 45 itens (energia, jogos, entrada, visual, privacidade, serviços, rede, sistema), cada um com o que faz, o impacto, o risco e *quando não usar*. Presets "Seguro" e "Equilibrado" só **marcam** os itens; você revisa e aplica. Itens avançados nunca entram em preset.
- **Adequação ao seu PC** — ajustes contraindicados (core parking em notebook, CPU híbrida ou X3D; SysMain em HD; hibernação em notebook) aparecem marcados como "não recomendado" e ficam fora da seleção automática.
- **Diagnóstico** — nota de saúde e pontos de atenção: taxa de atualização do monitor, RAM em velocidade base ou canal único, driver de vídeo antigo, TRIM, espaço livre, paginação, reinício pendente, jogos em HD e outros.
- **Jogos** — detecta Steam, Epic, Riot e outros; perfis recomendados por jogo (prioridade, GPU, tela cheia).
- **Modo Jogo automático** (opcional) — enquanto um jogo detectado roda: prioridade alta para ele, apps de fundo em modo de eficiência, notificações silenciadas e memória em espera limpa. Tudo volta ao normal quando o jogo fecha, mesmo que o programa feche no meio.
- **Limpeza** — temporários, caches de navegadores (todos os perfis), shaders, relatórios de erro, apps e lixeira. Nada de arquivos pessoais.
- **Inicialização** — programas do Registro, pasta Inicializar e tarefas agendadas de logon, com reativação.
- **Monitor** — CPU, memória, disco, rede, GPU e VRAM ao vivo.
- **Antes e depois** — retrato do PC antes da primeira otimização e comparação depois.
- **Histórico** — tudo o que foi aplicado ou revertido, com exportação para suporte (zip local, nada é enviado).

## O que NÃO faz

- Não envia dados para lugar nenhum. Não há telemetria, conta nem rede além da verificação de atualização (opcional, só consulta o GitHub).
- Não mexe em anti-cheat, não instala drivers, não "limpa o Registro" e não promete milagres: os ganhos dependem do seu PC e são medidos na tela Início.
- Não remove apps pré-instalados sem você marcar cada um; essa remoção é a única ação **sem reversão automática** (reinstale pela Microsoft Store).

## Como reverter

- Item por item: botão **Reverter** na tela Otimizações ou no Histórico.
- Tudo de uma vez: **Reverter tudo** em Início ou Configurações.
- O programa também cria um ponto de restauração do Windows antes de aplicar (dá para desligar em Configurações).
- Os backups ficam em `%APPDATA%\ProjectBoostX\backups`; o histórico, em `%APPDATA%\ProjectBoostX\log\actions.jsonl`.

## Instalação e requisitos

Baixe o `ProjectBoostX.exe` (arquivo único, não exige .NET instalado) e execute. O programa pede permissão de administrador porque altera configurações do sistema.

### O SmartScreen avisou "O Windows protegeu o computador"

O executável **não é assinado digitalmente** (certificado de assinatura é pago), então o SmartScreen mostra esse aviso para arquivos novos. Para continuar: **Mais informações → Executar assim mesmo**. Se preferir, compile você mesmo (abaixo).

### O antivírus acusou o arquivo

Programas que alteram serviços, Registro e prioridade de processos são, por natureza, parecidos com o comportamento de malware, e executáveis sem assinatura caem em heurísticas. É um falso positivo, mas não peça para confiar às cegas: o código-fonte está completo aqui, e o histórico (`actions.jsonl`) mostra exatamente o que foi feito. Adicione uma exceção apenas se você compilou o arquivo ou confia na origem.

## Compilar

Requer o SDK do .NET 10.

```powershell
dotnet build ProjectBoostX.sln -c Release
dotnet publish src/ProjectBoostX/ProjectBoostX.csproj -c Release -o publish   # arquivo único
./tests/run-all.ps1                                                            # todos os testes
```

Os testes nunca tocam o Registro, serviços ou processos reais: usam um Registro e um sistema de processos em memória.

## Licença

A licença ainda não foi escolhida. Enquanto não houver um arquivo `LICENSE`, todos os direitos são reservados ao autor.
