# Usar o LegacyDisplay Studio

O Studio é a janela de customização no Windows. O Agent continua sendo o runtime que envia os sensores ao tablet.

## Abrir

No checkout, execute `artifacts\studio\LegacyDisplay.Studio.exe`. Se recebeu o ZIP, extraia o pacote inteiro e abra `studio\LegacyDisplay.Studio.exe`. Preserve as duas pastas irmãs `studio` e `agent` e todas as DLLs da publicação. Não é necessário instalar .NET.

O Studio reutiliza o pareamento do Agent no mesmo usuário Windows. Ao abrir, tenta conectar ao endereço salvo. Se o Agent anterior ainda estiver rodando no terminal, o Studio reconhece a conexão; para usar a versão nova dos sensores, encerre aquele processo com Ctrl+C e clique em **Iniciar Agent**.

## Conectar

1. Informe o endereço mostrado pelo tablet, com `http://` e a porta `8765`, e clique em **Conectar**.
2. Para o primeiro pareamento, informe o código de seis dígitos mostrado no aparelho e clique em **Parear**. Um tablet já pareado não exige novo código. Se a credencial foi revogada, use **Novo pareamento** no menu local do Android.
3. Clique em **Ler layout do tablet** para começar pelo painel instalado ou abra um JSON existente. O botão **Novo** cria uma tela vazia de 800 × 1280.

O Studio exibe nome, resolução e estado do Agent. **Manter tela ligada** e **Iniciar ao ligar o tablet** são opções persistidas no Android ao clicar em **Aplicar opções do tablet**. Após perda temporária de conexão, o Studio tenta recuperar o endereço conhecido a cada cinco segundos. Alterar a URL exige clicar em Conectar para validar o novo endereço.

## Editar

- **Text**, **Value** e **Button** adicionam widgets. Um duplo clique em uma fonte da lista cria um Value ligado a ela.
- Selecione o widget pelo preview ou pela lista. Arraste para mover; a alça inferior direita redimensiona. O zoom só altera a visualização, mantendo coordenadas lógicas do layout.
- **Snap 8 px** encaixa os gestos em uma grade. O painel de propriedades aceita coordenadas exatas.
- Edite texto, fonte de dados, formato, geometria e estilo à direita e clique em **Aplicar alterações**. Cores usam `#RRGGBB`; formatos de Value usam `{value}`.
- **Duplicar**, **Excluir**, **Para trás** e **Para frente** controlam os widgets e a sobreposição. Cada gesto de arraste ocupa uma única etapa no histórico.
- **Desfazer/Refazer** e Ctrl+Z/Ctrl+Y operam o histórico do layout; nos campos de texto, os atalhos mantêm a edição normal do campo. Ctrl+S salva, Ctrl+O abre e Ctrl+N cria outro documento.

Reserve os últimos 48 pixels para o rodapé nativo. A escala, geometria e formato são compartilhados; fontes e rasterização WPF/Android podem diferir. A confirmação visual final ocorre no tablet.

## Sensores reais

O preview usa leituras do próprio PC e funciona mesmo com o tablet desconectado. CPU/RAM/rede/uptime vêm das APIs Windows. GPUs disponíveis são lidas pelo LibreHardwareMonitor 0.9.6; a lista inclui nome, temperatura, uso, potência, VRAM e outros sensores que o driver expõe, como hotspot, clock e ventoinhas.

As fontes `pc.gpu.*` referem-se à primeira GPU ordenada pelo identificador da biblioteca. As fontes `hw.*` identificam sensores de uma placa específica. Use estas últimas se houver várias GPUs. Sensor ausente aparece como `—`; cada driver disponibiliza um conjunto diferente. A coleta GPU ocorre no máximo uma vez por segundo, mesmo com o Agent transmitindo a 10 Hz. Até 200 fontes específicas são mantidas por sessão.

CPU térmica, placa-mãe e GPU Intel que exige coleta de CPU ficam para a próxima etapa. A configuração atual habilita apenas a categoria GPU da biblioteca. `demo.gpu.temperature` continua explicitamente simulada e só existe no Agent com `--demo`; o Agent iniciado pelo Studio usa sensores reais.

O pacote inclui `studio\layouts\dashboard-gpu.json`, com CPU, RAM, temperatura, potência e VRAM da GPU. Abra esse arquivo pelo Studio para usar como base.

## Ações dos botões

1. Clique em **Ações**, escolha o tipo e clique em **Nova**.
2. Preencha ID, nome e parâmetros. Há chamadas HTTP, abertura de aplicativos, sequências e funções internas registradas no Agent.
3. **Aplicar ação** valida a edição. **Executar teste** executa a configuração de verdade no PC; use quando quiser conferir seu resultado. **Salvar ações** grava o cadastro protegido e fecha a janela.
4. Selecione um Button, escolha o ID no campo **Ação**, clique em **Aplicar alterações** e faça **Deploy no tablet**.
5. Com o Agent v0.3 rodando, toque no botão do tablet. O resultado aparece no rodapé e na fonte `action.lastResult`.

O cadastro fica em `%LOCALAPPDATA%\LegacyDisplay\actions.json`, protegido por DPAPI para o usuário Windows. URLs, argumentos e tokens permanecem no PC; o layout contém apenas o ID da ação. Editar os parâmetros de um ID já usado passa a valer no próximo toque após salvar, sem outro Deploy. Renomear o ID atualiza os botões do documento aberto e exige Deploy. Salvar ações não salva o layout.

O APK já instalado é compatível. Atualize Studio **e** Agent para v0.3 e reinicie qualquer Agent antigo ainda rodando. Fechar o Studio mantém o runtime ativo. Confira os campos, exemplos e limites no [guia de ações](ACTIONS.md).

Para controlar AUTO/PAUSADO/FORÇAR MINERAÇÃO e sair do noturno do projeto MiningRoutine, use os [cadastros e painel de exemplo da integração](MININGROUTINE.md).

`mining.quiet` solicita mineração com iluminação e monitores apagados. O tablet acompanha a confirmação pelo Agent em modo automático ou manual. Essa função exige MiningRoutine atualizado, Agent novo e APK v0.2; consulte o guia da integração para instalar e despertar o painel temporariamente.

## Salvar, enviar e manter rodando

**Salvar** grava um JSON no Windows. **Deploy no tablet** envia uma fotografia do layout validado e a persiste no Android. Você pode continuar editando enquanto o Agent transmite dados; alterações locais posteriores exigem outro Deploy. Mudanças inválidas preservam o último layout válido.

**Iniciar Agent** lança o runtime em segundo plano. Fechar o Studio mantém esse Agent funcionando. Ao reabrir o Studio no mesmo usuário Windows, **Parar Agent** consegue encerrá-lo. Um Agent iniciado por outro terminal deve ser encerrado naquele terminal.

O Agent não é instalado como serviço e ainda não inicia automaticamente ao entrar no Windows. O estado do processo iniciado pelo Studio fica em `%LOCALAPPDATA%\LegacyDisplay\studio-agent.json`; a credencial compartilhada e protegida por DPAPI fica em `agent-device.json` na mesma pasta.

## USB

Instale Android Platform Tools, conecte o cabo, ative depuração USB e autorize o PC na tela do Android. **Detectar USB** encontra um único dispositivo autorizado e cria/reutiliza um encaminhamento ADB para a porta 8765 do app, escolhendo uma porta livre no PC. A API e a credencial são as mesmas da LAN.

O Studio procura ADB nos SDKs configurados, no PATH e no SDK local de desenvolvimento. ADB não está redistribuído no pacote Windows. Se houver vários dispositivos conectados, desconecte os extras nesta versão. O encaminhamento deixa de funcionar ao retirar o cabo; selecione o endereço LAN e reconecte manualmente. Descoberta mDNS e troca automática USB/Wi-Fi ainda não estão implementadas.
