using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseMenuPatchPlanner <input.dll> <plan.json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Input and plan output must be different files.");

var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");

var cctor = UniqueMethod(".cctor", 0);
var setMenu = UniqueMethod("SetMenu", 3);
var updateMenu = UniqueMethod("UpdateMenu", 0);
var drawMenu = UniqueMethod("DrawMenu", 0);
var updateMouse = UniqueMethod("UpdateMouse", 0);

var pauseArrayAllocations = FindPauseArrayAllocations(cctor);
var pauseSetMenuAnchors = FindPauseSetMenuAnchors(setMenu);
var onlineItemCountAnchors = FindFieldStores(updateMenu, "numMenuItems", 7);
var offlineItemCountAnchors = FindFieldStores(updateMenu, "numMenuItems", 6);
var pauseCoordinateIndex6 = FindCoordinateIndexWrites(updateMenu, 6);
var drawMenuCountRefs = CountFieldReferences(drawMenu, "numMenuItems");
var mouseHeightRefs = CountFieldReferences(updateMouse, "uiHeight");
var mouseCoordsRefs = CountFieldReferences(updateMouse, "uiCoords");

var checks = new[]
{
    Check("pause-array-allocation-is-unique", pauseArrayAllocations.Count == 1, pauseArrayAllocations),
    Check("pause-array-has-seven-initialized-coordinates", pauseArrayAllocations.Count == 1 && pauseArrayAllocations[0].InitializedIndexes.SequenceEqual(Enumerable.Range(0, 7)), pauseArrayAllocations),
    Check("pause-setmenu-branch-is-unique", pauseSetMenuAnchors.Count == 1, pauseSetMenuAnchors),
    Check("online-pause-item-count-seven", onlineItemCountAnchors.Count == 1, onlineItemCountAnchors),
    Check("offline-pause-item-count-six", offlineItemCountAnchors.Count == 1, offlineItemCountAnchors),
    Check("pause-index-six-has-online-and-offline-visibility-writes", pauseCoordinateIndex6.Count >= 2, pauseCoordinateIndex6),
    Check("draw-menu-uses-numMenuItems", drawMenuCountRefs > 0, drawMenuCountRefs),
    Check("mouse-navigation-uses-uiHeight-and-uiCoords", mouseHeightRefs > 0 && mouseCoordsRefs > 0, new { mouseHeightRefs, mouseCoordsRefs })
};

var allPassed = checks.All(c => c.Passed);
var plan = new
{
    schemaVersion = 1,
    status = allPassed ? "dry-run-plan-ready" : "dry-run-plan-blocked",
    mode = "read-only-dry-run",
    source = new { file = Path.GetFileName(input), sha256 = sourceHash, assembly = assembly.Name.FullName, module = assembly.MainModule.Name, mvid = assembly.MainModule.Mvid },
    output = new { file = Path.GetFileName(output), writesPatchedAssembly = false },
    checks,
    proposedStages = new[]
    {
        new { id = 1, target = "Terraria.UI::.cctor", action = "Extend only MENU_PAUSE_COORDS allocation and initializer with a new slot.", status = "planned-not-applied" },
        new { id = 2, target = "Terraria.UI::SetMenu(MenuMode, Boolean, Boolean)", action = "Change uiHeight only in the uniquely matched pause branch.", status = "planned-not-applied" },
        new { id = 3, target = "Terraria.UI::UpdateMenu()", action = "Extend online and offline pause item counts, coordinate visibility, and label assignment independently.", status = "planned-not-applied" },
        new { id = 4, target = "Terraria.UI::DrawMenu() and UpdateMouse()", action = "Verify render and navigation bounds after planned edits; do not alter unrelated menu modes.", status = "planned-not-applied" },
        new { id = 5, target = "Terraria.UI::UpdateMenu()", action = "Add a new selection branch without changing the existing Exit selection branch.", status = "planned-not-applied" },
        new { id = 6, target = "Managed assembly validation", action = "Write only to a separate output copy, reopen with Cecil, verify branch/EH targets, compare metadata, and run independent stack-depth audit.", status = "planned-not-applied" }
    },
    safety = new
    {
        originalAssemblyNotModified = true,
        noPatchedDllProduced = true,
        noXexProduced = true,
        runtimeXbox360TestPerformed = false,
        note = "A successful plan is not proof that the future IL patch is semantically valid or runs on hardware."
    }
};

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause-menu patch planner: {plan.status}");
Console.WriteLine($"Source SHA-256: {sourceHash}");
foreach (var check in checks)
    Console.WriteLine($"{(check.Passed ? "PASS" : "BLOCK")} {check.Name}");
return allPassed ? 0 : 1;

MethodDefinition UniqueMethod(string name, int parameterCount)
{
    var matches = ui.Methods.Where(m => m.Name == name && m.Parameters.Count == parameterCount).ToArray();
    if (matches.Length != 1 || !matches[0].HasBody)
        throw new InvalidOperationException($"Expected exactly one body for {name}/{parameterCount}; found {matches.Length}.");
    return matches[0];
}

static CheckResult Check(string name, bool passed, object evidence) => new(name, passed, evidence);

static List<ArrayAllocation> FindPauseArrayAllocations(MethodDefinition method)
{
    var result = new List<ArrayAllocation>();
    var ins = method.Body.Instructions;
    for (int i = 1; i < ins.Count - 1; i++)
    {
        if (ins[i].OpCode.Code != Code.Newarr || ins[i].Operand is not TypeReference t || t.FullName != "Terraria.Location")
            continue;
        if (!TryInt(ins[i - 1], out var length) || length != 7)
            continue;
        var indexes = new List<int>();
        for (int j = i + 1; j < ins.Count - 2; j++)
        {
            if (ins[j].OpCode.Code == Code.Ldelema && ins[j].Operand is TypeReference et && et.FullName == "Terraria.Location" &&
                TryInt(ins[j - 1], out var index))
                indexes.Add(index);
            if (ins[j].OpCode.Code == Code.Stsfld && ins[j].Operand is FieldReference f)
            {
                if (f.Name == "MENU_PAUSE_COORDS")
                    result.Add(new ArrayAllocation(i, length, indexes.Distinct().Order().ToArray()));
                break;
            }
        }
    }
    return result;
}

static List<object> FindPauseSetMenuAnchors(MethodDefinition method)
{
    var result = new List<object>();
    var ins = method.Body.Instructions;
    for (int i = 2; i + 4 < ins.Count; i++)
    {
        if (ins[i].OpCode.Code == Code.Stfld && IsField(ins[i], "uiHeight") &&
            TryInt(ins[i - 1], out var height) && height == 7 &&
            ins[i + 1].OpCode.Code == Code.Ldarg_0 &&
            ins[i + 2].OpCode.Code == Code.Ldsfld && IsField(ins[i + 2], "MENU_PAUSE_COORDS") &&
            ins[i + 3].OpCode.Code == Code.Stfld && IsField(ins[i + 3], "uiCoords"))
            result.Add(new { instruction = i - 1, height, coordinateField = "MENU_PAUSE_COORDS" });
    }
    return result;
}

static List<object> FindFieldStores(MethodDefinition method, string fieldName, int value)
{
    var ins = method.Body.Instructions;
    var result = new List<object>();
    for (int i = 1; i < ins.Count; i++)
        if (ins[i].OpCode.Code == Code.Stfld && IsField(ins[i], fieldName) && TryInt(ins[i - 1], out var n) && n == value)
            result.Add(new { instruction = i - 1, value });
    return result;
}

static List<object> FindCoordinateIndexWrites(MethodDefinition method, int index)
{
    var ins = method.Body.Instructions;
    var result = new List<object>();
    for (int i = 2; i + 2 < ins.Count; i++)
    {
        if (ins[i].OpCode.Code != Code.Ldelema || ins[i].Operand is not TypeReference t || t.FullName != "Terraria.Location" ||
            !TryInt(ins[i - 1], out var found) || found != index)
            continue;
        var fieldStore = ins.Skip(i + 1).Take(4).FirstOrDefault(x => x.OpCode.Code == Code.Stfld && HasFieldName(x, "X"));
        if (fieldStore is not null)
            result.Add(new { instruction = i - 1, xFieldStoreInstruction = fieldStore.Offset });
    }
    return result;
}

static int CountFieldReferences(MethodDefinition method, string fieldName) =>
    method.Body.Instructions.Count(i => i.Operand is FieldReference f && f.Name == fieldName);

static bool IsField(Instruction instruction, string name) =>
    instruction.Operand is FieldReference f && f.Name == name && f.DeclaringType.FullName == "Terraria.UI";

static bool HasFieldName(Instruction instruction, string name) =>
    instruction.Operand is FieldReference f && f.Name == name;

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

record CheckResult(string Name, bool Passed, object Evidence);
record ArrayAllocation(int Instruction, int DeclaredLength, int[] InitializedIndexes);
