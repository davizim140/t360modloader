# Terraria Xbox 360 — dependências de recompilação gerenciada

Data da análise: 2026-11-13  
Projeto: T360 ModLoader  
Origem: inventário automático do projeto gerado pelo ILSpy, extraído de uma cópia do pacote XBLA.

## Resultado verificado

A execução #7 do workflow `Terraria XEX static analysis` terminou com sucesso. O artefato contém `managed-build-requirements.json` e `managed-build-requirements.md`.

O projeto gerado declara:

- Target framework: `net20`
- Output type: `Exe`
- Nove referências de assembly.
- Nenhuma referência `PackageReference` do NuGet.

Referências identificadas:

- `Microsoft.Xna.Framework`
- `Microsoft.Xna.Framework.Game`
- `Microsoft.Xna.Framework.GamerServices`
- `Microsoft.Xna.Framework.Graphics`
- `Microsoft.Xna.Framework.Net`
- `Microsoft.Xna.Framework.Storage`
- `Microsoft.Xna.Framework.Xact`
- `Microsoft.Xna.Framework.Xdk`
- `System.Core`

Somente `System.Core` recebeu um `HintPath` no projeto decompilado; as referências XNA não têm caminhos explícitos.

## O que isso significa

O assembly de Terraria não é um projeto C# genérico que possa ser reconstruído apenas com o compilador .NET moderno. Ele depende de assemblies gerenciados específicos do XNA e de extensões voltadas ao Xbox 360.

A documentação oficial do XNA Game Studio 3.1 descreve o produto como ambiente para desenvolver para Xbox 360 e informa que inclui o XNA Framework 3.1, bibliotecas gerenciadas baseadas em .NET Framework 2.0. Para desenvolvimento Xbox 360, a página oficial também indica a exigência histórica de uma assinatura Premium do XNA Creators Club:  
https://www.microsoft.com/en-us/download/details.aspx?id=39

A documentação técnica oficial descreve o XNA Framework como bibliotecas gerenciadas para Windows, Xbox 360 e Zune:  
https://download.microsoft.com/download/E/5/E/E5E0C8AA-42F2-4825-BC27-9F7B94108073/XNA_Game_Studio_3.1_enus.pdf

A documentação histórica do programa XNA também menciona extensões XDK para desenvolvimento de títulos Xbox LIVE Arcade/retail com XNA:  
https://legacyupdate.net/download-center/download/3409/gdc-2009-xna-game-studio-program-overview

**Importante:** a existência do XNA Game Studio 3.1 público não prova que ele forneça os assemblies de extensão XDK específicos exigidos por este jogo. Também não prova que o projeto extraído possa ser recompilado fielmente a partir do código decompilado.

## Papel do xsdk

O xsdk usado pelo projeto T360 ModLoader é a ferramenta da trilha nativa C/C++ para construir o gerenciador XEX independente. Ele não substitui automaticamente as referências C# do XNA nem o pipeline de compilação do assembly gerenciado de Terraria.

Portanto, manteremos duas trilhas distintas:

1. **Gerenciador nativo:** XEX C/C++ compilado com xsdk via GitHub Actions.
2. **Integração no Terraria:** pesquisa separada sobre reconstrução do assembly gerenciado e compatibilidade com as bibliotecas XNA/XDK originais.

## Próximo teste permitido e necessário

Antes de qualquer tentativa de reconstruir o jogo, precisamos obter uma fonte legítima e compatível para as referências XNA/XDK exatas e verificar suas versões/identidades de assembly. Não vamos substituir essas referências por bibliotecas de desktop, NuGet ou implementações aproximadas e afirmar compatibilidade.

Quando as referências corretas estiverem disponíveis, o teste deve ser isolado e reproduzível: compilar uma cópia do assembly sem alterar o pacote original, comparar referências e metadados e só então avaliar a próxima etapa.

## Estado

- [x] Extrair o assembly gerenciado embutido.
- [x] Decompilar o código C#.
- [x] Inventariar framework, tipo de saída e referências.
- [x] Confirmar que o projeto gerado depende de assemblies XNA/XDK não resolvidos pelo projeto extraído.
- [ ] Identificar a origem e versão exatas dos assemblies XNA/XDK requeridos.
- [ ] Preparar ambiente de build compatível.
- [ ] Compilar uma cópia de teste.
- [ ] Validar empacotamento e execução em ambiente controlado.

**Conclusão:** ainda não há base técnica para prometer uma recompilação funcional. O bloqueio imediato está nas dependências gerenciadas XNA/XDK específicas, não na extração ou decompilação do código.
