# T360 ModLoader

Projeto comunitário para desenvolver um gerenciador nativo de mods para Terraria no Xbox 360 RGH.

## Sites

- Site do projeto: https://davizim140.github.io/t360modloader/
- Comunidade e catálogo: https://davizim140.github.io/t360modloader/community/

## Estado atual

- Site institucional e catálogo comunitário: implementados.
- Envio de mods: formulário GitHub Issues; publicações exigem revisão manual.
- Catálogo: `community/mods.json`; apenas itens com `approved: true` aparecem.
- Formato inicial de pacote: [docs/TMOD_FORMAT.md](docs/TMOD_FORMAT.md).
- Validador e testes: `tools/` e `tests/`.
- Gerenciador nativo Xbox 360: interface C em `native/`, com menu, navegação pelo controle e telas de diagnóstico/sobre.
- Build XEX: concluído com sucesso no GitHub Actions, execução [#3](https://github.com/davizim140/t360modloader/actions/runs/42874416867), usando xsdk.
- Teste em hardware: usuário confirmou que o XEX abriu no Xbox 360 RGH e que a navegação pelos botões do controle funcionou.
- Limite do teste: isso valida a inicialização da interface e a entrada do controle; não valida ainda acesso a arquivos, instalação ou carregamento de mods.
- Carregamento, instalação e ativação de mods no Terraria: ainda não implementados.

## Build nativo

O workflow **Build T360 ModLoader XEX** compila o gerenciador com xsdk e publica o arquivo `.xex` como artefato. A execução #3 foi concluída com sucesso e o usuário confirmou a inicialização e a navegação no Xbox 360 RGH. Isso ainda não significa que o carregamento de mods esteja implementado.

## Publicação no GitHub Pages

O workflow `.github/workflows/pages.yml` publica o conteúdo do repositório. Em Settings → Pages, selecione **GitHub Actions** como fonte de build e deploy, caso ainda não esteja selecionado.

## Contribuir

Abra uma submissão em [Enviar um mod](https://github.com/davizim140/t360modloader/issues/new?template=mod_submission.yml). Leia primeiro a [especificação .tmod](docs/TMOD_FORMAT.md).

O projeto não é afiliado à Microsoft nem aos criadores de Terraria.
