# Controlar o MiningRoutine pelo tablet

As ações `mining.auto`, `mining.paused`, `mining.force_mine`, `mining.exit_night` e `mining.quiet` usam o executável publicado do MiningRoutine, com seu `data_directory` configurado. Não precisam da sessão HTTP do painel, cujo token muda a cada inicialização.

Para cadastrar pela interface, abra **Ações → Importar MiningRoutine…**, selecione `C:\Projects\MiningRoutine` e clique em **Salvar ações**. Nas propriedades do Button escolha `mining.quiet` e faça **Aplicar alterações → Deploy**. Não crie uma função interna com esse nome.

Para cadastrar ou repetir a configuração pelo terminal:

```powershell
.\artifacts\agent\LegacyDisplay.Agent.exe configure-mining --project 'C:\Projects\MiningRoutine'
```

O comando acrescenta cinco ações ao cadastro protegido do LegacyDisplay, preservando outras ações. Se já existir o mesmo ID com parâmetros diferentes, recusa a importação. Uma cópia protegida do cadastro anterior é preservada antes de salvar. Cadastrar não muda o modo da mineração, abre aplicativos ou altera iluminação.

Reabra o Studio para carregar o cadastro. Selecione um Button, escolha um destes IDs no campo **Ação**, clique em **Aplicar alterações** e faça **Deploy no tablet**. Você pode adicionar os botões ao seu layout atual ou abrir o exemplo `studio/layouts/dashboard-mining.json`. O exemplo usa orientação horizontal; enviar esse arquivo substitui o layout do tablet, portanto salve seu painel atual se quiser usá-lo depois.

| Ação | Comando do MiningRoutine | Resultado esperado |
|---|---|---|
| `mining.auto` | `--mode auto` | Política automática, respeitando thresholds e regras |
| `mining.paused` | `--mode paused` | Pausa manual até trocar o modo |
| `mining.force_mine` | `--mode force_mine` | Override de mineração, preservando proteções e pausas nativas |
| `mining.exit_night` | `--exit-night` | Saída do noturno, restauração da iluminação e inibição de reentrada conforme o controlador |
| `mining.quiet` | `--quiet-mining` | Solicita FORCE_MINE com proteções; após confirmar mineração e iluminação apagada, solicita espera dos monitores Windows |

O controlador deve estar ativo para consumir os comandos. O LegacyDisplay confirma a criação do processo de comando com `aplicativo iniciado`; isso não comprova que o modo já foi aplicado ou que os miners já retomaram. Confira `mode`, `control.state` e `mining` no painel do MiningRoutine ou em seu `status.json`. A fila local do MiningRoutine contém um comando por vez; aguarde a confirmação antes de trocar novamente.

Estados como `day_mining`, `night_deep` e `paused_gpu_busy` são resultados da política e das condições observadas, não modos definidos diretamente pelo botão. O Agent agora lê o `status.json` do runtime identificado pelo cadastro, verificando timestamp recente e processo ativo. As fontes `mining.mode`, `mining.state`, `mining.status.available`, `mining.lighting.off`, `mining.display.off` e `mining.blackout` podem ser usadas em Values; informe o ID manualmente no campo de fonte se ainda não estiver na lista. O Studio não coleta esses valores no preview nesta entrega.

Se mover o projeto ou alterar `data_directory`, atualize os parâmetros em **Ações** ou remova os cadastros antes de importar novamente. **Executar teste** nessa integração envia um comando real ao MiningRoutine.

## Luzes, monitores e tablet juntos

O comando manual não ignora regras de processos, falhas ou pausas nativas. Aguarda mineração observada e a confirmação dos providers de iluminação antes de enviar `SC_MONITORPOWER` ao Windows. Não altera o plano de energia nem solicita suspensão do PC. Falha/interrupção restaura a iluminação pelo journal existente. Mouse/teclado ou despertar dos monitores cancelam a sessão visual manual; o modo FORCE_MINE permanece. Trocar para AUTO/PAUSADO/FORCE ou sair do noturno cancela a sessão e solicita despertar dos monitores que ela apagou. Reiniciar o controlador conserva o modo de mineração, mas não reaplica automaticamente a sessão visual manual.

Tanto no automático quanto no manual, `mining.blackout` só é verdadeiro com iluminação **confirmada apagada**, `display_state=off`, runtime real e snapshot recente. O APK v0.2 usa esse sinal para cobrir o painel de preto, reduzir brilho e suspender temporariamente **Manter tela ligada**. A tela física é desligada pelo timeout configurado no Android, sem permissão de administrador do dispositivo. Preto/brilho mínimo não equivalem ao desligamento imediato do backlight.

O primeiro toque enquanto o app ainda está acordado só revela o painel, sem disparar um botão. Depois de 15 segundos sem toque, ele volta a escurecer se o PC ainda estiver apagado. Se o Android já desligou a tela, use o botão físico de energia para despertar: o app concede os mesmos 15 segundos. O tablet não altera o estado do PC só por despertar. Configurações locais abertas mantêm o painel visível. Falta de conexão, sinal antigo por mais de 15 segundos ou confirmação falsa liberam o modo escuro; isso não força a tela física a ligar.

## Aplicar esta atualização

1. No MiningRoutine, encerre normalmente o controlador com `scripts/Stop-Controller.ps1` e aguarde sua saída. Copie o conteúdo de `publish-quiet` para `publish`, preservando `controller.json` e `.tools/runtime`. Inicie novamente por `scripts/Start-Controller.ps1` ou pelo launcher habitual. Se o processo elevado impedir a atualização, execute esses passos no mesmo contexto administrativo usado para iniciá-lo.
2. Pare o Agent anterior, feche o Studio e extraia o pacote Windows novo completo. Reinicie o Agent e reabra o Studio. Os cadastros/credenciais do usuário são preservados.
3. Instale `LegacyDisplay-v0.2-debug.apk` sobre o APK anterior, sem desinstalar, para preservar pareamento e layout. Esta função precisa do APK novo; ações anteriores isoladas continuam compatíveis com v0.1.
4. Vincule `mining.quiet` a um Button e faça Deploy. Confira no aparelho o blackout, timeout e retorno temporário; essa validação física ainda é necessária.

Verificação local disponível: `python scripts/test-mining-routine.py --project C:\Projects\MiningRoutine`. O script exerce os cinco comandos com uma pasta de dados temporária e não altera o runtime ativo. Use `--executable C:\Projects\MiningRoutine\publish-quiet\MiningController.Windows.exe` para testar a publicação preparada antes de instalá-la.
