# Terraria Xbox 360 — análise estática de entrada e desenho do menu

Data: 2026-11-13  
Projeto: T360 ModLoader  
Alvo analisado: assembly gerenciado extraído de `game.exe.xex` do pacote XBLA de Terraria Xbox 360.

## Resultado desta etapa

A análise estática confirma um ciclo de atualização e desenho centralizado em `Terraria.Main` e `Terraria.UI`. Isso fornece pontos concretos para estudar uma integração visual dentro do jogo. Não demonstra, por si só, que o código recompilado pode ser empacotado, carregado ou executado no Xbox 360.

## Entrada do controle

Em `Terraria/Main.cs`, método `Main.Update(GameTime)`:

1. Atualiza o estado base do jogo.
2. Chama `ui[i].UpdateGamePad()` para os quatro jogadores.
3. Chama `ui[j].Update()` para os quatro jogadores.
4. Continua a atualização dos sistemas do jogo.

Em `Terraria/UI.cs`:

- `UpdateGamePad()` copia `gpState` para `gpPrevState` e consulta `GamePad.GetState(controller)`, desde que o jogo esteja em foco.
- `IsButtonTriggered(Buttons)` identifica a transição de solto para pressionado.
- `IsButtonUntriggered(Buttons)` identifica a transição de pressionado para solto.
- Durante o jogo, o valor `16` abre o menu de pausa e o valor `32` abre o mapa.
- Os botões padrão já participam de ações do jogo e de menus. Portanto, não se deve escolher um botão isolado presumindo que esteja livre.

**Implicação:** um atalho para o ModLoader precisa ser tratado como combinação de botões e precisa ser interceptado antes da lógica normal de pausa/mapa, evitando que a mesma combinação execute duas ações. A combinação final ainda não foi escolhida nem testada.

## Desenho da interface

Em `Terraria/UI.cs`, método `UI.Draw()`:

- Desenha o fundo da visão.
- Desenha mundo e cursor quando não está no menu principal.
- Chama `DrawTopLayer()`.
- Desenha o menu normal quando aplicável.
- Finaliza o lote de desenho com `Main.spriteBatch.End()`.

Em `DrawTopLayer()`, quando `menuType == MenuType.NONE`, são desenhados a interface e o HUD.

Em `Terraria/Main.cs`, `Main.LoadContent()` cria o `SpriteBatch` compartilhado em `Main.spriteBatch`. O desenho principal também usa esse objeto em diferentes lotes.

**Implicação:** `DrawTopLayer()` é um ponto candidato para investigar uma sobreposição do ModLoader. Antes de inserir desenho novo, será necessário confirmar o estado do SpriteBatch, viewport, coordenadas e compatibilidade do pipeline XNA/Xbox. Não foi adicionado desenho ao jogo nesta etapa.

## Decisão técnica provisória

1. Manter o gerenciador nativo XEX como ferramenta externa de armazenamento e diagnóstico.
2. Tratar a interface dentro do Terraria como uma possível integração no assembly gerenciado do jogo.
3. Não assumir que existe API de plugins: a busca anterior não encontrou chamadas explícitas a `Assembly.Load`, `Assembly.LoadFrom`, `AppDomain` ou `Activator.CreateInstance`.
4. Não modificar o pacote original. Qualquer experimento futuro deve usar cópia de trabalho e preservar os arquivos originais.

## Limitações ainda abertas

- Recompilar o assembly gerenciado com as referências corretas do XNA/Xbox 360.
- Determinar o processo válido de reconstrução e empacotamento do XEX/XBLA.
- Verificar as restrições de assinatura, criptografia e execução do título.
- Testar a integração no console real sem substituir a instalação original.
- Definir e testar uma combinação de controle que não conflite com a jogabilidade.

## Status

- [x] Extração do XEX e identificação do assembly gerenciado.
- [x] Decompilação estática do código C#.
- [x] Identificação da ordem de atualização do controle.
- [x] Identificação dos pontos candidatos de desenho.
- [ ] Prova de conceito de menu integrada.
- [ ] Recompilação para o alvo Xbox 360.
- [ ] Reempacotamento verificável.
- [ ] Teste no Xbox 360 RGH.

**Conclusão:** há pontos de código plausíveis para implementar a entrada e a renderização de um menu, mas ainda não há prova de que a cadeia de recompilação e execução do título possa ser concluída.
