# T360 ModLoader

Projeto comunitário para integrar o T360 ModLoader diretamente à interface do Terraria Xbox 360 no console RGH. O objetivo é não exigir um gerenciador externo.

## Sites

- Site do projeto: https://davizim140.github.io/t360modloader/
- Comunidade e catálogo: https://davizim140.github.io/t360modloader/community/

## Estado atual

- Site institucional e catálogo comunitário publicados.
- **Envio de mods temporariamente fechado** enquanto o gerenciador e o processo de instalação são desenvolvidos.
- Catálogo comunitário: `community/mods.json`; apenas itens com `approved: true` aparecem.
- Formato inicial de pacote: [docs/TMOD_FORMAT.md](docs/TMOD_FORMAT.md).
- Validador e testes automatizados: `tools/` e `tests/`.
- **Integração no menu principal:** patch separado do assembly gerenciado adiciona a entrada T360 ModLoader ao menu do Terraria; a tela de destino ainda é uma página inicial experimental.
- **Teste em Xbox 360 RGH:** o usuário confirmou que o XEX abriu e que D-pad, A, B e BACK funcionaram. Isso valida somente a interface e a navegação.
- **Direção definida:** navegação, lista de mods e controles do T360 devem aparecer dentro do próprio Terraria. O XEX nativo separado não será a interface final do usuário.
- **Ainda não implementado:** tela interna completa, leitura de pacotes dentro do jogo, instalação e carregamento/ativação real de mods.
- Terraria ainda precisa ser instalado no console para os testes de integração.

## Integração dentro do Terraria

O workflow **Terraria XEX static analysis** aplica o patch de menu em uma cópia separada do assembly gerenciado e reconstrói uma cópia de `game.exe.xex`. A entrada T360 abre uma tela inicial dentro do Terraria. A tela informa que o carregamento de mods ainda está em desenvolvimento; ela não carrega mods nesta fase.

O workflow **Build T360 ModLoader XEX** pertence ao protótipo nativo separado e não representa a interface final escolhida para o projeto.

## Formato e segurança

A especificação inicial `.tmod` e o validador ficam em `docs/`, `tools/` e `tests/`. O formato inicial é limitado a assets e metadados; não executa scripts nem patches.

## Publicação no GitHub Pages

O workflow `.github/workflows/pages.yml` publica o conteúdo do repositório. Em Settings → Pages, selecione **GitHub Actions** como fonte de build e deploy, caso ainda não esteja selecionado.

## Contribuir

O envio de mods está temporariamente fechado. Enquanto isso, acompanhe o desenvolvimento pelo repositório e consulte a [especificação .tmod](docs/TMOD_FORMAT.md). O catálogo será reaberto quando houver um processo de validação e instalação que possa ser testado.

O projeto não é afiliado à Microsoft nem aos criadores de Terraria.
