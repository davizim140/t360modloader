using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseHeightAudit <input.dll> <report.json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var reportPath = Path.GetFullPath(args[1]);
var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var methods = ui.Methods.Where(m => m.Name == "SetMenu" && m.Parameters.Count == 3 && m.HasBody).ToArray();
if (methods.Length != 1)
    throw new InvalidOperationException($"Expected one Terraria.UI::SetMenu method with three parameters; found {methods.Length}.");

var method = methods[0];
var instructions = method.Body.Instructions;
var matches = new List<int>();
for (int i = 2; i + 3 < instructions.Count; i++)
{
    if (instructions[i].OpCode.Code == Code.Stfld &&
        IsField(instructions[i], "uiHeight") &&
        TryInt(instructions[i - 1], out var height) && height == 7 &&
        instructions[i + 1].OpCode.Code == Code.Ldarg_0 &&
        instructions[i + 2].OpCode.Code == Code.Ldsfld &&
        IsField(instructions[i + 2], "MENU_PAUSE_COORDS") &&
        instructions[i + 3].OpCode.Code == Code.Stfld &&
        IsField(instructions[i + 3], "uiCoords"))
        matches.Add(i - 1);
}

var targetIndex = matches.Count == 1 ? matches[0] : -1;
var context = targetIndex < 0 ? Array.Empty<object>() : instructions
    .Skip(Math.Max(0, targetIndex - 4))
    .Take(Math.Min(instructions.Count - Math.Max(0, targetIndex - 4), 12))
    .Select((instruction, offset) => new
    {
        index = Math.Max(0, targetIndex - 4) + offset,
        opcode = instruction.OpCode.Code.ToString(),
        operand = DescribeOperand(instruction)
    }).Cast<object>().ToArray();

var sourceHashAfter = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
var pass = matches.Count == 1 && sourceHash == sourceHashAfter;
var report = new
{
    status = pass ? "pause-height-target-audit-pass" : "pause-height-target-audit-fail",
    mode = "read-only-no-patch",
    source = new { file = Path.GetFileName(input), sha256Before = sourceHash, sha256After = sourceHashAfter },
    target = new
    {
        method = method.FullName,
        matchCount = matches.Count,
        instructionIndex = targetIndex,
        currentHeight = matches.Count == 1 ? 7 : (int?)null,
        expectedCoordinateField = "Terraria.UI::MENU_PAUSE_COORDS",
        context
    },
    safety = new
    {
        sourceFileUnchanged = sourceHash == sourceHashAfter,
        assemblyWritten = false,
        xexProduced = false,
        runtimeXbox360TestPerformed = false,
        note = "This is a read-only target audit. It does not change uiHeight or enable an eighth menu position."
    }
};
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause uiHeight target audit: {report.status}");
Console.WriteLine($"Unique pause branch: {matches.Count == 1}; instruction index: {targetIndex}");
Console.WriteLine($"Source unchanged: {sourceHash == sourceHashAfter}");
return pass ? 0 : 1;

static bool IsField(Instruction instruction, string name) =>
    instruction.Operand is FieldReference field &&
    field.Name == name && field.DeclaringType.FullName == "Terraria.UI";

static bool TryInt(Instruction instruction, out int value)
{
    value = instruction.OpCode.Code switch
    {
        Code.Ldc_I4_M1 => -1,
        Code.Ldc_I4_0 => 0,
        Code.Ldc_I4_1 => 1,
        Code.Ldc_I4_2 => 2,
        Code.Ldc_I4_3 => 3,
        Code.Ldc_I4_4 => 4,
        Code.Ldc_I4_5 => 5,
        Code.Ldc_I4_6 => 6,
        Code.Ldc_I4_7 => 7,
        Code.Ldc_I4_8 => 8,
        Code.Ldc_I4 => instruction.Operand is int n ? n : int.MinValue,
        Code.Ldc_I4_S => instruction.Operand is sbyte s ? s : instruction.Operand is byte b ? b : int.MinValue,
        _ => int.MinValue
    };
    return value != int.MinValue;
}

static string DescribeOperand(Instruction instruction) => instruction.Operand switch
{
    null => "",
    FieldReference field => field.FullName,
    MethodReference method => method.FullName,
    TypeReference type => type.FullName,
    VariableDefinition variable => $"V_{variable.Index}:{variable.VariableType.FullName}",
    ParameterDefinition parameter => $"arg_{parameter.Index}:{parameter.ParameterType.FullName}",
    string value => value,
    IFormattable value => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    _ => instruction.Operand.ToString() ?? ""
};
