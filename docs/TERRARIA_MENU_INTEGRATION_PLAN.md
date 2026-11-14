# Terraria Xbox 360 — plano de integração do menu T360

Status: pesquisa estática; nenhuma alteração no assembly ou no pacote do jogo.

## Pontos de código verificados

A análise de C# decompilado identificou:

- `Terraria.Main.Update(GameTime)`: atualiza o estado dos controles em `ui[i].UpdateGamePad()` e depois chama `ui[j].Update()` para até quatro interfaces.
- `Terraria.UI.UpdateGamePad()`: copia `gpState` para `gpPrevState` e consulta `GamePad.GetState(controller)` quando o jogo está em foco.
- `Terraria.UI.Update()`: quando `menuType == MenuType.NONE`, trata a soltura de `Buttons 16` como pausa e o pressionamento de `Buttons 32` como mapa. Esses valores não são atalhos livres.
- `Terraria.UI.Draw()`: chama `DrawTopLayer()` e depois encerra `Main.spriteBatch`.
- `Terraria.UI.DrawTopLayer()`: desenha interface e HUD quando `menuType == MenuType.NONE`.
- `Terraria.Main.Draw(GameTime)`: chama `ui[j].Draw()` antes de iniciar outro lote para chat e ícone de salvamento.

## Contexto de renderização confirmado

A análise adicional identificou uma janela candidata mais segura para desenhar o overlay:

- `Main.Draw(GameTime)` chama `ui[j].Draw()` antes de abrir um novo `SpriteBatch`.
- Em seguida, abre o lote com `spriteBatch.Begin()`, desenha chat e ícone de salvamento e fecha com `spriteBatch.End()`.
- `UI.Draw()` encerra o lote anterior com `Main.spriteBatch.End()`.
- O jogo já possui `UI.fontSmallOutline`, carregada do asset interno `Fonts/small2`.

**Ponto candidato:** adicionar a chamada de desenho do T360 imediatamente antes do `spriteBatch.End()` final em `Main.Draw()`. Assim o overlay usa o lote que já está aberto e a fonte existente, sem introduzir dependências XNA novas nem abrir/fechar outro lote. O relatório automatizado `menu-render-context.md` verifica essa ordem.

Isto ainda não é um patch: a compatibilidade visual precisa ser validada em execução, e o caminho de compilação do assembly continua bloqueado pelas referências XNA/XDK ausentes.

## Próxima implementação, em ordem

1. Confirmar o estado de início/fim do SpriteBatch e os estados gráficos usados por `DrawTopLayer()`; não inserir desenho até isso estar provado.
2. Definir uma combinação de controle somente após mapear os estados completos de `GamePadState` e os conflitos em todos os modos de jogo. Interceptar e consumir a combinação antes da lógica de pausa/mapa.
3. Obter de forma legítima as referências gerenciadas XNA/XDK exatas e suas identidades/versões, especialmente `Microsoft.Xna.Framework.Xdk, Version=4.0.0.0`.
4. Fazer uma compilação isolada do assembly modificado e comparar metadados/referências com o original antes de estudar reempacotamento.

## Limitações e segurança

- O mapa de métodos é evidência estática do código decompilado; não é execução no Xbox.
- O projeto extraído declara `net20` e depende de assemblies XNA 4.0.0.0, incluindo a extensão XDK. Essas referências não estão presentes no projeto extraído.
- O `xsdk` continua sendo a trilha nativa para o gerenciador XEX; não substitui as bibliotecas C# XNA/XDK.
- Não há prova de recompilação, reempacotamento, carregamento ou execução de um Terraria modificado.
- Todos os próximos experimentos devem usar cópias. O pacote XBLA original e a instalação do console permanecem intocados.

## Automação

O workflow `.github/workflows/terraria-xex-analysis.yml` gera `menu-integration-points.json` e `menu-integration-points.md` no artefato `terraria-game-exe-analysis`. O relatório falha se os métodos ou âncoras esperados desaparecerem do código decompilado.
