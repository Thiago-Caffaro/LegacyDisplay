# LegacyDisplay Protocol v1

Todos os endpoints ficam sob `/api/v1`, usam JSON UTF-8 e são servidos pelo tablet na porta TCP 8765. O transporte principal é HTTP + WebSocket em LAN. O Agent inicia a conexão com o tablet; o tablet permanece disponível mesmo quando o PC está offline.

## Layout

O contrato executável está em `schemas/layout-v1.schema.json`. `examples/dashboard.json` é o dashboard carregado no primeiro boot. `examples/layout-conformance.json` é a coleção compartilhada de casos aceitos/rejeitados, consumida pelos testes Kotlin e C#.

```json
{
  "version": 1,
  "screen": { "width": 800, "height": 1280, "background": "#101820" },
  "widgets": [
    {
      "id": "cpu",
      "type": "value",
      "x": 40, "y": 80, "width": 720, "height": 160,
      "source": "pc.cpu.usage", "format": "{value} %",
      "style": { "fontSize": 80, "alignment": "center" }
    }
  ]
}
```

Coordenadas e tamanhos são pixels lógicos no espaço `screen`. O cliente preserva a proporção e centraliza esse espaço na tela física. Retrato/paisagem acompanha as dimensões declaradas. Um rodapé de 48 pixels lógicos é reservado para estado/pareamento; não coloque controles nesse espaço.

| Tipo | Campos específicos obrigatórios | Comportamento |
|---|---|---|
| `text` | `text` | Texto estático |
| `value` | `source` | Valor identificado por nome, sem conhecimento do sensor |
| `button` | `text`, `action` | Envia uma ação identificada por nome |

`format` substitui cada `{value}`. O padrão é `{value}`. Valor ausente/null aparece como `—`, booleanos como `ON`/`OFF`, números com até uma casa decimal. Texto usa quebra de linha e recorte dentro do widget. A ordem do array é a ordem de desenho; o último widget sobreposto recebe o hit test. Uma View Canvas única evita árvores grandes de Views.

O estilo admite `background`, `color` (`#RRGGBB`), `fontSize`, `fontWeight` (`normal`/`bold`), `alignment` (`left`/`center`/`right`), `borderRadius`, `padding` e `opacity`. Valores padrão estão no schema/modelos. Não há HTML ou scripting.

Limites: layout de 64 KiB UTF-8, até 128 widgets, telas de 1–4096 por dimensão, ids únicos de 1–64 caracteres ASCII (`[A-Za-z0-9][A-Za-z0-9_.-]*`), widgets completamente dentro da tela, texto até 256 unidades UTF-16 e formato até 128. Os campos inteiros exigem literais inteiros, sem notação decimal/exponencial. O schema descreve estrutura; unicidade de ids, geometria, comprimento UTF-16 e representação lexical de inteiros são verificados pelos parsers. Campos desconhecidos são rejeitados. Alterações incompatíveis exigem outra versão.

## REST

| Método/rota | Autenticação | Resultado |
|---|---|---|
| `GET /status` | Pública | Versão, nome/modelo, dimensões do layout, pareamento e conexão |
| `POST /pair` | Código local | Recebe `{ "code": "123456" }`; retorna `{ "token": "64 caracteres hex" }` |
| `GET /layout` | Bearer | Layout persistido atual |
| `POST /layout` | Bearer | Valida e persiste antes de alterar o renderer; retorna `{ "saved": true }` |
| `GET /config` | Bearer | Configuração atual |
| `PUT /config` | Bearer | Valida/persiste `keepScreenOn` e `startOnBoot` |
| `POST /action` | Bearer | `{ "widgetId": "ping" }`; dispara a ação desse botão, retorna `{ "sent": true }` |
| `WS /live` | Bearer no upgrade | Uma sessão do Agent |

Rotas acima são relativas a `/api/v1`. Requests com corpo exigem `Content-Type: application/json`, `Content-Length` de 1–65536 bytes e não aceitam transferência chunked. GET não tem corpo. Erros retornam `{ "error": "..." }`: 400 para payload inválido, 401 para token ausente/incorreto, 409 para pareamento/sessão/ação indisponível, 404 para rota/método desconhecido e 500 para falha de persistência/conexão.

Autenticação: `Authorization: Bearer TOKEN`. O código só aparece localmente no tablet, expira em cinco minutos e aceita no máximo cinco tentativas por minuto. Um novo pareamento remoto é recusado enquanto houver token. **Novo pareamento**, na configuração local, revoga o token e desconecta o Agent. Não há código ou token em `/status`; o token não entra no layout.

Configuração:

```json
{ "keepScreenOn": true, "startOnBoot": true }
```

## WebSocket

O tablet envia `hello` ao abrir a sessão e um `heartbeat` a cada cinco segundos:

```json
{ "type": "hello", "protocolVersion": 1, "layout": { "version": 1, "screen": {}, "widgets": [] } }
{ "type": "heartbeat" }
```

O campo `layout` do hello contém o layout completo válido (a abreviação acima não é um layout). Ele elimina a corrida entre ler o layout por REST e abrir a sessão. Deploys posteriores são enviados como `layout.changed`.

O Agent envia valores de fontes, usualmente a 1 Hz (CLI configurável de 100–10000 ms):

```json
{ "type": "data.update", "values": { "pc.cpu.usage": 27.5, "pc.memory.usage": 42 } }
```

`values` faz merge por chave. São permitidos null, booleanos, números finitos e strings até 256 caracteres. Há no máximo 256 fontes armazenadas; objetos/arrays são recusados. O renderer agrupa solicitações de invalidação com intervalo mínimo de 33 ms, sem um loop permanente de animação.

Um toque ou `POST /action` envia:

```json
{ "type": "action.invoke", "requestId": "UUID", "widgetId": "ping", "action": "demo.ping" }
```

O Agent confere o botão no layout atual e a ação habilitada no cadastro local (`demo.ping` também está sempre disponível). O Studio/Agent v0.3 permite HTTP, aplicativos, sequências e funções registradas no PC sem mudar este contrato. Retorna:

```json
{ "type": "action.result", "requestId": "MESMO UUID", "success": true, "message": "PC recebeu o toque!" }
```

Resultados só são aceitos para uma solicitação pendente de até dez segundos. Existem no máximo 32 ações pendentes. O texto também atualiza a fonte local `action.lastResult`. URLs, tokens, argumentos e etapas permanecem no catálogo protegido do Windows; o tablet não fornece parâmetros executáveis. O Agent executa uma ação por vez, com limite cooperativo de oito segundos, e rejeita toques adicionais durante uma execução. Não há retry automático de ações. Os últimos 128 UUIDs de cada sessão são retidos para rejeitar solicitações repetidas.

Deploy durante a sessão produz `{ "type": "layout.changed", "layout": { ... } }`, para o Agent atualizar a associação entre botões e ações. Deploy persiste; não há edição transitória no PoC.

Uma mensagem inválida resulta em `error` e encerramento da sessão. Mensagens do Agent para o tablet são texto JSON até 64 KiB. O Agent aceita até 128 KiB do tablet para acomodar o envelope de `hello`/`layout.changed` ao redor de um layout de até 64 KiB. NanoWSD 2.3.1 faz a alocação do frame antes do callback de validação; este limite não oferece proteção completa contra frames gigantes enviados por um peer autenticado. Endurecer o parser/limites por conexão e introduzir TLS é trabalho prévio a uma exposição em redes não confiáveis.

## Falhas e reconexão

O Agent usa timeout de conexão de dez segundos e de recebimento de vinte segundos, com retentativas de 1, 2, 4, 8, 16 e 30 segundos. Uma sessão estável por trinta segundos reinicia o backoff. Um 401 interrompe o runtime com mensagem para parear novamente. O servidor usa timeout de leitura de trinta segundos e rejeita uma segunda sessão do Agent.

O tablet mantém o último layout e os últimos valores durante a perda da conexão. O rodapé identifica os valores como última leitura; após reiniciar o app/processo, valores começam ausentes até chegar uma nova amostra. O boot não depende do PC. Credenciais Windows usam DPAPI CurrentUser; o tablet usa armazenamento privado, sem backup Android habilitado.

Wi-Fi e ADB forwarding compartilham o contrato. O canal não usa TLS neste PoC e requer LAN confiável ou USB. Pairing não autentica a identidade do tablet contra um atacante que intercepte a primeira troca.

## Estado visual opcional

`display.blackout` é uma fonte booleana genérica: `true` solicita escurecimento e libera o timeout da tela; `false` restaura o painel. Não representa controle físico garantido do backlight. O cliente exige conexão e sinal recente e permite despertar temporário por toque. Integrações devem emitir essa fonte periodicamente e limpar o sinal quando o estado externo não puder ser confirmado.
