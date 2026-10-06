# Integrações opcionais

O núcleo funciona sem MiningRoutine: métricas Windows, editor, pareamento, HTTP, executáveis e sequências são independentes. Nenhuma ação pessoal vem cadastrada. Cadastros só são criados quando o usuário importa ou configura uma integração.

O adaptador de exemplo [MiningRoutine](MININGROUTINE.md) permanece disponível. Ele usa a pasta escolhida pelo usuário, lê `data_directory` e valida snapshots recentes do controlador. Seu código ficará separado em `windows-common/LegacyDisplay.Windows/Integrations/MiningRoutine`; o controlador externo não faz parte deste repositório.

Novas fontes podem implementar `IAdditionalMetrics` e ser conectadas ao Agent. Um produtor pode emitir `display.blackout` como booleano para solicitar o escurecimento do tablet. O sinal precisa ser atualizado; desconexão e expiração permitem restaurar o painel. O adaptador MiningRoutine traduz seu estado confirmado para essa fonte genérica.

As funções C# precisam de registro explícito. Para serviços externos, prefira ações HTTP ou executáveis cadastrados no Studio; parâmetros e credenciais permanecem no PC protegidos por DPAPI. Não grave tokens no layout ou no Git.
