# Validação do PoC no SM-T561M

Data do desenvolvimento inicial: 2026-10-02. Dispositivo de referência: Galaxy Tab E SM-T561M, Android 7/LineageOS, 800 × 1280 e aproximadamente 1,5 GB de RAM.

## Verificação automatizada

Execute `scripts/build.ps1 -Interop`. Ele cobre build .NET/Android, testes C#/JVM, lint, interface WPF e sessões de rede entre o servidor Kotlin real, Studio e Agent C#. O fixture JVM não simula Activity, Canvas, toque físico ou Android boot.

Registre em `docs/VERIFICATION.md` o resultado efetivamente obtido. Não marque o milestone concluído antes da etapa física.

## Checklist no tablet

- [x] APK instalado e aberto no Tab E; LineageOS identifica `SM-T561`, Android 7.1.2.
- [x] Tela imersiva, sem barras na captura observada.
- [ ] Último layout restaura ao fechar/reabrir e após reiniciar.
- [ ] BOOT_COMPLETED inicia o app após boot, com opção habilitada e aparelho desbloqueado quando necessário.
- [ ] Configuração de tela ligada permite/desabilita timeout conforme escolhido.
- [x] Dashboard mostra Text, Value e Button sem browser.
- [x] Pareamento pela LAN funciona; rejeição de código/token já coberta pelo teste de interoperabilidade.
- [x] PC conecta pela LAN e transmite CPU/RAM reais sem recarregar a UI.
- [ ] Toque no botão confirma no PC e mostra resposta no tablet.
- [ ] Wi-Fi desligado identifica valores antigos; Wi-Fi restaurado reconecta automaticamente.
- [ ] Encerrar/reiniciar Agent preserva painel e reconecta.
- [ ] Novo pareamento revoga a credencial anterior e permite parear de novo.
- [ ] Layout inválido mantém o layout atual.
- [ ] Paisagem/retrato e hit test correspondem após deploy de layouts das duas orientações.
- [ ] Estável por algumas horas, com alterações de valores e toques periódicos.

## Próxima verificação com Studio/GPU

O APK já instalado usa o mesmo protocolo v1. Para esta etapa, abra o Studio e mantenha o tablet disponível; não é necessária outra instalação Android.

- [ ] **Ler layout do tablet**, mover/redimensionar um widget, salvar e fazer Deploy; conferir geometria/fontes/toque na tela física.
- [ ] Abrir `studio/layouts/dashboard-gpu.json` e enviar; conferir sensores reais e o botão de confirmação.
- [ ] Iniciar Agent pela janela, fechar Studio e confirmar que o tablet segue recebendo valores; reabrir e parar o Agent pelo Studio.
- [ ] Conectar cabo autorizado e usar **Detectar USB**; conferir amostras pelo endereço de loopback.
- [ ] Aplicar opções de tela ligada/início no boot e conferir o comportamento no Android.
- [ ] Com Studio/Agent v0.3, cadastrar uma ação HTTP de um serviço de teste, vinculá-la ao Button e fazer Deploy; conferir execução única e confirmação no tablet.
- [ ] Conferir abertura de um aplicativo configurado e sequência pelo toque físico; desabilitar uma ação e confirmar a recusa no próximo toque.

Esses resultados físicos complementam os testes de interface/protocolo já aprovados. Na próxima alteração do engine Android (novos widgets), repetir a inspeção visual, toque e medições de frames/RAM no Tab E.

## Metas e medição

| Métrica | Meta | Resultado físico |
|---|---|---|
| RAM cliente | abaixo de 100–150 MB | PSS de 14,5 MiB em amostra curta com dados a 10 Hz |
| UI em layout normal | pelo menos 30 FPS quando houver desenho | amostra posterior de 1013 frames: p95 15 ms, p99 19 ms, 3,16% janky; FPS contínuo não medido |
| Toque | resposta perceptualmente imediata | não medido |
| Sensor | 1–10 Hz | dados reais enviados a 10 Hz e visíveis no tablet |
| Startup | abaixo de 10 s após Android disponível | não medido |
| Reconexão | automática | verificar na LAN real |
| Offline/layout local | sim | verificar persistência física |

O painel é dirigido por invalidações, então um dashboard parado não precisa desenhar trinta vezes por segundo. Meça custo de frame quando atualiza e resposta ao input, em vez de tratar ausência de frames ociosos como uma falha.

Com ADB:

```powershell
adb shell dumpsys meminfo org.legacydisplay.client
adb shell dumpsys gfxinfo org.legacydisplay.client reset
# Exercite o painel com dados a 10 Hz e toques; depois:
adb shell dumpsys gfxinfo org.legacydisplay.client framestats
adb logcat -d -s AndroidRuntime
```

Registre PSS total, frames lentos, duração aproximada do boot, ROM, conexão, quantidade de widgets, frequência dos sensores e duração do ensaio. Boot automático requer que o app já tenha sido aberto uma vez e não esteja em estado de force-stop. USB exige autorização de depuração; Wi-Fi exige acesso entre os dispositivos na LAN.
