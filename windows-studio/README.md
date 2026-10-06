# LegacyDisplay Studio

Editor Windows .NET 8/WPF para layouts do protocolo v1. Inclui Canvas com arraste/redimensionamento, propriedades, undo/redo, arquivos JSON, preview dos sensores locais, pareamento, Deploy, configuração do tablet e detecção USB/ADB. O Agent é um processo independente e continua funcionando ao fechar o Studio.

Abra `artifacts/studio/LegacyDisplay.Studio.exe`. Mantenha as pastas `studio` e `agent` juntas, conforme o pacote `artifacts/LegacyDisplay-Windows.zip`. O runtime está incluído; preserve as DLLs nativas ao copiar a pasta.

O botão **Ações** abre o cadastro de HTTP, aplicativos, sequências e funções internas; a seleção nas propriedades vincula o ID ao botão. O cadastro é protegido por DPAPI e relido pelo Agent a cada toque. Consulte os guias do [Studio](../docs/STUDIO.md) e de [ações](../docs/ACTIONS.md). Para desenvolver:

```powershell
dotnet run --project windows-studio/LegacyDisplay.Studio
```

`LegacyDisplay.Studio.Core` mantém o modelo de edição e o histórico sem dependência de WPF. O preview aproxima o layout nativo; diferenças de fonte/renderização devem ser conferidas no tablet. Edição transitória ao vivo, novos widgets e descoberta LAN continuam no roadmap.
