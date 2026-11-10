# T360 ModLoader

Projeto comunitário para desenvolver um gerenciador nativo de mods para Terraria no Xbox 360 RGH.

## Sites

- Site do projeto: https://davizim140.github.io/t360modloader/
- Comunidade e catálogo: https://davizim140.github.io/t360modloader/community/

## Estado atual

- Site institucional e catálogo comunitário publicados.
- Envio de mods pelo formulário GitHub Issues; publicações exigem revisão manual.
- Catálogo: `community/mods.json`; apenas itens com `approved: true` aparecem.
- Formato inicial de pacote: [docs/TMOD_FORMAT.md](docs/TMOD_FORMAT.md).
- Validador e testes automatizados: `tools/` e `tests/`.
- **Gerenciador nativo XEX:** primeira compilação concluída com xsdk no GitHub Actions, na [execução #3](https://github.com/davizim140/t360modloader/actions/runs/42874416867).
- **Teste em Xbox 360 RGH:** o usuário confirmou que o XEX abriu e que D-pad, A, B e BACK funcionaram.
- **Em desenvolvimento:** explorador de armazenamento somente leitura para USB, HD interno e pasta do jogo. A nova compilação está em andamento na [execução #5](https://github.com/davizim140/t360modloader/actions/runs/42882153362); essa função ainda não foi testada no console.
- **Ainda não implementado:** instalação de pacotes, integração com os arquivos do Terraria e carregamento/ativação de mods no jogo.
- Terraria ainda não foi instalado no console para testes de integração.

## Build nativo

O workflow **Build T360 ModLoader XEX** compila o gerenciador com xsdk e publica o arquivo `.xex` como artefato. A compilação #3 foi bem-sucedida e o XEX foi executado no Xbox 360 RGH. O teste confirmou somente a inicialização da interface e a navegação do controle; não confirma instalação ou carregamento de mods.

## Formato e segurança

A especificação inicial `.tmod` e o validador ficam em `docs/`, `tools/` e `tests/`. O formato inicial é limitado a assets e metadados; não executa scripts nem patches.

## Publicação no GitHub Pages

O workflow `.github/workflows/pages.yml` publica o conteúdo do repositório. Em Settings → Pages, selecione **GitHub Actions** como fonte de build e deploy, caso ainda não esteja selecionado.

## Contribuir

Abra uma submissão em [Enviar um mod](https://github.com/davizim140/t360modloader/issues/new?template=mod_submission.yml). Leia primeiro a [especificação .tmod](docs/TMOD_FORMAT.md).

O projeto não é afiliado à Microsoft nem aos criadores de Terraria.
