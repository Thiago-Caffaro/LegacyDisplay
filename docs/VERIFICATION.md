# Verificação

O projeto inclui testes C# de protocolo, credenciais, ações, editor e integrações opcionais; testes JVM do cliente Android; lint; interoperabilidade C# ↔ Kotlin e testes da interface WPF.

Execute `scripts/build.ps1 -Interop` para verificar e gerar os artefatos. O workflow GitHub Actions executa esse mesmo fluxo em Windows.

Testes automatizados não substituem a validação em aparelho: pareamento, reconexão Wi-Fi, orientação, boot, consumo de memória e timeout físico da tela precisam ser conferidos no tablet. O APK gerado é debug; assinatura de uma release não está configurada.

## Verificação da edição pública — 2026-10-06

- 32 testes C# aprovados, incluindo funcionamento sem integração configurada.
- 10 testes JVM aprovados; APK debug compilado e lint aprovado.
- Interoperabilidade C# ↔ Kotlin, editor de ações WPF e smoke tests do Studio aprovados.
- Pacote Windows inclui licença MIT e avisos das dependências.
- Configurações de usuário, credenciais, runtimes, SDKs e binários não entram no histórico Git.

O CI verifica o mesmo fluxo a partir do código publicado. A validação física em tablet não foi repetida para esta separação.
