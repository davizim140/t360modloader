# Dependências XNA/XDK do Terraria Xbox 360

## Resultado desta etapa

A inspeção do metadata de `Terraria.Managed.dll` confirmou 11 referências AssemblyRef. Entre elas estão as assemblies XNA 4.0.0.0, incluindo `Microsoft.Xna.Framework.Xdk`; as referências principais do runtime `mscorlib`, `System` e `System.Core` aparecem na versão 2.0.5.0. A lista completa está em [TERRARIA_ASSEMBLYREF_IDENTITIES.md](TERRARIA_ASSEMBLYREF_IDENTITIES.md).

A pesquisa identificou uma distribuição oficial da Microsoft para **XNA Game Studio 4.0** e outra para **XNA Game Studio 4.0 Refresh**. A página oficial descreve o XNA Game Studio 4.0 como ambiente para jogos Windows, Xbox 360 e Windows Phone, e informa que inclui o XNA Framework 4.0. Isso confirma a existência do pacote público, mas não confirma que ele contenha a extensão XDK nem a assembly específica `Microsoft.Xna.Framework.Xdk` necessária para reproduzir o build deste jogo.

Uma referência técnica comunitária sobre desenvolvimento Xbox 360 distingue o XNA Game Studio 4.0 Refresh das **XNA 4.0 XDK Extensions**. Segundo essa referência, as extensões XDK são necessárias para gerar XEX de jogos C# destinados a execução offline no console. Ela identifica o instalador como `xdk-xna-extensions.msi`, mas não fornece redistribuição do pacote. Não tratamos essa informação como confirmação oficial da Microsoft nem como autorização para redistribuir o instalador.

O projeto TerrariaOGC também descreve dependências específicas do ambiente XNA/XDK para compilar o código original do Terraria Xbox 360.

## O que isso significa para o T360 ModLoader

- O xsdk usado nos builds nativos de XEX não substitui automaticamente o conjunto de assemblies gerenciadas XNA/XDK.
- O instalador público XNA Game Studio 4.0/Refresh, sozinho, ainda não foi comprovado como suficiente.
- A dependência `Microsoft.Xna.Framework.Xdk` continua sendo o bloqueio principal a resolver.
- Não vamos baixar nem incluir DLLs ou instaladores proprietários de procedência duvidosa no repositório.
- Ainda não foi feita uma recompilação de `Terraria.Managed.dll` nem criado um XEX modificado.
- Não é seguro assumir que um assembly XNA de Windows seja intercambiável com a versão destinada ao Xbox 360.

## Próxima verificação técnica

Precisamos de uma fonte legítima e compatível das XNA 4.0 XDK Extensions e das assemblies Xbox 360 correspondentes. Se esse conjunto não estiver disponível no ambiente de build autorizado, o caminho de recompilação gerenciada não poderá ser considerado reproduzível no GitHub Actions.

Até resolver essa dependência, o trabalho deve permanecer na análise estática e no desenho da integração, sem substituir assemblies do jogo nem testar um XEX modificado no console.

## Fontes

- Microsoft, XNA Game Studio 4.0: https://www.microsoft.com/en-us/download/details.aspx?id=23714
- Microsoft, XNA Game Studio 4.0 Refresh: https://www.microsoft.com/en-US/Download/details.aspx?id=27599
- Pilosophos' Circle, Xbox 360 development tools (referência comunitária sobre XNA 4.0 XDK Extensions): https://pilosophos.com/writing/xbox-360-development-tools
- PPrism, TerrariaOGC (requisitos para executar o código original): https://github.com/PPrism/TerrariaOGC
- dptug, TerrariaXDK (decompilação do Terraria Xbox 360): https://github.com/dptug/TerrariaXDK
