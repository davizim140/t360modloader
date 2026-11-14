# Terraria Xbox 360 — inventário estático dos controles

## Resultado

A análise automática percorreu **106 arquivos C#** decompilados e encontrou **17 valores numéricos** usados em casts para `Buttons`. O relatório é produzido pelo script `tools/report_controller_bindings.py` durante o workflow `Terraria XEX static analysis`.

| Valor | Constantes do próprio jogo | Ocorrências de casts |
|---:|---|---:|
| 1 | — | 3 |
| 2 | — | 3 |
| 4 | — | 5 |
| 8 | — | 5 |
| 16 | — | 6 |
| 32 | — | 4 |
| 64 | `BTN_JUMP2` | 4 |
| 128 | `BTN_CURSOR_MODE` | 3 |
| 256 | `BTN_PREV_ITEM` | 6 |
| 512 | `BTN_NEXT_ITEM` | 7 |
| 4096 | `BTN_JUMP`, `BTN_RESPAWN`, `BTN_INVENTORY_SELECT`, `BTN_NPC_CHAT_SELECT` | 35 |
| 8192 | `BTN_INTERACT`, `BTN_INVENTORY_CLOSE`, `BTN_NPC_CHAT_CLOSE` | 12 |
| 16384 | `BTN_DROP`, `BTN_INVENTORY_SELL_OR_TRASH`, `BTN_INVENTORY_DROP` | 19 |
| 32768 | `BTN_INVENTORY_OPEN` | 9 |
| 4194304 | `BTN_USE`, `BTN_INVENTORY_ACTION` | 13 |
| 8388608 | `BTN_INVENTORY_HOUSING`, `BTN_GRAPPLE` | 6 |

## Comportamentos confirmados no código

- O valor `16` é verificado em `UI.Update()` para detectar a soltura do botão que abre o menu de pausa.
- O valor `32` é verificado em `UI.Update()` para abrir o mapa.
- O jogo já associa diversos valores a ações de jogabilidade, inventário, seleção e navegação.
- `BTN_JUMP2`, `BTN_CURSOR_MODE`, `BTN_PREV_ITEM` e `BTN_NEXT_ITEM` são campos/constantes explícitos em `Terraria.UI`.

## Decisão para o T360 ModLoader

**Nenhum botão isolado foi declarado livre.** Não vamos escolher um atalho com base apenas na ausência de uma ocorrência textual: o jogo também lê estados do controle, e a framework pode processar entradas fora dos casts encontrados.

O futuro atalho deverá ser uma combinação de botões, detectada antes da lógica normal de pausa/mapa e consumida para que a mesma entrada não execute uma ação original. A combinação ainda não foi escolhida nem testada no console.

## Limitações

- É uma análise estática do código decompilado, não uma captura de entradas em tempo real.
- A contagem é de casts numéricos `(Buttons)value`; não representa necessariamente todas as leituras de controle.
- Não prova que valores ausentes estejam livres.
- Não implementa nem testa um menu dentro do Terraria.

## Reprodutibilidade

O workflow gera os arquivos `controller-bindings.json` e `controller-bindings.md` no artefato `terraria-game-exe-analysis`.

- Script: [tools/report_controller_bindings.py](../tools/report_controller_bindings.py)
- Workflow: [terraria-xex-analysis.yml](../.github/workflows/terraria-xex-analysis.yml)
- Última execução bem-sucedida: https://github.com/davizim140/t360modloader/actions/runs/43592904150
