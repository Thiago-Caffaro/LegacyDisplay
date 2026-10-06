# Ações personalizadas dos botões

Studio e Agent v0.3 permitem que um toque no tablet faça uma chamada HTTP, abra um aplicativo ou execute uma sequência no PC. O APK v0.1 já suporta esse fluxo. URLs, argumentos e tokens ficam no Windows; o tablet guarda apenas o identificador da ação no layout.

## Começar

1. Atualize as pastas `studio` e `agent` com o pacote v0.3, mantendo-as juntas. Reinicie o Agent antigo: **Parar Agent** para o processo iniciado pelo Studio, ou Ctrl+C no terminal que iniciou o Agent. Abra o Studio novo e use **Iniciar Agent**.
2. Clique em **Ações** na barra superior ou em **Gerenciar ações** nas propriedades de um Button.
3. Escolha o tipo, clique em **Nova ação** e preencha os campos. Defina um ID como `luz.ligar` e um nome legível como `Ligar luz`.
4. **Aplicar ação** valida a edição. **Executar teste** realiza a ação imediatamente no PC, com os parâmetros da tela; não precisa de tablet nem salva o cadastro. **Salvar ações** grava o cadastro e fecha o editor.
5. Selecione um Button no painel, escolha o ID no campo **Ação**, clique em **Aplicar alterações** e em **Deploy no tablet**. Salve também o layout se quiser manter uma cópia no PC.
6. Toque no botão com o Agent conectado. O rodapé recebe a confirmação; um widget Value com fonte `action.lastResult` também pode mostrá-la.

O ID aceita de 1 a 64 caracteres ASCII: letras, números, ponto, hífen e sublinhado; deve começar com letra ou número. `demo.ping` é reservado e sempre confirma o toque. Desmarcar **Habilitada** impede a execução da ação no próximo toque. O Studio só faz Deploy quando os IDs dos botões existem e estão habilitados neste PC.

## Chamada HTTP

| Campo | Como preencher |
|---|---|
| URL | Endereço completo HTTP/HTTPS da API, visto pelo PC |
| Método | GET, POST, PUT, PATCH, DELETE ou HEAD |
| Timeout | 100–7500 ms, padrão 3000 ms |
| Content-Type | Tipo do corpo, como `application/json` |
| Corpo | Conteúdo literal enviado à API; deve ficar vazio para GET/HEAD |
| Token Bearer | Opcional; enviado em `Authorization: Bearer TOKEN` |
| Outros cabeçalhos | Objeto JSON de nomes e valores; use `{}` se não precisar |

Exemplo para uma API sua que aceite ativação:

```text
ID: dispositivo.ligar
Nome: Ligar dispositivo
URL: http://192.168.100.50:8080/api/device/enable
Método: POST
Content-Type: application/json
Corpo: {"enabled":true}
Timeout: 3000
```

O endereço, a rota e o corpo precisam corresponder à API que você usa. O exemplo não pressupõe que exista um serviço nesse IP. `127.0.0.1` refere-se ao Windows onde o Agent está rodando.

Exemplo de cabeçalhos adicionais:

```json
{"Accept":"application/json","X-Api-Key":"SEU_TOKEN"}
```

Use o campo Bearer ou um cabeçalho `Authorization`, sem duplicar. `Content-Type` tem campo próprio; Host, Content-Length, Connection e Transfer-Encoding são controlados pelo cliente HTTP.

Uma resposta 2xx confirma sucesso, por exemplo `Ligar dispositivo · HTTP 204`. Outros códigos falham a ação. O corpo da resposta não é lido nem mostrado no tablet. Redirecionamentos, cookies automáticos e retries estão desabilitados. O corpo é literal: não há substituição de sensores, interpretação de código ou extração JSON nesta entrega.

## Abrir aplicativo

Use **Selecionar…** para escolher um `.exe`. Informe um argumento por linha. Caminhos com espaços ocupam uma linha inteira, sem aspas adicionais:

```text
C:\Painéis do PC\perfil.json
--modo=painel
```

Cada linha é enviada como um argumento separado. A pasta de trabalho é opcional; se vazia, é a pasta do executável. O arquivo e a pasta precisam existir no PC que executa o Agent. O aplicativo roda com o usuário/permissões do Agent.

O resultado `aplicativo iniciado` confirma que o processo foi criado. Não aguarda o encerramento, lê stdout ou comprova o resultado interno do programa. Não há interpretação automática de comandos shell; use os parâmetros documentados do executável selecionado.

## Sequência

Cadastre primeiro as ações que serão usadas. Crie uma **Sequência de ações**, selecione um ID por etapa e clique em **Adicionar etapa**. **Atualizar etapa** grava a edição da etapa selecionada; **Subir/Descer** muda sua ordem.

A pausa é aplicada **antes** da etapa, em milissegundos. Por exemplo: chamar `dispositivo.ligar`, pausar 500 ms e executar `app.painel`. A sequência para na primeira falha. Uma etapa desabilitada também falha. Ações anteriores bem-sucedidas não são desfeitas.

Há de 1 a 16 etapas por sequência, pausas de até 4000 ms por etapa e até 7000 ms na sequência expandida. Sequências podem chamar outras sequências, até oito níveis, sem ciclos, com no máximo 32 ações finais. Todo o toque compartilha um limite cooperativo de oito segundos, incluindo HTTP e pausas; reserve tempo suficiente para cada chamada.

## Função própria em C#

**Função interna** referencia uma implementação compilada no Agent. A função disponível no pacote é `demo.ping`. Digitar outro ID não cria código automaticamente; para acrescentar uma função, registre-a no código e publique o Agent novamente.

Em `windows-agent/LegacyDisplay.Agent/AgentSession.cs`, a construção do executor pode receber um registro:

```csharp
using var actions = new ActionExecutor(
    () => ActionStore.Load(actionsPath ?? ActionStore.DefaultPath),
    customFunctions: new Dictionary<string, Func<CancellationToken, Task<ActionOutcome>>> {
        ["custom.confirmar"] = token => {
            token.ThrowIfCancellationRequested();
            // Chame sua implementação aqui, respeitando o CancellationToken.
            return Task.FromResult(new ActionOutcome(true, "Função concluída"));
        }
    });
```

Depois, cadastre uma ação do tipo **Função interna** com ID `minha.confirmacao`, nome à escolha e função `custom.confirmar`. Vincule `minha.confirmacao` ao Button e faça Deploy.

O teste local do Studio usa seu próprio registro de funções. Para testar uma função nova por **Executar teste**, registre-a também na construção do `ActionExecutor` em `ActionsWindow.xaml.cs`, preferindo uma implementação compartilhada na biblioteca Windows. Sem isso, teste pelo tablet com o Agent atualizado. Funções devem cooperar com o cancelamento e não bloquear a recepção de mensagens; o runtime não encerra à força código arbitrário nem processos já abertos.

## Armazenamento e execução

- O catálogo inteiro fica protegido por Windows DPAPI CurrentUser em `%LOCALAPPDATA%\LegacyDisplay\actions.json`, com gravação atômica. Outro usuário Windows não pode reutilizá-lo diretamente. O nome `.json` descreve o envelope cifrado; não edite seu conteúdo manualmente.
- Tokens e parâmetros ficam separados do layout e do token de pareamento. Ao transferir um layout para outro PC, cadastre suas ações nesse PC.
- Salvar parâmetros de um ID já usado vale no próximo toque; a execução em andamento mantém sua configuração inicial. Renomear o ID atualiza referências nas sequências e nos botões do documento aberto, mas exige novo Deploy. Layouts em outros arquivos preservam seus IDs antigos.
- Há uma execução por vez. Outro toque durante a ação recebe uma mensagem para aguardar; não fica em fila. Solicitações com UUID repetido são rejeitadas dentro do histórico de 128 IDs da sessão. Perder a conexão não repete uma ação já enviada.
- Limites adicionais: 128 ações, catálogo de 64 KiB, corpo HTTP de 16 KiB, 16 cabeçalhos e 32 argumentos por aplicativo. Configurações inválidas preservam o cadastro anterior ao salvar; arquivo corrompido impede ações personalizadas até corrigir o cadastro.

O transporte do tablet continua no protocolo v1. Sensores seguem sendo enviados enquanto uma chamada HTTP aguarda resposta. A verificação automatizada cobre o editor, a persistência, as regras de execução e uma API HTTP local; o toque das novas ações no aparelho físico ainda precisa ser conferido.
