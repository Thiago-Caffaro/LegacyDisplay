Visão do projeto
Objetivo: transformar tablets Android antigos e fracos em painéis touch dedicados, altamente customizáveis, capazes de monitorar e controlar computadores, Home Assistant e serviços locais.
O primeiro dispositivo-alvo será:
Samsung Galaxy Tab E SM-T561M
Android 7 / LineageOS
800 × 1280
~1,5 GB RAM
hardware incapaz de executar confortavelmente a web moderna, mas perfeitamente capaz de executar uma UI dedicada.

A proposta não é fazer o tablet voltar a ser um tablet convencional.
A proposta é:
reaproveitar completamente o hardware como um smart display local.

Algo conceitualmente próximo de uma TURZX, só que muito maior, touch, programável e sem ficar preso a sensores específicos.
Princípios do produto
Eu fixaria estas decisões desde o começo:
Decisão	Escolha
Funcionamento	Local-first
Conta/login obrigatório	Não
Cloud obrigatório	Não
Internet obrigatória	Não
Dispositivo inicial	SM-T561M
Plataforma cliente	Android
Android inicial	7.x
PC inicial	Windows
Transporte principal	Wi-Fi
Transporte alternativo	USB
Configuração	Aplicativo Windows
Renderização	Nativa como arquitetura principal
Layout	JSON declarativo
Custom HTML	Futuro/opcional
Automação	REST/WebSocket inicialmente
MQTT	Posteriormente
Home Assistant	Integração planejada
Código	Open source recomendado
Arquitetura	Genérica, não específica ao Tab E


Isso é importante porque evita uma armadilha: fazer um programa extremamente adaptado à sua configuração de mineração e depois descobrir que nada é reutilizável.
Nome dos componentes
Independentemente do nome final do produto, eu usaria esta separação conceitual:
Project
│
├── Android Client
│
│   painel que roda no tablet
│
├── Studio
│
│   editor/configurador Windows
│
├── Agent
│
│   serviço leve executado no Windows
│
└── Protocol
    formato e comunicação compartilhados

Por exemplo, se o produto eventualmente se chamasse RevivePanel:
RevivePanel Client
RevivePanel Studio
RevivePanel Agent
RevivePanel Protocol

Arquitetura geral
                   WINDOWS PC
┌───────────────────────────────────────────────┐
│                                               │
│  Studio.exe                                   │
│  ┌─────────────────────────────────────────┐  │
│  │ Editor visual                           │  │
│  │ Preview                                 │  │
│  │ Widgets                                 │  │
│  │ Data Sources                            │  │
│  │ Actions                                 │  │
│  │ Configuração dos dispositivos           │  │
│  └─────────────────────────────────────────┘  │
│                                               │
│  Agent.exe                                    │
│  ┌─────────────────────────────────────────┐  │
│  │ Sensores Windows                        │  │
│  │ LibreHardwareMonitor                    │  │
│  │ RainbowMiner                            │  │
│  │ Estado do PC                            │  │
│  │ Comandos                                │  │
│  │ WebSocket                               │  │
│  └─────────────────────────────────────────┘  │
│                                               │
└───────────────┬───────────────────────────────┘
                │
          Wi-Fi │ USB/ADB
                │
                ▼
┌───────────────────────────────────────────────┐
│             ANDROID CLIENT                    │
│                                               │
│  Layout Engine                               │
│  Widget Engine                               │
│  REST Client/Server                          │
│  WebSocket                                   │
│  Actions                                     │
│  Kiosk                                       │
│  Device Manager                              │
│                                               │
│          800 × 1280 touchscreen              │
└───────────────┬───────────────────────────────┘
                │
             LAN│
                ▼
       ┌─────────────────┐
       │ Home Assistant  │
       │ APIs locais     │
       │ outros serviços │
       └─────────────────┘

Android Client
Essa é a parte mais crítica do projeto.
Eu usaria:
Kotlin
Android Views
Custom View / Canvas
JSON
NanoHTTPD/NanoWSD ou equivalente leve

E evitaria deliberadamente:
Jetpack Compose
Flutter
React Native
Electron
frameworks web grandes
browser completo

Compose é excelente em aparelhos modernos, mas não temos nada a ganhar colocando uma camada dessas sobre um Cortex-A7/Mali-400 com 1,5 GB de RAM.
Renderer: mudança importante na ideia original
Eu não faria HTML/CSS ser a tecnologia fundamental da tela.
Podemos continuar suportando isso futuramente, mas eu colocaria o layout JSON + renderer nativo no centro da arquitetura.
Por exemplo:
{
  "version": 1,
  "screen": {
    "width": 800,
    "height": 1280
  },
  "widgets": [
    {
      "id": "gpu-temp",
      "type": "text",
      "x": 50,
      "y": 80,
      "width": 300,
      "height": 100,
      "source": "pc.gpu.temperature",
      "format": "{value} °C",
      "style": {
        "fontSize": 52,
        "alignment": "center"
      }
    }
  ]
}

O Android interpreta isso e desenha diretamente.
Isso nos dá três vantagens enormes.
Primeiro, o tablet não depende de um WebView de Android 7.
Segundo, o Studio não depende do mecanismo de renderização do tablet.
Terceiro, no futuro podemos ter vários renderers:
             Layout JSON
                  │
       ┌──────────┴──────────┐
       │                     │
       ▼                     ▼
Native Android           Web renderer
Canvas/Views            HTML/CSS opcional

Sistema de widgets
A primeira versão não precisa de dezenas.
Eu começaria com:
Text
Value
ProgressBar
Button
Image
Gauge
Container

Depois:
Graph
Clock
Icon
Toggle
Slider
Image sequence
Network graph
Sensor group

Cada widget recebe:
posição
tamanho
estilo
fonte de dados
formatação
ação opcional

Estilo parecido com CSS
Mesmo usando renderer nativo, podemos tornar a configuração familiar.
Por exemplo:
"style": {
  "background": "#181818",
  "color": "#FFFFFF",
  "fontSize": 42,
  "fontWeight": "bold",
  "borderRadius": 16,
  "padding": 12,
  "opacity": 1.0
}

Ou seja, ganhamos boa parte da ergonomia do CSS sem carregar um browser inteiro.
Data Sources
Esse provavelmente será um dos recursos mais poderosos do projeto.
Um widget não deveria saber nada sobre GPU, mineração ou Home Assistant.
Ele simplesmente pede:
pc.gpu.temperature

O sistema de Data Sources resolve de onde aquilo vem.
Exemplo:
pc.gpu.temperature
        │
        ▼
Windows Agent
        │
LibreHardwareMonitor

Enquanto:
home.office.power
        │
        ▼
REST
        │
Home Assistant

Data Source REST genérico
Algo como:
{
  "id": "home.pc.power",
  "type": "rest",
  "url": "http://homeassistant:8123/api/states/sensor.pc_power",
  "method": "GET",
  "interval": 5000,
  "path": "$.state"
}

Permite conectar praticamente qualquer API local sem escrever plugin.
WebSocket
Para valores atualizados rapidamente, REST polling é desnecessário.
Por exemplo:
GPU usage
CPU usage
VRAM
network traffic
hashrate
power

podem utilizar:
Agent
   │
   │ WebSocket persistente
   ▼
Tablet

Talvez 1–10 atualizações por segundo dependendo do sensor.
Não precisamos atualizar temperatura 60 vezes por segundo.
Comunicação Wi-Fi
O tablet terá um listener local, por exemplo:
192.168.1.70:8765

com algo parecido com:
GET  /api/status
GET  /api/layout
POST /api/layout

GET  /api/config
PUT  /api/config

POST /api/action

WS   /api/live

Mais tarde podemos versionar:
/api/v1/...

Descoberta automática
Não quero que o usuário precise ficar digitando IP.
Idealmente:
Studio
  │
  │ mDNS / Android NSD
  ▼
Tablet encontrado

e aparece:
Galaxy Tab E
SM-T561M

192.168.1.70
Android 7
800×1280

[ Connect ]

USB
A grande vantagem é que não precisamos desenvolver um protocolo USB próprio.
Para a primeira implementação:
ADB
+
port forwarding

O Studio detecta:
adb devices

e cria:
adb forward tcp:8765 tcp:8765

Portanto:
Wi-Fi

192.168.1.70:8765

e:
USB

127.0.0.1:8765

usam exatamente a mesma API.
No Studio:
Connection

● Automatic
○ USB
○ Wi-Fi

Em automático:
USB?
 │
 ├─ yes → USB
 │
 └─ no → Wi-Fi

Windows Studio
Aqui eu escolheria sem muita dúvida:
C#
.NET 8
WPF

Por enquanto eu não usaria Avalonia nem WinUI 3.
WPF atende perfeitamente:
drag-and-drop
Canvas
data binding
property editor
preview
tree de objetos
undo/redo
serialização JSON

e é muito maduro.
Interface do Studio
A ideia seria algo próximo disso:
┌─────────────────────────────────────────────────────┐
│ Device: Galaxy Tab E     ● USB         [Deploy]     │
├──────────┬──────────────────────────────┬───────────┤
│ Widgets  │                              │Properties │
│          │       800 × 1280             │           │
│ Text     │                              │ X: 40     │
│ Value    │     GPU                      │ Y: 120    │
│ Button   │     58°C                     │ W: 300    │
│ Gauge    │                              │ H: 100    │
│ Image    │ █████████████░               │           │
│ Graph    │                              │ Source:   │
│          │ [ STOP MINING ]              │ gpu.temp  │
│          │                              │           │
├──────────┴──────────────────────────────┴───────────┤
│ Data Sources │ Actions │ Devices │ Logs             │
└─────────────────────────────────────────────────────┘

O preview utiliza o mesmo schema JSON do tablet.
Edição ao vivo
Esse recurso valeria bastante a pena.
Você move:
GPU Temperature

no Windows.
O Studio manda:
{
  "type": "widget.update",
  "id": "gpu-temp",
  "x": 300,
  "y": 400
}

O tablet altera imediatamente.
Assim temos:
WYSIWYG no hardware real.

O botão Deploy persiste a versão definitiva.
Windows Agent
O Studio não deve precisar ficar aberto.
Isso é uma decisão arquitetural importante.
Teremos:
Studio.exe

para configuração.
E:
Agent.exe

para runtime.
O Agent pode iniciar com o Windows e ficar praticamente invisível.
Funções do Agent
Inicialmente:
CPU usage
RAM
GPU usage
GPU temperature
GPU power
VRAM
network
Windows uptime
idle time
foreground application

Depois:
RainbowMiner
miner atual
coin
algorithm
hashrate
profit
power
mining status

E eventualmente o seu controlador de:
mineração
monitores
LEDs
Attack Shark
Bluetooth LED strip
etc.

poderia expor informações para ele. 
LibreHardwareMonitor
É um encaixe muito natural para o Agent.
Em vez de tentarmos implementar manualmente suporte para NVIDIA, AMD, Intel, CPU etc., usamos uma camada já consolidada de sensores.
Então podemos expor:
hardware.gpu.0.temperature
hardware.gpu.0.load
hardware.gpu.0.power
hardware.cpu.temperature
hardware.cpu.load

Actions
Widgets também poderão controlar coisas.
Exemplo:
[ STOP MINING ]

associado a:
{
  "type": "rest",
  "method": "POST",
  "url": "http://pc:9123/api/mining/stop"
}

Ou uma action do Agent:
{
  "type": "agent",
  "action": "mining.stop"
}

Home Assistant
Também encaixa muito bem.
Podemos chegar a:
Tablet
 │
 ├─ PC
 │   ├ GPU
 │   └ mining
 │
 └─ Home Assistant
     ├ tomada
     ├ TV
     ├ LEDs
     ├ energia
     └ automações

A ideia é não reinventar automação residencial.
O painel só serve como interface.
Kiosk Android
Quando o aparelho liga:
Android boot
     ↓
Client iniciado
     ↓
fullscreen
     ↓
conexão
     ↓
último layout salvo

Precisamos de:
BOOT_COMPLETED
immersive fullscreen
KEEP_SCREEN_ON opcional
reconnect automático
disable sleep opcional
start on boot
restore layout

E posteriormente:
default launcher mode
lock task/device owner

Tela sempre ligada
Eu deixaria configurável, e não permanentemente obrigatório.
Exemplos:
Always On

ou:
Screen schedule

07:00 → ON
00:30 → OFF

ou:
PC online → ON
PC offline → OFF

ou:
Brightness
Day:   80%
Night: 15%

Isso ajuda bastante a preservar backlight e bateria.
Metas de desempenho
O projeto deve ser desenvolvido para o SM-T561M, e não testado somente em um celular moderno.
Eu colocaria como targets do MVP:
Métrica	Meta
RAM Client	<100–150 MB
UI	≥30 FPS em layouts normais
Input touch	resposta perceptualmente imediata
Atualização sensor	1–10 Hz
Startup	<10 s depois do Android disponível
Reconexão	automática
Operação offline	sim
Layout local	sim
Dependência Google	nenhuma


Não precisamos perseguir 60 FPS para um painel de sensores.
Consistência em 30 FPS é muito mais importante.
Segurança
Como vamos permitir chamadas e comandos na LAN, não podemos simplesmente deixar execução arbitrária aberta.
Eu faria inicialmente:
pairing
device token
allowlist de agentes

No primeiro pareamento:
Tablet: 593841

Studio:
Enter pairing code:
593841

Depois ambos guardam uma credencial.
Mesmo estando em LAN, isso evita qualquer dispositivo da rede mandar comandos para o painel.
O que NÃO deve entrar no MVP
Essa parte é essencial.
Não colocaria inicialmente:
- editor HTML completo;
- plugin marketplace;
- temas online;
- cloud sync;
- contas;
- login;
- servidor externo;
- múltiplos tablets simultâneos;
- Android abaixo de 7;
- MQTT;
- gráficos extremamente sofisticados;
- animações complexas;
- scripting arbitrário;
- Linux client;
- macOS;
- Linux Studio.
Tudo isso pode existir depois.
MVP real
O MVP precisa provar apenas esta cadeia:
Windows
   │
   │ Wi-Fi
   ▼
Galaxy Tab E
   │
   ▼
dashboard nativo

E permitir:
Studio cria layout
       ↓
envia layout
       ↓
tablet renderiza
       ↓
Agent manda GPU temp
       ↓
valor aparece
       ↓
touch em botão
       ↓
comando volta ao PC

Quando isso funcionar fluidamente no SM-T561M, o projeto está tecnicamente validado.
Roadmap
v0.1 — Proof of Concept
Android app, fullscreen, boot, JSON layout simples, Wi-Fi, Text/Value/Button, Agent básico e dados fictícios/reais simples.
v0.2 — Communication
WebSocket, descoberta LAN, reconexão automática, pairing e suporte USB através de ADB forwarding.
v0.3 — Dashboard Engine
Sistema formal de widgets, styles, pages, Data Sources REST e Actions.
v0.4 — Studio
Editor WPF, Canvas, drag-and-drop, property panel, preview, live editing e Deploy.
v0.5 — PC Monitoring
LibreHardwareMonitor, GPU/CPU/RAM/rede, tray Agent e startup automático.
v0.6 — Integrations
Home Assistant, RainbowMiner e integrações genéricas.
v1.0
Instaladores, atualização segura, documentação, configuração amigável e estabilidade suficiente para uso diário.
Estrutura do repositório
Eu usaria monorepo.
/
├── android-client/
│   ├── app/
│   └── README.md
│
├── windows-studio/
│
├── windows-agent/
│
├── protocol/
│   ├── schemas/
│   ├── examples/
│   └── PROTOCOL.md
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── ROADMAP.md
│   ├── ANDROID.md
│   └── WINDOWS.md
│
├── examples/
│
├── .github/
│   ├── workflows/
│   └── ISSUE_TEMPLATE/
│
├── README.md
└── LICENSE

Não criaria três repositórios ainda. Eles vão evoluir juntos demais no início.
GitHub Project
Um único Project.
Kanban:
Backlog
   ↓
Ready
   ↓
In Progress
   ↓
Testing
   ↓
Done

Eu colocaria um limite informal de:
máximo duas Issues em In Progress.

Isso evita justamente começar Android, Studio, Agent, MQTT e editor ao mesmo tempo.
Campos do Project
Pouquíssimos:
Status
Area
Priority
Milestone

Area:
Android
Studio
Agent
Protocol
Integration
Docs

Priority:
P0
P1
P2

Nada de story points neste momento.
Issues iniciais
Eu criaria aproximadamente estas:
1. [Protocol] Define v1 layout schema
   - formato JSON;
   - screen metadata;
   - widget base;
   - styles;
   - versionamento.
2. [Android] Create minimal Android project
   - Kotlin;
   - Views;
   - minSdk inicial compatível;
   - sem Compose.
3. [Android] Implement kiosk Activity
   - fullscreen;
   - immersive;
   - keep screen on;
   - landscape/portrait control.
4. [Android] Implement boot startup
   - BOOT_COMPLETED;
   - restore automático.
5. [Android] Implement layout parser
   - leitura JSON;
   - validation;
   - fallback em caso de layout inválido.
6. [Android] Implement Text and Value widgets
7. [Android] Implement Button widget and touch Actions
8. [Android] Implement local REST server
9. [Protocol] Implement WebSocket live protocol
10. [Agent] Create Windows Agent
    - C#;
    - connection;
    - heartbeat.
11. [Agent] Integrate LibreHardwareMonitor
12. [Studio] Create WPF Studio shell
13. [Studio] Implement device discovery
14. [Studio] Implement 800×1280 layout preview
15. [Studio] Implement layout deployment
Depois desse conjunto eu pararia de criar Issues antecipadamente.
O restante nasce conforme o PoC ensina o que realmente precisamos.
Definition of Done do primeiro milestone
O milestone v0.1 PoC só termina quando:
✓ APK instalado no SM-T561M
✓ inicia sozinho
✓ ocupa a tela inteira
✓ roda sem browser
✓ carrega layout JSON
✓ mostra pelo menos Text/Value/Button
✓ Windows conecta pela LAN
✓ PC envia um valor dinâmico
✓ tablet atualiza sem recarregar
✓ botão do tablet gera ação no PC
✓ reconecta após perda de Wi-Fi
✓ funcionamento continua estável por algumas horas

Isso é muito melhor do que considerar “Android app criado” como sucesso.
Escolha de produto / negócio
Eu posicionaria isso inicialmente como:
open-source local-first dashboard system for repurposing old Android tablets.

O diferencial não seria simplesmente mostrar temperatura de GPU. Há dezenas de aplicativos que fazem isso.
O diferencial seria:
transformar hardware Android obsoleto em uma tela touch genérica e programável.

Isso cobre:
PC monitoring
Home Assistant
servers
mining
network monitoring
media control
streaming controls
smart home
custom REST APIs

A aplicação não deve saber que existe RainbowMiner.
Ela deve ser genérica o suficiente para que RainbowMiner seja apenas uma fonte de dados.
Essa decisão aumenta muito o valor do projeto.
Open source
Eu tenderia a publicar open source.
Para licença, provavelmente:
MIT

seria suficiente pela simplicidade.
Mas eu deixaria essa decisão para antes da primeira release pública, principalmente se você considerar algum dia construir recursos comerciais em cima dele.
Possível evolução comercial
Eu não tentaria monetizar agora.
O projeto precisa primeiro provar que um tablet de dez anos consegue rodá-lo bem.
Se algum dia ganhar usuários, existem caminhos que não prejudicam o projeto open source, como:
templates premium
packs de dashboards
integrations avançadas
companion mobile app
remote/cloud relay opcional
instalador/device provisioning empresarial

Mas nada disso merece tempo no MVP.
O ponto mais importante
Eu não começaria construindo o Studio.
O caminho certo é:
Protocol
   ↓
Android Renderer
   ↓
Agent mínimo
   ↓
provar no SM-T561M
   ↓
Studio

Se começarmos pelo drag-and-drop bonito, podemos passar semanas construindo um editor para uma arquitetura que ainda nem sabemos se funciona bem no hardware.
O Tab E é o benchmark do produto.