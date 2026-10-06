# Windows Agent e componentes compartilhados

O Agent v0.3 é um console C#/.NET 8. O runtime não depende do Studio aberto, browser ou servidor HTTP no PC. O tablet ouve em 8765 e o Agent faz conexão de saída. O [Studio](STUDIO.md) oferece a janela de customização e pode iniciar o Agent sem console.

## Desenvolver

```powershell
dotnet test LegacyDisplay.sln
dotnet run --project windows-agent/LegacyDisplay.Agent -- pair --device http://192.168.1.70:8765 --code 123456
dotnet run --project windows-agent/LegacyDisplay.Agent -- deploy --layout protocol/examples/dashboard.json
dotnet run --project windows-agent/LegacyDisplay.Agent -- run
```

Para utilizar o SDK instalado localmente neste checkout, substitua `dotnet` por `.\.tools\dotnet\dotnet.exe` ou execute `scripts/build.ps1 -Interop`.

Publicação sem exigir runtime instalado:

```powershell
dotnet publish windows-agent/LegacyDisplay.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/agent
```

O arquivo `LegacyDisplay.Agent.exe` inclui o runtime .NET. A pasta publicada também pode conter símbolos e bibliotecas nativas; mantenha a pasta completa ao distribuir. O script de build copia o APK para `artifacts/LegacyDisplay-v0.1-debug.apk`.

## Comandos

- `pair --device URL --code 123456`: obtém token e salva a credencial no usuário atual.
- `deploy --layout ARQUIVO`: valida localmente e envia o layout para persistência no tablet.
- `run [--interval 1000] [--demo] [--actions CAMINHO]`: transmite sensores e recebe ações. Intervalo permitido: 100–10000 ms.
- `sensors`: imprime valores reais locais, ids/unidades dos sensores GPU e motivo de indisponibilidade em JSON; não exige pareamento.
- `configure-mining --project CAMINHO [--actions CAMINHO]`: acrescenta os cadastros do [MiningRoutine](MININGROUTINE.md), sem executar seus comandos; não exige pareamento.
- `--device URL`: substitui o endereço salvo, útil após DHCP ou para `http://127.0.0.1:8765` com ADB.
- `--credentials CAMINHO`: usa outro arquivo em qualquer comando.
- `--help`: mostra ajuda. `Ctrl+C` encerra o runtime.

A credencial padrão fica em `%LOCALAPPDATA%\LegacyDisplay\agent-device.json`. O token é protegido por Windows DPAPI CurrentUser; outro usuário Windows não consegue reutilizar o conteúdo diretamente. O JSON guarda a URL e o blob protegido. Para esquecer o pareamento, use **Novo pareamento** no tablet e repita `pair`. O token nunca deve ser colocado no layout nem no Git.

Se o arquivo de credenciais não existir, `run`/`deploy` exibem instruções de pareamento e encerram com código 2. Execute `pair` e `run` no mesmo usuário Windows, usando o mesmo `--credentials` se escolher um caminho personalizado.

## Fontes reais

| Id | Unidade |
|---|---|
| `pc.cpu.usage` | % de uso total, após a segunda amostra |
| `pc.memory.usage` | % de RAM física |
| `pc.memory.usedMiB` | MiB |
| `pc.uptime.seconds` | segundos |
| `pc.network.rxBytesPerSecond` | bytes/s, soma de interfaces ativas sem loopback |
| `pc.network.txBytesPerSecond` | bytes/s, mesma agregação |
| `pc.gpu.name` | nome da primeira GPU ordenada pelo identificador da biblioteca |
| `pc.gpu.temperature` | °C, GPU Core |
| `pc.gpu.usage` | %, GPU Core |
| `pc.gpu.powerWatts` | W, GPU Package |
| `pc.gpu.memory.usedMiB` | MiB de VRAM utilizada |
| `pc.gpu.memory.totalMiB` | MiB de VRAM total |

O LibreHardwareMonitor 0.9.6 coleta a categoria GPU, sem habilitar CPU/placa-mãe/storage. As fontes `hw.*` identificam cada sensor disponível por placa. Amostras não finitas/ausentes são enviadas como null; sensores anteriormente conhecidos são limpos após falha, evitando manter uma temperatura antiga como leitura atual. Até 200 fontes específicas são preservadas por sessão. Identificadores duplicados da biblioteca recebem um sufixo determinístico do nome do sensor.

A consulta ao driver ocorre no máximo uma vez por segundo. `--interval` controla o envio das mensagens, não aumenta o polling GPU. O catálogo depende do modelo/driver; CPU térmica e Intel que exige CPU habilitada continuam pendentes. Referência: [código e distribuição da biblioteca](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6).

Com `--demo`, `demo.gpu.temperature` produz uma curva explicitamente simulada e separada dos sensores reais.

## Ações e runtime

O botão `demo.ping` confirma recebimento. O [editor de ações](ACTIONS.md) cadastra chamadas HTTP, aplicativos `.exe`, sequências e funções internas. Toda ação precisa estar habilitada no cadastro local e corresponder a um Button do layout atual. O tablet fornece IDs; os parâmetros executáveis são obtidos no PC. A abertura usa `ProcessStartInfo.ArgumentList`, com argumentos separados, sem interpretação automática por shell.

O cadastro padrão fica em `%LOCALAPPDATA%\LegacyDisplay\actions.json`. Apesar da extensão, contém um envelope com a configuração cifrada por DPAPI CurrentUser, criado pelo Studio; `run --actions` aponta para esse arquivo protegido, não para um JSON de parâmetros escrito manualmente. O Studio usa o caminho padrão e o transmite ao Agent que inicia. O catálogo é relido a cada toque, preservando uma fotografia durante a execução. Há uma execução por vez, limite cooperativo de oito segundos e nenhuma repetição automática de uma chamada HTTP.

O Agent reconecta automaticamente com backoff máximo de trinta segundos. Credencial revogada gera uma mensagem para repetir o pareamento e encerra com código 1. Serviço Windows, tray e início no login ainda não estão implementados. O Studio mantém o processo iniciado em segundo plano após fechar a janela e consegue retomá-lo ao reabrir, validando PID, instante de início e caminho do executável antes de parar.

O pacote inclui avisos de componentes de terceiros, textos de licença e referências ao código-fonte das bibliotecas usadas sem modificações. A licença do próprio LegacyDisplay ainda será escolhida pelo proprietário antes da publicação pública.
