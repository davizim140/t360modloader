using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseCoordinatePatch <input.dll> <separate-output.dll>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (!File.Exists(input)) throw new FileNotFoundException("Input DLL is missing.", input);
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Refusing to overwrite the source DLL.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

const int newX = 480;
const int newY = 497;
var inputHash = Hash(input);

using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});

if (assembly.Name.Name != "game" || assembly.MainModule.Name != "game.exe")
    throw new InvalidOperationException("Unexpected assembly identity; refusing to patch.");
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var cctorMatches = ui.Methods.Where(m => m.Name == ".cctor" && m.IsStatic && m.Parameters.Count == 0 && m.HasBody).ToArray();
if (cctorMatches.Length != 1) throw new InvalidOperationException($"Expected one Terraria.UI::.cctor; found {cctorMatches.Length}.");
var cctor = cctorMatches[0];
var il = cctor.Body.GetILProcessor();
var instructions = cctor.Body.Instructions;

var field = ui.Fields.Single(f => f.Name == "MENU_PAUSE_COORDS" && f.FieldType is ArrayType a && a.ElementType.FullName == "Terraria.Location");
var location = field.FieldType is ArrayType arrayType ? arrayType.ElementType : throw new InvalidOperationException("Pause coordinate field is not an array.");
var ctorMatches = ui.Methods.Where(m => m.Name == ".ctor" && m.Parameters.Count == 2 &&
    m.Parameters[0].ParameterType.MetadataType == MetadataType.Int32 &&
    m.Parameters[1].ParameterType.MetadataType == MetadataType.Int32 &&
    m.DeclaringType.FullName == "Terraria.Location").ToArray();
if (ctorMatches.Length != 1) throw new InvalidOperationException($"Expected one Location(int,int) constructor; found {ctorMatches.Length}.");
var locationCtor = ctorMatches[0];

var allocations = new List<ArrayTarget>();
for (int i = 1; i < instructions.Count - 1; i++)
{
    if (instructions[i].OpCode.Code != Code.Newarr ||
        instructions[i].Operand is not TypeReference allocated ||
        allocated.FullName != "Terraria.Location" ||
        !TryInt(instructions[i - 1], out var length) || length != 7 ||
        !TryStoredLocal(instructions[i + 1], out var local))
        continue;

    var store = instructions.Skip(i + 2).TakeWhile(x => x != null)
        .FirstOrDefault(x => x.OpCode.Code == Code.Stsfld && x.Operand is FieldReference f && f.FullName == field.FullName);
    if (store is null) continue;
    allocations.Add(new ArrayTarget(instructions[i - 1], instructions[i], local, store));
}
if (allocations.Count != 1) throw new InvalidOperationException($"Expected exactly one seven-slot pause array allocation; found {allocations.Count}.");

var target = allocations[0];
var storeIndex = instructions.IndexOf(target.FieldStore);
if (storeIndex < 1 || !TryLoadedLocal(instructions[storeIndex - 1], out var storedLocal) || storedLocal != target.Local)
    throw new InvalidOperationException("Pause array store does not use the allocated local.");

var existing = new List<Coordinate>();
for (int i = instructions.IndexOf(target.NewArray) + 2; i + 6 < storeIndex; i++)
{
    if (!TryLoadedLocal(instructions[i], out var local) || local != target.Local ||
        !TryInt(instructions[i + 1], out var index) ||
        instructions[i + 2].OpCode.Code != Code.Ldelema ||
        instructions[i + 2].Operand is not TypeReference element || element.FullName != "Terraria.Location" ||
        !TryInt(instructions[i + 3], out var x) ||
        !TryInt(instructions[i + 4], out var y) ||
        instructions[i + 5].OpCode.Code != Code.Newobj ||
        instructions[i + 5].Operand is not MethodReference ctor || ctor.FullName != locationCtor.FullName ||
        instructions[i + 6].OpCode.Code != Code.Stobj ||
        instructions[i + 6].Operand is not TypeReference storedType || storedType.FullName != "Terraria.Location")
        continue;
    existing.Add(new Coordinate(index, x, y, instructions[i]));
}

var expectedY = new[] { 221, 259, 297, 334, 372, 421, 459 };
if (existing.Count != 7 ||
    !existing.Select(c => c.Index).SequenceEqual(Enumerable.Range(0, 7)) ||
    existing.Any(c => c.X != 480 || c.Y != expectedY[c.Index]))
    throw new InvalidOperationException("Existing pause coordinate initializers do not match the verified seven-slot layout.");

var anchor = instructions[storeIndex - 1];
var newInstructions = new[]
{
    Instruction.Create(OpCodes.Ldloc, target.Local),
    Instruction.Create(OpCodes.Ldc_I4_7),
    Instruction.Create(OpCodes.Ldelema, location),
    Instruction.Create(OpCodes.Ldc_I4, newX),
    Instruction.Create(OpCodes.Ldc_I4, newY),
    Instruction.Create(OpCodes.Newobj, locationCtor),
    Instruction.Create(OpCodes.Stobj, location)
};
foreach (var instruction in newInstructions)
    il.InsertBefore(anchor, instruction);

il.Replace(target.LengthInstruction, Instruction.Create(OpCodes.Ldc_I4_8));

var beforeMvid = assembly.MainModule.Mvid;
var beforeMethodCount = ui.Methods.Count;
assembly.Write(output, new WriterParameters { WriteSymbols = false });
var outputHash = Hash(output);
var sourceHashAfterWrite = Hash(input);

using var reopened = AssemblyDefinition.ReadAssembly(output, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});
var reopenedUi = reopened.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var reopenedCctor = reopenedUi.Methods.Single(m => m.Name == ".cctor" && m.IsStatic && m.Parameters.Count == 0 && m.HasBody);
var verified = InspectPauseArray(reopenedCctor, reopenedUi);
var branchAndEhValid = ValidateControlFlow(reopened.MainModule);
var pass = inputHash == sourceHashAfterWrite &&
    reopened.MainModule.Mvid == beforeMvid &&
    reopenedUi.Methods.Count == beforeMethodCount &&
    verified.Length == 8 &&
    verified.Coordinates.SequenceEqual(existing.Select(c => new Coordinate(c.Index, c.X, c.Y, null)).Append(new Coordinate(7, newX, newY, null))) &&
    branchAndEhValid;

var report = new
{
    status = pass ? "pause-coordinate-copy-patch-pass" : "pause-coordinate-copy-patch-fail",
    mode = "separate-copy-only",
    source = new { file = Path.GetFileName(input), sha256Before = inputHash, sha256After = sourceHashAfterWrite },
    output = new { file = Path.GetFileName(output), sha256 = outputHash },
    assembly = new { name = reopened.Name.FullName, module = reopened.MainModule.Name, mvid = reopened.MainModule.Mvid },
    change = new
    {
        field = "Terraria.UI::MENU_PAUSE_COORDS",
        previousLength = 7,
        newLength = verified.Length,
        addedSlot = new { index = 7, x = newX, y = newY },
        existingCoordinatesPreserved = verified.Coordinates.Take(7).SequenceEqual(existing.Select(c => new Coordinate(c.Index, c.X, c.Y, null)))
    },
    validation = new
    {
        sourceFileUnchanged = inputHash == sourceHashAfterWrite,
        separateOutputFile = !string.Equals(input, output, StringComparison.OrdinalIgnoreCase),
        moduleIdentityPreserved = reopened.MainModule.Mvid == beforeMvid,
        methodCountPreserved = reopenedUi.Methods.Count == beforeMethodCount,
        branchAndExceptionHandlerTargetsValid = branchAndEhValid,
        patchedAssemblyReopened = true
    },
    safety = new
    {
        originalDllOverwritten = false,
        xexProduced = false,
        runtimeXbox360TestPerformed = false,
        note = "Only the pause coordinate array initializer was extended. No menu height, labels, item counts, or selection logic were changed. Y=497 is a geometric placement estimate and is not runtime-validated."
    }
};
File.WriteAllText(output + ".patch-report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause coordinate copy patch: {report.status}");
Console.WriteLine($"Source unchanged: {report.validation.sourceFileUnchanged}");
Console.WriteLine($"New array length: {verified.Length}; added slot: (index 7, {newX}, {newY})");
Console.WriteLine($"Branch/EH targets valid: {branchAndEhValid}");
return pass ? 0 : 1;

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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

static bool TryStoredLocal(Instruction instruction, out VariableDefinition? local)
{
    local = instruction.OpCode.Code switch
    {
        Code.Stloc_0 => instruction.Offset >= 0 ? null : null,
        _ => null
    };
    if (instruction.OpCode.Code is Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3)
    {
        local = null;
        return false;
    }
    if (instruction.OpCode.Code is Code.Stloc or Code.Stloc_S && instruction.Operand is VariableDefinition variable)
    {
        local = variable;
        return true;
    }
    return false;
}

static bool TryLoadedLocal(Instruction instruction, out VariableDefinition? local)
{
    local = null;
    if (instruction.OpCode.Code is Code.Ldloc or Code.Ldloc_S && instruction.Operand is VariableDefinition variable)
    {
        local = variable;
        return true;
    }
    return false;
}

static PauseArrayInspection InspectPauseArray(MethodDefinition cctor, TypeDefinition ui)
{
    var instructions = cctor.Body.Instructions;
    var field = ui.Fields.Single(f => f.Name == "MENU_PAUSE_COORDS");
    var matches = new List<(int Length, VariableDefinition Local, Instruction Store)>();
    for (int i = 1; i < instructions.Count - 1; i++)
        if (instructions[i].OpCode.Code == Code.Newarr &&
            instructions[i].Operand is TypeReference t && t.FullName == "Terraria.Location" &&
            TryInt(instructions[i - 1], out var length) &&
            instructions[i + 1].OpCode.Code is Code.Stloc or Code.Stloc_S &&
            instructions[i + 1].Operand is VariableDefinition local)
        {
            var store = instructions.Skip(i + 2).FirstOrDefault(x => x.OpCode.Code == Code.Stsfld && x.Operand is FieldReference f && f.FullName == field.FullName);
            if (store is not null) matches.Add((length, local, store));
        }
    if (matches.Count != 1) throw new InvalidOperationException("Could not uniquely re-find pause coordinate array after writing.");
    var m = matches[0];
    var end = instructions.IndexOf(m.Store);
    var coords = new List<Coordinate>();
    for (int i = 0; i + 6 < end; i++)
        if (TryLoadedLocal(instructions[i], out var local) && local == m.Local &&
            TryInt(instructions[i + 1], out var index) &&
            instructions[i + 2].OpCode.Code == Code.Ldelema &&
            instructions[i + 2].Operand is TypeReference et && et.FullName == "Terraria.Location" &&
            TryInt(instructions[i + 3], out var x) && TryInt(instructions[i + 4], out var y) &&
            instructions[i + 5].OpCode.Code == Code.Newobj &&
            instructions[i + 5].Operand is MethodReference ctor && ctor.Name == ".ctor" &&
            instructions[i + 6].OpCode.Code == Code.Stobj)
            coords.Add(new Coordinate(index, x, y, instructions[i]));
    return new PauseArrayInspection(m.Length, coords.OrderBy(c => c.Index).ToArray());
}

static bool ValidateControlFlow(ModuleDefinition module)
{
    foreach (var type in Flatten(module.Types))
    foreach (var method in type.Methods.Where(m => m.HasBody))
    {
        var body = method.Body;
        var set = body.Instructions.ToHashSet();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is Instruction target && !set.Contains(target)) return false;
            if (instruction.Operand is Instruction[] targets && targets.Any(t => !set.Contains(t))) return false;
        }
        foreach (var eh in body.ExceptionHandlers)
            if ((eh.TryStart is not null && !set.Contains(eh.TryStart)) ||
                (eh.TryEnd is not null && !set.Contains(eh.TryEnd)) ||
                (eh.HandlerStart is not null && !set.Contains(eh.HandlerStart)) ||
                (eh.HandlerEnd is not null && !set.Contains(eh.HandlerEnd)) ||
                (eh.FilterStart is not null && !set.Contains(eh.FilterStart))) return false;
    }
    return true;
}

static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in Flatten(type.NestedTypes))
            yield return nested;
    }
}

record ArrayTarget(Instruction LengthInstruction, Instruction NewArray, VariableDefinition? Local, Instruction FieldStore);
record Coordinate(int Index, int X, int Y, Instruction? Start);
record PauseArrayInspection(int Length, Coordinate[] Coordinates);
