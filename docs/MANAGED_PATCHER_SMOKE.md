# Managed IL writer validation

## Passed gates

- Mono.Cecil 0.11.6 synthetic writer test passed on GitHub Actions. It created, wrote, reopened, and re-wrote a synthetic assembly with conditional branches and a catch exception region.
- Read-only inventory of the actual extracted Terraria assembly passed: 168 types, 1,367 methods, 1,338 method bodies, zero invalid branch targets or exception-region boundaries.
- Input identity: `game, Version=1.0.4.1`; module `game.exe`; CLR runtime `v2.0.50727`; x86 console module.
- Original Terraria managed assembly SHA-256: `55b82aeb26bb366107206ee15ba8cd21804eab903febdb702d1288920bef9106`.

## Real-assembly round-trip blocker

The first separate-copy write attempt failed while Cecil was serializing a parameter constant whose type belongs to the external XNA assembly `Microsoft.Xna.Framework.Net, Version=4.0.0.0`. The runner does not have the Xbox XNA/XDK reference assemblies.

This is a dependency-resolution issue, not evidence of a corrupted input assembly. The original DLL was read-only and its hash was not changed.

## Next resolver investigation

1. Enumerate all parameter constants whose declared types come from external AssemblyRefs.
2. Determine the exact enum/type definitions Cecil needs to resolve for constant serialization.
3. Provide narrowly scoped metadata-only resolver definitions for the required external constant types, without pretending they are real XNA runtime libraries.
4. Retry the write only to a separate output path.
5. Compare full metadata/IL/resource fingerprints and verify the original input hash is unchanged.

Do not use the metadata-only resolver definitions to compile or run Terraria code. They are only an implementation detail for serializing metadata constants. No Terraria DLL patch or XEX is authorized by this test.
