# LegacyDisplay

Painéis touch locais para reaproveitar tablets Android antigos. Editor visual no Windows, métricas do PC e botões personalizáveis no Android 7 ou superior.

Este repositório implementa o **cliente Android v0.2 e o Studio/Agent Windows v0.3**: editor visual → layout JSON → Canvas Android → métricas Windows por WebSocket → toque no tablet → ação cadastrada no PC. O cliente usa Kotlin e Android Views, sem WebView, Compose ou serviços Google. A operação do painel não depende de conta, cloud ou internet; ações HTTP podem consultar serviços externos conforme sua configuração.

## O que está implementado

- Protocolo v1, JSON Schema e validação equivalente em Kotlin e C#.
- Widgets Text, Value e Button, posições absolutas, estilos e escala proporcional.
- Tela imersiva, orientação pelo layout, keep-screen-on configurável e início no boot do Android 7.
- Layout e configuração locais, restauração e fallback para o dashboard padrão.
- API REST e WebSocket no tablet, porta 8765, pareamento por código temporário e token de um único PC.
- Agent de console .NET 8: parear, enviar layout, transmitir CPU/RAM/rede/uptime reais e reconectar.
- Studio WPF: arraste/redimensionamento, propriedades, fontes, preview, JSON, undo/redo e Deploy.
- Pareamento e opções do tablet pela janela; detecção de um tablet USB autorizado por ADB.
- Agent iniciado em segundo plano pelo Studio e independente após fechar o editor.
- GPU real com LibreHardwareMonitor 0.9.6: temperatura/uso/potência/VRAM e catálogo dos sensores disponíveis.
- Editor de ações: chamadas HTTP, abertura de aplicativos, sequências e funções registradas em C#, além de `demo.ping`.
- Parâmetros das ações protegidos por DPAPI no PC; o tablet envia apenas o identificador do botão/ação.
- Integrações opcionais por ações HTTP, executáveis e fontes adicionais de métricas; veja [integrações](docs/INTEGRATIONS.md).
- Escurecimento do tablet por um sinal genérico de tela, com despertar temporário por toque.
- Credencial protegida por DPAPI para o usuário Windows; revogação local no tablet.

Novos widgets, descoberta LAN, troca automática USB/Wi-Fi, serviço/tray e integrações continuam no roadmap. O modo `--demo` adiciona apenas `demo.gpu.temperature`, um dado explicitamente simulado.

## Customização no Windows

Abra `artifacts/studio/LegacyDisplay.Studio.exe`. O Studio reutiliza a credencial do Agent e permite ler o layout do tablet, editar os widgets e enviar por **Deploy**. **Iniciar Agent** mantém os sensores funcionando após fechar o Studio. Use **Ações** para cadastrar uma chamada e selecione seu ID nas propriedades do botão. Consulte os guias do [Studio](docs/STUDIO.md) e de [ações personalizadas](docs/ACTIONS.md). Atualize os dois executáveis Windows e reinicie o Agent anterior; o APK existente é compatível.

Para copiar para outro PC, extraia `artifacts/LegacyDisplay-Windows.zip` inteiro, preservando as pastas `studio` e `agent`. O runtime .NET está incluído. O pacote contém também um dashboard com GPU real em `studio/layouts/dashboard-gpu.json`.

## Experimentar no tablet

1. Instale `artifacts/LegacyDisplay-v0.2-debug.apk` no Android 7 e abra **LegacyDisplay uma vez**. A instalação via ADB usa `adb install -r artifacts/LegacyDisplay-v0.2-debug.apk`. O diretório `artifacts` é gerado pelo build, não versionado.
2. Conecte PC e tablet à mesma LAN. O rodapé exibe `Código 123456 · 192.168.1.70:8765`. O código muda após até cinco minutos.
3. No Windows, usando o Agent publicado:

```powershell
.\artifacts\agent\LegacyDisplay.Agent.exe pair --device http://192.168.1.70:8765 --code 123456
.\artifacts\agent\LegacyDisplay.Agent.exe deploy --layout .\protocol\examples\dashboard.json
.\artifacts\agent\LegacyDisplay.Agent.exe run
```

CPU e memória começam a atualizar. Toque **TESTAR AÇÃO NO PC**: o Agent registra o toque e o tablet mostra **PC recebeu o toque!**. O Agent continua funcionando sem um editor aberto; `Ctrl+C` encerra o processo.

Mantenha pressionada uma área vazia ou o rodapé para abrir as configurações. Ali é possível alterar tela ligada/início no boot e escolher **Novo pareamento**, que revoga a credencial anterior. Após mudança de IP, use `run --device http://NOVO_IP:8765`; a credencial continua a mesma.

Se preferir USB manual, após autorizar a depuração no tablet:

```powershell
adb forward tcp:8765 tcp:8765
.\artifacts\agent\LegacyDisplay.Agent.exe pair --device http://127.0.0.1:8765 --code 123456
.\artifacts\agent\LegacyDisplay.Agent.exe run --device http://127.0.0.1:8765
```

Se já estiver pareado, basta o comando `run` com a URL de loopback. O botão **Detectar USB** do Studio configura automaticamente o encaminhamento para um único dispositivo autorizado.

## Compilar e verificar

Requisitos: Windows, .NET SDK 8, JDK 17–21, Android SDK com `platforms;android-35`, `build-tools;35.0.0` e `platform-tools`, e Python 3 para o teste de interoperabilidade. O Gradle Wrapper fixa a versão 8.11.1 e verifica seu SHA-256.

Configure `JAVA_HOME` e `ANDROID_HOME` ou `android-client/local.properties` (não versionado). Ferramentas locais em `.tools/dotnet` e `.tools/android-sdk` também são reconhecidas pelo script.

```powershell
.\scripts\build.ps1 -Interop
```

O script executa testes Windows/JVM, lint, interoperabilidade C# ↔ Kotlin e testes da interface WPF. Gera APK debug, publica Studio e Agent Windows x64 com runtime incluído e cria o ZIP. Consulte [Android](docs/ANDROID.md), [Windows](docs/WINDOWS.md) e os [resultados de verificação](docs/VERIFICATION.md).

## Estrutura e documentação

| Pasta | Responsabilidade |
|---|---|
| `protocol/` | Schema, exemplos, contrato e modelos C# |
| `android-client/` | Renderer e servidor local Kotlin |
| `windows-agent/` | Runtime Windows independente do editor |
| `windows-studio/` | Editor WPF e núcleo de edição/histórico |
| `windows-common/` | Clientes HTTP, credenciais, sensores, ações, USB e lançamento do Agent |
| `tests/`, `scripts/` | Verificação, build e teste entre linguagens |
| `docs/` | Arquitetura, roadmap, operação e validação no tablet |

Leia o [contrato](protocol/PROTOCOL.md), a [arquitetura](docs/ARCHITECTURE.md), o [roadmap](docs/ROADMAP.md) e o [roteiro de validação no SM-T561M](docs/VALIDATION.md). A [visão original](docs/VISION.md) foi preservada.

O pareamento usa HTTP/WebSocket sem TLS neste PoC: o token limita quem altera o painel, mas não protege contra captura de tráfego na LAN. Use uma rede local confiável ou o encaminhamento USB. O cliente não deve ser exposto à internet. Limites do NanoWSD e requisitos para endurecer o transporte estão no contrato.

## Licença

MIT, copyright Thiago Caffaro. Consulte [LICENSE](LICENSE). Dependências preservam suas próprias licenças; os avisos são reunidos no pacote pelo script de build.

A integração opcional com MiningRoutine está documentada separadamente e não é necessária para compilar, parear ou usar o painel. Este repositório não contém o controlador de mineração nem configurações de uma instalação pessoal.
