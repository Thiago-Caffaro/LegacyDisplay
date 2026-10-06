# Android Client

Kotlin, Android Views e Canvas; `minSdk 24` (Android 7.0), `compileSdk/targetSdk 35`. Compile/target modernos não elevam a versão mínima do aparelho. O boot automático foi escrito para Android 7; dispositivos modernos exigirão o trabalho de provisioning/device owner antes de prometer o mesmo comportamento.

## Build

Instale JDK 17 ou 21 e o Android SDK com `platform-tools`, `platforms;android-35` e `build-tools;35.0.0`. Configure `JAVA_HOME` para o JDK e `ANDROID_HOME` para o SDK. Alternativamente, crie `android-client/local.properties` com `sdk.dir=C:/caminho/do/sdk`.

```powershell
cd android-client
.\gradlew.bat :app:assembleDebug :app:lintDebug :app:testDebugUnitTest --console=plain
```

APK: `android-client/app/build/outputs/apk/debug/app-debug.apk`. É um APK de desenvolvimento assinado com debug key, sem instalador/updater de release.

## Instalar e iniciar

No tablet, habilite depuração USB e autorize o PC. Se houver mais de um dispositivo, acrescente `-s SERIAL` aos comandos ADB.

```powershell
adb devices -l
adb install -r android-client/app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n org.legacydisplay.client/.MainActivity
```

Abra o app pelo menos uma vez após instalar; apps ainda não abertos e apps forçados a parar não recebem BOOT_COMPLETED normalmente. O receiver restaura o último layout após boot se `startOnBoot` estiver habilitado. A tela continua utilizável sem PC, com valores ausentes no primeiro início ou marcados como última leitura após uma desconexão.

## Configuração no aparelho

O rodapé exibe código e endereço enquanto não houver pareamento. Mantenha pressionada uma área sem botão para abrir configuração. Há duas opções: **Manter tela ligada** e **Iniciar ao ligar**. **Novo pareamento** revoga o token atual e desconecta o PC.

O layout ocupa coordenadas lógicas e determina retrato/paisagem. Reserve os últimos 48 pixels lógicos para o rodapé. Fontes e espaçamentos são definidos no JSON; nenhum recurso visual depende de uma página web.

`keepScreenOn=false` permite o timeout normal do Android; o PoC não agenda brilho, apaga a tela remotamente, bloqueia o usuário em lock task ou se torna launcher padrão. Esses recursos estão no roadmap.

## Verificação

Os testes JVM cobrem o parser compartilhado, restauração/fallback, falha de persistência, expiração/rate limit/revogação de pareamento, dados/ações e o servidor REST real. `writePocClasspath` prepara o servidor Kotlin para o teste de interoperabilidade com o Agent C#.

Esses testes não executam Activity/Canvas no Android. A validação de fullscreen, diálogo, boot, toque, desempenho e consumo permanece no [roteiro de hardware](VALIDATION.md).
