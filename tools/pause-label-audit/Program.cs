using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseLabelAudit <input.dll> <report.json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var reportPath = Path.GetFullPath(args[1]);
var sourceHash = Hash(input);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var methods = ui.Methods.Where(m => m.Name == "UpdateMenu" && m.Parameters.Count == 0 && m.HasBody).ToArray();
if (methods.Length != 1) throw new InvalidOperationException($"Expected one UpdateMenu body; found {methods.Length}.");
var method = methods[0];
var ins = method.Body.Instructions;

var sequences = new List<int>();
for (int i = 0; i < ins.Count; i++)
{
    var cursor = i;
    var assignments = new List<object>();
    var expectedLangIds = new[] { 108, 106, 107, 99, 101 };
    var valid = true;
    for (int offset = 0; offset < expectedLangIds.Length; offset++)
    {
        var start = cursor;
        if (cursor + 6 >= ins.Count || ins[cursor].OpCode.Code != Code.Ldarg_0 ||
            !IsField(ins[cursor + 1], "menuString") ||
            !IsLocal(ins[cursor + 2], 9))
        {
            valid = false;
            break;
        }
        cursor += 3;
        if (offset > 0)
        {
            if (!TryInt(ins[cursor], out var addend) || addend != offset ||
                ins[cursor + 1].OpCode.Code != Code.Add)
            {
                valid = false;
                break;
            }
            cursor += 2;
        }
        if (ins[cursor].OpCode.Code != Code.Ldsfld ||
            ins[cursor].Operand is not FieldReference lang || lang.Name != "menu" ||
            lang.DeclaringType.FullName != "Terraria.Lang" ||
            !TryInt(ins[cursor + 1], out var langId) || langId != expectedLangIds[offset] ||
            ins[cursor + 2].OpCode.Code != Code.Ldelem_Ref ||
            ins[cursor + 3].OpCode.Code != Code.Stelem_Ref)
        {
            valid = false;
            break;
        }
        cursor += 4;
        assignments.Add(new { relativeIndex = offset, languageMenuIndex = langId, startInstruction = start, endInstruction = cursor - 1 });
    }
    if (valid) sequences.Add(i);
}

var unique = sequences.Count == 1;
var target = unique ? sequences[0] : -1;
var existingNextIndex5Write = false;
if (unique)
{
    // The next instruction after the five existing label writes must begin the menuScale initialization.
    var end = target;
    for (int offset = 0; offset < 5; offset++)
        end += offset == 0 ? 7 : 9;
    existingNextIndex5Write = FindRelativeMenuStringWrite(ins, end, 9, 5);
}
var hashAfter = Hash(input);
var pass = unique && !existingNextIndex5Write && sourceHash == hashAfter;
var report = new
{
    status = pass ? "pause-label-anchor-audit-pass" : "pause-label-anchor-audit-fail",
    mode = "read-only-no-patch",
    source = new { file = Path.GetFileName(input), sha256Before = sourceHash, sha256After = hashAfter },
    target = new
    {
        method = method.FullName,
        matchingSequenceCount = sequences.Count,
        firstInstruction = target,
        existingRelativeLabelWrites = new[] { 0, 1, 2, 3, 4 },
        existingLangMenuIndexes = new[] { 108, 106, 107, 99, 101 },
        newRelativeIndex5WriteAlreadyPresent = existingNextIndex5Write,
        candidateInsertionPoint = unique ? target + 7 + 9 + 9 + 9 + 9 : -1
    },
    safety = new
    {
        sourceFileUnchanged = sourceHash == hashAfter,
        assemblyWritten = false,
        xexProduced = false,
        runtimeXbox360TestPerformed = false,
        note = "This audit only identifies the existing pause-menu label sequence and a candidate insertion point. It does not add a label or change menu counts."
    }
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause label anchor audit: {report.status}");
Console.WriteLine($"Unique label sequence: {unique}; existing index 5 assignment: {existingNextIndex5Write}");
return pass ? 0 : 1;

static bool IsField(Instruction instruction, string name) =>
    instruction.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName == "Terraria.UI";

static bool IsLocal(Instruction instruction, int index)
{
    if (instruction.Operand is VariableDefinition variable)
        return variable.Index == index && instruction.OpCode.Code is Code.Ldloc or Code.Ldloc_S;
    return instruction.OpCode.Code switch
    {
        Code.Ldloc_0 => index == 0,
        Code.Ldloc_1 => index == 1,
        Code.Ldloc_2 => index == 2,
        Code.Ldloc_3 => index == 3,
        _ => false
    };
}

static bool FindRelativeMenuStringWrite(Mono.Collections.Generic.Collection<Instruction> ins, int start, int local, int relative)
{
    for (int i = Math.Max(0, start); i + 8 < ins.Count; i++)
        if (ins[i].OpCode.Code == Code.Ldarg_0 && IsField(ins[i + 1], "menuString") &&
            IsLocal(ins[i + 2], local) && TryInt(ins[i + 3], out var offset) && offset == relative &&
            ins[i + 4].OpCode.Code == Code.Add && ins[i + 5].OpCode.Code == Code.Ldsfld &&
            ins[i + 5].Operand is FieldReference lang && lang.Name == "menu" &&
            ins[i + 6].OpCode.Code == Code.Ldc_I4_S &&
            ins[i + 7].OpCode.Code == Code.Ldelem_Ref && ins[i + 8].OpCode.Code == Code.Stelem_Ref)
            return true;
    return false;
}

static bool TryInt(Instruction instruction, out int value)
{
    value = instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1, Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3, Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4 => instruction.Operand is int n ? n : int.MinValue,
        Code.Ldc_I4_S => instruction.Operand is sbyte s ? s : instruction.Operand is byte b ? b : int.MinValue,
        _ => int.MinValue
    };
    return value != int.MinValue;
}

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
