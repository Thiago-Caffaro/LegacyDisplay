# Contribuir

Abra uma issue com a versão do Android/Windows, passos para reproduzir e logs sem tokens ou dados pessoais. Para alterações, faça uma branch e abra um pull request explicando comportamento e validação.

Execute `scripts/build.ps1 -Interop` com .NET SDK 8, Python 3, JDK 21 e Android SDK. As alterações no protocolo precisam continuar compatíveis nas implementações Kotlin e C#. Mudanças visuais ou de ciclo de vida devem indicar o que foi validado em tablet e o que permanece pendente.

Mantenha integrações opcionais separadas do núcleo. Não inclua arquivos de pareamento, cadastro de ações, configuração pessoal, chaves de assinatura ou artefatos de build. Consulte LICENSE para a licença MIT do projeto.
