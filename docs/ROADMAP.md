# Roadmap

## v0.1 — Proof of Concept

Implementação de software: protocolo, Android nativo Text/Value/Button, kiosk, boot, persistência, REST/WebSocket, pareamento, Agent mínimo, CPU/RAM/rede/uptime e uma ação de confirmação. Pareamento e WebSocket foram antecipados para validar o caminho completo com autenticação desde o início.

**Validação parcial em hardware:** instalado no Tab E; pareamento/deploy, métricas e ações foram exercitados, com amostras de RAM/frames registradas. Boot, perda real de Wi-Fi, startup e estabilidade de horas continuam no [checklist](VALIDATION.md).

## v0.2 — Comunicação

**Implementado:** pareamento pela janela, detalhes/estado do dispositivo, retomada HTTP do endereço conhecido e detecção USB/ADB com porta local livre para um único tablet. **Pendente:** descoberta LAN por NSD/mDNS, escolha automática USB → Wi-Fi e verificação do USB/reconexão em hardware. Revisar limites do servidor/transporte antes de ampliar uso.

## v0.3 — Dashboard Engine

**Entrega Windows v0.3:** ações HTTP cadastradas localmente, abertura de aplicativos, sequências e funções registradas em C#, mantendo o protocolo v1 e o APK atual. **Pendente:** ProgressBar, Image, Gauge, Container e depois Graph/Clock/Toggle/Slider, páginas e fontes REST genéricas com extração JSON/limites de polling. Widgets continuam genéricos.

## v0.4 — Studio

**Entregas antecipadas:** WPF, Canvas editável Text/Value/Button, propriedades, preview, arquivos JSON, undo/redo, catálogo de fontes, Deploy persistente, configuração do tablet e lançamento independente do Agent. Editor de ações com parâmetros, teste local, persistência protegida e seleção por botão. **Pendente:** fontes configuráveis, edição transitória ao vivo e os widgets/páginas de v0.3. O milestone completo ainda não está encerrado.

## v0.5 — Monitoramento Windows

**Implementado parcialmente:** LibreHardwareMonitor 0.9.6 para GPU, sensores disponíveis por placa, temperatura/carga/potência/VRAM e CLI `sensors`. Verificado localmente com RTX 4070 SUPER. **Pendente:** CPU térmica/placa-mãe, Intel que depende de coleta CPU, tray, início no login e seleção persistida de sensores. Dados simulados continuam identificados.

## v0.6 — Integrações

Home Assistant e RainbowMiner por adaptadores de fontes/ações, sem acoplamento no renderer. APIs genéricas antes de integrações específicas.

## v1.0 — Uso diário

Instaladores, assinatura e atualização segura, documentação, provisioning, endurecimento de autenticação/transporte, estabilidade e configuração amigável. Decidir licença antes da primeira release pública.

## Organização futura no GitHub

Um Project com Backlog → Ready → In Progress → Testing → Done. Campos: Status, Area (Android/Studio/Agent/Protocol/Integration/Docs), Priority (P0/P1/P2) e Milestone. Limite informal de duas issues em progresso. Não há Project/issues remotos criados neste checkout; isso exige escolher um repositório de destino.

Próximas tarefas concretas: confirmar no tablet o layout produzido pelo Studio, USB, boot e recuperação Wi-Fi; então ampliar o engine com ProgressBar/Gauge/Image e suas verificações nativas. As entregas Windows antecipadas mantêm o protocolo v1 e o APK existente. Cloud, contas, MQTT, scripting, marketplace e desktop multiplataforma permanecem fora do MVP.
