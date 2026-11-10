# T360 ModLoader

Projeto comunitário para desenvolver um gerenciador nativo de mods para Terraria no Xbox 360 RGH.

## Sites

- Site do projeto: https://davizim140.github.io/t360modloader/
- Comunidade e catálogo: https://davizim140.github.io/t360modloader/community/

## Estado atual

- Site institucional e catálogo comunitário publicados.
- **Envio de mods temporariamente fechado** enquanto o gerenciador e o processo de instalação são desenvolvidos.
- Catálogo comunitário: `community/mods.json`; apenas itens com `approved: true` aparecem.
- Formato inicial de pacote: [docs/TMOD_FORMAT.md](docs/TMOD_FORMAT.md).
- Validador e testes automatizados: `tools/` e `tests/`.
- **Gerenciador nativo XEX:** primeira compilação concluída com xsdk no GitHub Actions, na [execução #3](https://github.com/davizim140/t360modloader/actions/runs/42874416867).
- **Teste em Xbox 360 RGH:** o usuário confirmou que o XEX abriu e que D-pad, A, B e BACK funcionaram. Isso valida somente a interface e a navegação.
- **Explorador de armazenamento:** o código atual lista entradas de USB, HD interno e pasta do jogo, em modo somente leitura. Ainda não permite entrar em subpastas; essa é a próxima etapa de desenvolvimento.
- **Ainda não implementado:** instalação de pacotes, integração com os arquivos do Terraria e carregamento/ativação de mods no jogo.
- Terraria ainda precisa ser instalado no console para os testes de integração.

## Build nativo

O workflow **Build T360 ModLoader XEX** compila o gerenciador com xsdk e publica o arquivo `.xex` como artefato. A compilação #3 foi bem-sucedida e o XEX foi executado no Xbox 360 RGH. O teste confirmou somente a inicialização da interface e a navegação do controle; não confirma o funcionamento completo do explorador nem a instalação ou o carregamento de mods.

## Formato e segurança

A especificação inicial `.tmod` e o validador ficam em `docs/`, `tools/` e `tests/`. O formato inicial é limitado a assets e metadados; não executa scripts nem patches.

## Publicação no GitHub Pages

O workflow `.github/workflows/pages.yml` publica o conteúdo do repositório. Em Settings → Pages, selecione **GitHub Actions** como fonte de build e deploy, caso ainda não esteja selecionado.

## Contribuir

O envio de mods está temporariamente fechado. Enquanto isso, acompanhe o desenvolvimento pelo repositório e consulte a [especificação .tmod](docs/TMOD_FORMAT.md). O catálogo será reaberto quando houver um processo de validação e instalação que possa ser testado.

O projeto não é afiliado à Microsoft nem aos criadores de Terraria.
