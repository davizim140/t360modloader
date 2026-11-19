# Auditoria de armazenamento para o catálogo interno T360

Status: **APIs de armazenamento identificadas estaticamente; leitor de pacotes ainda não implementado**.

## Evidência no Terraria decompilado

O assembly gerenciado do Terraria já usa o armazenamento XNA/Xbox:

- Terraria.UI.playerStorage mantém o StorageDeviceManager selecionado.
- Terraria.UI.OpenPlayerStorage(string name) abre um StorageContainer pelo dispositivo selecionado.
- StorageContainer.GetFileNames(string pattern) já é usado pelo próprio jogo para localizar personagens com o padrão player?.plr.
- O jogo abre streams com StorageContainer.OpenFile(...) e verifica arquivos com FileExists(...).
- O projeto referencia Microsoft.Xna.Framework.Storage versão 4.0.0.0.

Isso demonstra que existe uma API de armazenamento no próprio título. Não demonstra ainda que um novo container chamado T360Mods possa ser aberto em todos os cenários, nem que ZIP/Deflate esteja disponível no runtime do Xbox 360.

## Caminho de implementação decidido

1. Reutilizar o dispositivo de armazenamento que o próprio Terraria já selecionou; não abrir um aplicativo ou gerenciador externo.
2. Criar uma área/container exclusivo do T360 para os arquivos .tmod, sem tocar em Characters, Worlds ou Settings.
3. Fazer primeiro uma enumeração somente de leitura com GetFileNames("*.tmod").
4. Mostrar os nomes encontrados na tela interna do T360, com navegação pelo controle.
5. Só depois validar o manifesto e os hashes de cada pacote.
6. Não extrair, sobrescrever ou executar conteúdo durante a etapa de catálogo.

## Regras de segurança

- Se não houver dispositivo selecionado, a tela deve informar isso e oferecer retorno; não deve abrir seletor inesperadamente.
- Erros de I/O devem virar uma mensagem na tela, sem travar o menu.
- A listagem deve ter limite explícito de entradas e truncar nomes longos apenas na apresentação.
- A primeira versão do catálogo não executará código e não instalará mods.
- Não usar o container de personagens, mundos ou configurações.
- Não testar sobre a instalação original.

## Bloqueios ainda abertos

- Confirmar por teste no XDK/Xbox 360 o comportamento de abertura/criação do container dedicado.
- Confirmar se o runtime oferece descompressão ZIP compatível com o formato atual .tmod.
- Confirmar a disponibilidade de arquivos de pacotes no dispositivo escolhido.
- A tela atual ainda é a página provisória; nenhuma lista real de pacotes foi conectada.

## Fonte da análise

Artefato terraria-game-exe-analysis, execução GitHub Actions 44573363023, especialmente decompiled/Terraria/UI.cs, managed-assembly-refs.json e menu-integration-points.md.
