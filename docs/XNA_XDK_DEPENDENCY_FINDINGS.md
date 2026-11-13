# Dependências XNA/XDK do Terraria Xbox 360

## Resultado desta etapa

A análise do executável extraído confirmou que o código gerenciado do Terraria referencia bibliotecas da família XNA para Xbox 360. A documentação oficial do XNA Game Studio 3.1 confirma que o produto inclui bibliotecas gerenciadas XNA, mas isso, isoladamente, não comprova que ele forneça todas as dependências específicas usadas por este jogo.

Uma referência pública independente, o projeto TerrariaOGC, descreve a configuração necessária para executar o código original de Xbox 360: framework XNA feito para Xbox 360, dependência auxiliar `XNA.XDK` e um Xbox 360 Development Kit. O mesmo projeto diz que a configuração `USE_ORIGINAL_CODE` depende de um ambiente oficial de desenvolvimento Xbox 360.

## O que isso significa para o T360 ModLoader

- O xsdk usado nos nossos builds nativos de XEX não substitui automaticamente as bibliotecas gerenciadas XNA/XDK.
- O XNA Game Studio 3.1 público é uma referência útil, mas ainda não foi confirmado como fonte suficiente para as assemblies específicas do Terraria.
- Não vamos baixar nem incluir DLLs proprietárias de procedência duvidosa no repositório.
- Ainda não foi feita uma recompilação do assembly do Terraria nem criado um XEX modificado.

## Próxima verificação técnica

Extrair do metadata CLI de `Terraria.Managed.dll` os registros completos de AssemblyRef (nome, versão, cultura e public key token) e comparar com as referências de um ambiente XNA/XDK legítimo. Só depois disso faz sentido decidir se há um caminho de build reproduzível no GitHub Actions.

## Fontes

- Microsoft, XNA Game Studio 3.1: https://www.microsoft.com/en-us/download/details.aspx?id=39
- Microsoft, documentação técnica do XNA Game Studio 3.1: https://download.microsoft.com/download/E/5/E/E5E0C8AA-42F2-4825-BC27-9F7B94108073/XNA_Game_Studio_3.1_enus.pdf
- PPrism, TerrariaOGC (requisitos para executar o código original): https://github.com/PPrism/TerrariaOGC
- dptug, TerrariaXDK (decompilação do Terraria Xbox 360): https://github.com/dptug/TerrariaXDK
