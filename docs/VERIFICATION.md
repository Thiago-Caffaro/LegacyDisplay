# Verificação

O projeto inclui testes C# de protocolo, credenciais, ações, editor e integrações opcionais; testes JVM do cliente Android; lint; interoperabilidade C# ↔ Kotlin e testes da interface WPF.

Execute `scripts/build.ps1 -Interop` para verificar e gerar os artefatos. O workflow GitHub Actions executa esse mesmo fluxo em Windows.

Testes automatizados não substituem a validação em aparelho: pareamento, reconexão Wi-Fi, orientação, boot, consumo de memória e timeout físico da tela precisam ser conferidos no tablet. O APK gerado é debug; assinatura de uma release não está configurada.
