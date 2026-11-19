# Diretriz de integração do T360 ModLoader

## Decisão de arquitetura

A interface usada pelo jogador deve ficar **dentro do Terraria Xbox 360**. Não será necessário iniciar um segundo XEX para abrir o ModLoader.

O executável nativo separado que existe no repositório é um protótipo de pesquisa/diagnóstico e não é o destino da interface do jogador. O desenvolvimento da experiência do usuário deve priorizar o assembly gerenciado do Terraria e o menu integrado.

## Estado atual verificado

- O patch de menu adiciona a entrada `T360 ModLoader` ao menu principal.
- Selecionar a entrada abre uma tela de destino dentro do Terraria.
- A tela é somente uma página inicial experimental. Ela não lista, instala, ativa nem executa mods.
- A reconstrução do XEX em GitHub Actions passou nas verificações estáticas e de hash na execução registrada no histórico do projeto; isso não substitui teste no console.
- A instalação original do Terraria deve permanecer intacta. Toda reconstrução e teste usam cópias separadas.

## Próximas etapas técnicas

1. Implementar uma tela T360 própria dentro do ciclo de UI do Terraria, mantendo navegação pelo controle.
2. Adicionar leitura somente de pacotes e metadados em uma pasta definida, sem alterar arquivos do jogo.
3. Definir um formato de pacote compatível com o runtime disponível no Xbox 360.
4. Só depois implementar carregamento real, com validação de compatibilidade e testes isolados.

## Limitações

A entrada do menu e a tela inicial não significam que exista suporte a mods. O carregamento real depende de uma implementação compatível com o runtime .NET/XNA do título; não se deve afirmar compatibilidade com mods de PC ou tModLoader desktop sem uma prova técnica específica.
