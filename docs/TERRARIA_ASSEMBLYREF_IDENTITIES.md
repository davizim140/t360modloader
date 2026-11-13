# Identidades exatas das dependências gerenciadas do Terraria Xbox 360

## Resultado verificado

A execução do GitHub Actions #10 concluiu a extração e leitura da tabela CLR `AssemblyRef` com sucesso.

- Workflow: https://github.com/davizim140/t360modloader/actions/runs/43488996369
- Artefato: https://github.com/davizim140/t360modloader/actions/runs/43488996369/artifacts/14069121720
- Assembly analisado: `Terraria.Managed.dll`
- SHA-256: `55b82aeb26bb366107206ee15ba8cd21804eab903febdb702d1288920bef9106`
- Referências encontradas no metadata: **11**

## AssemblyRef

| Assembly | Versão | Cultura |
|---|---:|---|
| `mscorlib` | `2.0.5.0` | neutral |
| `System` | `2.0.5.0` | neutral |
| `System.Core` | `2.0.5.0` | neutral |
| `Microsoft.Xna.Framework` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Game` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.GamerServices` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Graphics` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Net` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Storage` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Xact` | `4.0.0.0` | neutral |
| `Microsoft.Xna.Framework.Xdk` | `4.0.0.0` | neutral |

O campo PublicKeyOrToken estava vazio nas 11 entradas. Isso é o conteúdo literal registrado na tabela AssemblyRef; não é, sozinho, uma conclusão sobre assinatura ou política de carregamento do console.

## Conclusão técnica

Agora sabemos que o código gerenciado foi compilado contra as referências **XNA 4.0.0.0** e as bibliotecas base **2.0.5.0**. A dependência mais específica é `Microsoft.Xna.Framework.Xdk, Version=4.0.0.0`.

Isso corrige a suposição anterior de que bastava procurar XNA 3.1: a referência efetiva encontrada no assembly é XNA 4.0.0.0.

Ainda não está provado que um instalador público do XNA para Windows contenha as assemblies específicas de Xbox 360/XDK. Também não foi feita recompilação do jogo, alteração do XEX ou teste de carregamento de mods.

## Próxima etapa

Verificar, por documentação e fontes legítimas, quais componentes oficiais do XNA Game Studio 4.0 e das extensões XDK fornecem essas sete assemblies XNA, especialmente `Microsoft.Xna.Framework.Xdk`. Em seguida, avaliar se existe uma forma reproduzível de disponibilizá-las ao GitHub Actions sem redistribuir binários proprietários.

