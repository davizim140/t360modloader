using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) {
    Console.Error.WriteLine("Usage: PauseSelectionAudit <input.dll> <report.json>");
    return 2;
}
var input = Path.GetFullPath(args[0]);
var reportPath = Path.GetFullPath(args[1]);
var before = Hash(input);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters {
    InMemory = true, ReadSymbols = false, ReadingMode = ReadingMode.Immediate
});
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var method = ui.Methods.Single(m => m.Name == "UpdateMenu" && m.Parameters.Count == 0 && m.HasBody);
var ins = method.Body.Instructions;
var matches = new List<int>();
for (int i=0; i+5<ins.Count; i++) {
    if (ins[i].OpCode.Code == Code.Ldarg_0 &&
        IsField(ins[i+1], "selectedMenu") &&
        IsLocal(ins[i+2], 9) &&
        IsInt(ins[i+3], 4) &&
        ins[i+4].OpCode.Code == Code.Add &&
        ins[i+5].OpCode.Code == Code.Bne_Un)
        matches.Add(i);
}
var pass = matches.Count == 1 && before == Hash(input);
var target = matches.Count == 1 ? matches[0] : -1;
var exitBranch = target < 0 ? null : ins[target+5];
var exitTargetIndex = exitBranch?.Operand is Instruction branchTarget ? ins.IndexOf(branchTarget) : -1;
var report = new {
 status = pass ? "pause-selection-anchor-audit-pass" : "pause-selection-anchor-audit-fail",
 mode = "read-only-no-patch",
 source = new { file = Path.GetFileName(input), sha256Before = before, sha256After = Hash(input) },
 target = new {
  method = method.FullName,
  matchCount = matches.Count,
  exitConditionStartIndex = target,
  existingExitCondition = "selectedMenu == local9 + 4",
  existingExitBranchTargetIndex = exitTargetIndex,
  existingExitHandlerPreserved = pass,
  newRelativeIndex5HandlerAlreadyPresent = false,
  candidate = "change the non-exit branch target to a new index5 handler, leaving the existing exit body unchanged"
 },
 safety = new { sourceFileUnchanged = before == Hash(input), assemblyWritten = false, xexProduced = false, runtimeXbox360TestPerformed = false }
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause selection anchor audit: {report.status}; matches={matches.Count}; exit target={exitTargetIndex}");
return pass ? 0 : 1;

static bool IsField(Instruction i, string name) => i.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName == "Terraria.UI";
static bool IsLocal(Instruction i, int index) => i.OpCode.Code == Code.Ldloc_S && i.Operand is VariableDefinition v && v.Index == index;
static bool IsInt(Instruction i, int n) => i.OpCode.Code switch {
 Code.Ldc_I4_M1 => n == -1, Code.Ldc_I4_0 => n == 0, Code.Ldc_I4_1 => n == 1,
 Code.Ldc_I4_2 => n == 2, Code.Ldc_I4_3 => n == 3, Code.Ldc_I4_4 => n == 4,
 Code.Ldc_I4_5 => n == 5, Code.Ldc_I4_6 => n == 6, Code.Ldc_I4_7 => n == 7,
 Code.Ldc_I4_8 => n == 8, Code.Ldc_I4 => i.Operand is int x && x == n,
 Code.Ldc_I4_S => i.Operand is sbyte x && x == n || i.Operand is byte y && y == n, _ => false
};
static string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
