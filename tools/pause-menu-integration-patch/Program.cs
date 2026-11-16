using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseMenuIntegrationPatch <pausecoords.dll> <separate-output.dll>");
    return 2;
}
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (!File.Exists(input) || string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Input must exist and output must be a different file.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var sourceHash = Hash(input);

using var netStub = Stub("Microsoft.Xna.Framework.Net", "Microsoft.Xna.Framework.Net", "SendDataOptions");
using var coreStub = Stub("Microsoft.Xna.Framework", "Microsoft.Xna.Framework.Input", "Buttons");
using var resolver = new Resolver(new Dictionary<string, AssemblyDefinition>
{
    ["Microsoft.Xna.Framework.Net"] = netStub,
    ["Microsoft.Xna.Framework"] = coreStub
});
var reader = new ReaderParameters { InMemory = true, ReadSymbols = false, ReadingMode = ReadingMode.Immediate, AssemblyResolver = resolver };
using var assembly = AssemblyDefinition.ReadAssembly(input, reader);
if (assembly.Name.Name != "game" || assembly.MainModule.Name != "game.exe")
    throw new InvalidOperationException("Unexpected Terraria assembly identity.");

var module = assembly.MainModule;
var ui = module.Types.Single(t => t.FullName == "Terraria.UI");
var main = module.Types.Single(t => t.FullName == "Terraria.Main");
var setMenu = ui.Methods.Single(m => m.Name == "SetMenu" && m.Parameters.Count == 3 &&
    m.Parameters[0].ParameterType.FullName == "Terraria.MenuMode" &&
    m.Parameters[1].ParameterType.MetadataType == MetadataType.Boolean &&
    m.Parameters[2].ParameterType.MetadataType == MetadataType.Boolean);
var prevMenu = ui.Methods.Single(m => m.Name == "PrevMenu" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.MetadataType == MetadataType.Int32);
var back = ui.Methods.Single(m => m.Name == "IsBackButtonTriggered" && m.Parameters.Count == 0);
var playSound = main.Methods.Single(m => m.Name == "PlaySound" && m.Parameters.Count == 1 &&
    m.Parameters[0].ParameterType.MetadataType == MetadataType.Int32);
var statusText = ui.Fields.Single(f => f.Name == "statusText" && f.FieldType.MetadataType == MetadataType.String);
var menuModeType = module.Types.Single(t => t.FullName == "Terraria.MenuMode");
var statusModeValue = Convert.ToInt32(menuModeType.Fields.Single(f => f.Name == "STATUS_SCREEN").Constant);
var uiHeight = ui.Fields.Single(f => f.Name == "uiHeight");
var pauseCoords = ui.Fields.Single(f => f.Name == "MENU_PAUSE_COORDS");

var setMenuBody = ui.Methods.Single(m => m.Name == "SetMenu" && m.Parameters.Count == 3 && m.HasBody);
var pauseHeight = FindUnique(setMenuBody, i =>
    Is(i, Code.Ldarg_0) && Is(i.Next, Code.Ldc_I4_7) &&
    Is(i.Next?.Next, Code.Stfld) && Ref(i.Next?.Next, uiHeight) &&
    Is(i.Next?.Next?.Next, Code.Ldarg_0) &&
    Is(i.Next?.Next?.Next?.Next, Code.Ldsfld) && Ref(i.Next?.Next?.Next?.Next, pauseCoords));
pauseHeight.Next!.OpCode = OpCodes.Ldc_I4_8;

var update = ui.Methods.Single(m => m.Name == "UpdateMenu" && m.HasBody);
var body = update.Body;
var il = body.GetILProcessor();

// Change the Trial count (7 -> 8) and Full-game count (6 -> 7), each immediately before its pause-coordinate visibility setup.
var trialCount = FindUnique(update, i => Is(i, Code.Ldarg_0) && Is(i.Next, Code.Ldc_I4_7) &&
    Is(i.Next?.Next, Code.Stfld) && RefName(i.Next?.Next, "numMenuItems") &&
    Is(i.Next?.Next?.Next, Code.Ldsfld) && Ref(i.Next?.Next?.Next, pauseCoords));
trialCount.Next!.OpCode = OpCodes.Ldc_I4_8;

var fullCount = FindUnique(update, i => Is(i, Code.Ldarg_0) && Is(i.Next, Code.Ldc_I4_6) &&
    Is(i.Next?.Next, Code.Stfld) && RefName(i.Next?.Next, "numMenuItems") &&
    Is(i.Next?.Next?.Next, Code.Ldsfld) && Ref(i.Next?.Next?.Next, pauseCoords));
fullCount.Next!.OpCode = OpCodes.Ldc_I4_7;

// The full-game branch previously hid slot 6 because it was the unused seventh coordinate.
// Slot 6 becomes the new item in the full game.
var hiddenSlot6 = FindUnique(update, i => Is(i, Code.Ldsfld) && Ref(i, pauseCoords) &&
    Is(i.Next, Code.Ldc_I4_6) && Is(i.Next?.Next, Code.Ldelema) &&
    Is(i.Next?.Next?.Next, Code.Ldc_I4_0) &&
    Is(i.Next?.Next?.Next?.Next, Code.Stfld) && RefName(i.Next?.Next?.Next?.Next, "X"));
il.Replace(hiddenSlot6.Next!.Next!.Next!, Instruction.Create(OpCodes.Ldc_I4, 480));

// Insert menuString[loc9 + 5] = "T360 ModLoader" after the five existing pause labels.
var labelAnchor = FindUnique(update, i => Is(i, Code.Ldsfld) && RefName(i, "menuString") == false &&
    i.Operand is FieldReference f && f.DeclaringType.FullName == "Terraria.Lang" && f.Name == "menu" &&
    Is(i.Next, Code.Ldc_I4_S) && IntValue(i.Next!) == 101 &&
    Is(i.Next?.Next, Code.Ldelem_Ref) && Is(i.Next?.Next?.Next, Code.Stelem_Ref) &&
    Is(i.Next?.Next?.Next?.Next, Code.Ldc_I4_0) && Is(i.Next?.Next?.Next?.Next?.Next, Code.Stloc_S));
var loc9 = labelAnchor.Previous?.Previous?.Previous;
if (loc9 is null || !IsLocalLoad(loc9, 9))
    throw new InvalidOperationException("Could not identify pause-menu base local for label insertion.");
var labelCode = new[]
{
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldfld, ui.Fields.Single(f => f.Name == "menuString")),
    CloneLocalLoad(loc9),
    Instruction.Create(OpCodes.Ldc_I4_5),
    Instruction.Create(OpCodes.Add),
    Instruction.Create(OpCodes.Ldstr, "T360 ModLoader"),
    Instruction.Create(OpCodes.Stelem_Ref)
};
var labelInsertionAnchor = labelAnchor.Next!.Next!.Next!.Next!;
foreach (var ins in labelCode) il.InsertBefore(labelInsertionAnchor, ins);

// Add the T360 selection branch before the original Exit test. Exit remains byte-for-byte in its original logical path.
var exitTest = FindUnique(update, i => Is(i, Code.Ldarg_0) && Is(i.Next, Code.Ldfld) &&
    RefName(i.Next, "selectedMenu") && IsLocalLoad(i.Next?.Next, 9) &&
    Is(i.Next?.Next?.Next, Code.Ldc_I4_4) && Is(i.Next?.Next?.Next?.Next, Code.Add) &&
    Is(i.Next?.Next?.Next?.Next?.Next, Code.Bne_Un));
var exitFallback = (Instruction)exitTest.Next!.Next!.Next!.Next!.Next!.Operand!;
var t360 = new[]
{
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldfld, ui.Fields.Single(f => f.Name == "selectedMenu")),
    CloneLocalLoad(exitTest.Next!.Next!),
    Instruction.Create(OpCodes.Ldc_I4_5),
    Instruction.Create(OpCodes.Add),
    Instruction.Create(OpCodes.Bne_Un, exitTest),
    Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)10),
    Instruction.Create(OpCodes.Call, playSound),
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldc_I4, statusModeValue), // MenuMode.STATUS_SCREEN
    Instruction.Create(OpCodes.Ldc_I4_1),
    Instruction.Create(OpCodes.Ldc_I4_0),
    Instruction.Create(OpCodes.Call, setMenu),
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldstr, "T360 ModLoader\nGerenciador carregado.\nB: Voltar"),
    Instruction.Create(OpCodes.Stfld, statusText),
    Instruction.Create(OpCodes.Br, exitFallback)
};
foreach (var ins in t360) il.InsertBefore(exitTest, ins);

// STATUS_SCREEN becomes a usable page: B returns to the pause menu.
var statusTextLoad = update.Body.Instructions.First(i => Is(i, Code.Ldfld) && Ref(i, statusText) &&
    Is(i.Previous, Code.Ldarg_0) && Is(i.Next, Code.Stelem_Ref) &&
    Is(i.Previous?.Previous, Code.Ldc_I4_0));
var tipsUpdate = FindFirstAfter(statusTextLoad, i => Is(i, Code.Callvirt) &&
    i.Operand is MethodReference mr && mr.DeclaringType.FullName == "Terraria.TextSequenceBlock" && mr.Name == "Update");
var skipBack = Instruction.Create(OpCodes.Nop);
var backCode = new[]
{
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Call, back),
    Instruction.Create(OpCodes.Brfalse_S, skipBack),
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldc_I4_M1),
    Instruction.Create(OpCodes.Call, prevMenu),
    skipBack
};
var insertion = tipsUpdate;
foreach (var ins in backCode) { il.InsertAfter(insertion, ins); insertion = ins; }

body.MaxStackSize = Math.Max(body.MaxStackSize, 4);
var beforeMvid = module.Mvid;
assembly.Write(output);
var unchanged = sourceHash == Hash(input);
using var reopened = AssemblyDefinition.ReadAssembly(output, reader);
var reopenedUi = reopened.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var reopenedUpdate = reopenedUi.Methods.Single(m => m.Name == "UpdateMenu");
var controlFlowValid = Validate(reopened.MainModule);
var pass = unchanged && reopened.MainModule.Mvid == beforeMvid && controlFlowValid &&
    reopenedUi.Methods.Single(m => m.Name == "SetMenu" && m.Parameters.Count == 3).Body.Instructions.Any(i => Is(i, Code.Ldc_I4_8)) &&
    reopenedUpdate.Body.Instructions.Any(i => Is(i, Code.Ldstr) && Equals(i.Operand, "T360 ModLoader\nGerenciador carregado.\nB: Voltar"));
var report = new
{
    status = pass ? "pause-menu-integration-copy-patch-pass" : "pause-menu-integration-copy-patch-fail",
    mode = "separate-copy-only",
    source = new { file = Path.GetFileName(input), sha256 = sourceHash, unchanged },
    output = new { file = Path.GetFileName(output), sha256 = Hash(output) },
    changes = new[] { "pause uiHeight 7->8", "Trial pause item count 7->8", "Full-game pause item count 6->7", "full-game coordinate slot 6 enabled", "T360 ModLoader label at loc9+5", "selection opens STATUS_SCREEN page", "B returns through PrevMenu; original Exit branch retained" },
    validation = new { controlFlowTargetsValid = controlFlowValid, patchedAssemblyReopened = true, moduleIdentityPreserved = reopened.MainModule.Mvid == beforeMvid },
    safety = new { originalDllOverwritten = false, gameXexProducedByThisPatchStep = false, xboxRuntimeTested = false, coordinateY497RuntimeValidated = false }
};
File.WriteAllText(output + ".patch-report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"T360 pause menu patch: {report.status}");
Console.WriteLine($"Source unchanged: {unchanged}; control-flow targets valid: {controlFlowValid}");
return pass ? 0 : 1;

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static bool Is(Instruction? i, Code c) => i?.OpCode.Code == c;
static bool Ref(Instruction? i, FieldDefinition f) => i?.Operand is FieldReference r && r.Name == f.Name && r.DeclaringType.FullName == f.DeclaringType.FullName;
static bool RefName(Instruction? i, string name) => i?.Operand is FieldReference r && r.Name == name;
static int IntValue(Instruction i) => i.OpCode.Code switch { Code.Ldc_I4_S when i.Operand is sbyte n => n, Code.Ldc_I4 when i.Operand is int n => n, _ => int.MinValue };
static bool IsLocalLoad(Instruction? i, int n) => i is not null && (i.OpCode.Code switch
{
    Code.Ldloc_0 => n == 0, Code.Ldloc_1 => n == 1, Code.Ldloc_2 => n == 2, Code.Ldloc_3 => n == 3,
    Code.Ldloc or Code.Ldloc_S => i.Operand is VariableDefinition v && v.Index == n, _ => false
});
static Instruction CloneLocalLoad(Instruction source) => source.OpCode.Code switch
{
    Code.Ldloc_0 => Instruction.Create(OpCodes.Ldloc_0), Code.Ldloc_1 => Instruction.Create(OpCodes.Ldloc_1),
    Code.Ldloc_2 => Instruction.Create(OpCodes.Ldloc_2), Code.Ldloc_3 => Instruction.Create(OpCodes.Ldloc_3),
    Code.Ldloc or Code.Ldloc_S when source.Operand is VariableDefinition v => Instruction.Create(OpCodes.Ldloc, v),
    _ => throw new InvalidOperationException("Unsupported local load form.")
};
static Instruction FindUnique(MethodDefinition m, Func<Instruction, bool> predicate)
{
    var hits = m.Body.Instructions.Where(predicate).ToArray();
    if (hits.Length != 1) throw new InvalidOperationException($"Expected one IL anchor in {m.FullName}; found {hits.Length}.");
    return hits[0];
}
static Instruction FindFirstAfter(Instruction start, Func<Instruction, bool> predicate)
{
    return start.Next?.GetSelfAndFollowing().FirstOrDefault(predicate)
        ?? throw new InvalidOperationException("Could not find following IL anchor.");
}
static bool Validate(ModuleDefinition module)
{
    foreach (var type in Flatten(module.Types))
    foreach (var method in type.Methods.Where(m => m.HasBody))
    {
        var set = method.Body.Instructions.ToHashSet();
        foreach (var i in method.Body.Instructions)
        {
            if (i.Operand is Instruction target && !set.Contains(target)) return false;
            if (i.Operand is Instruction[] targets && targets.Any(t => !set.Contains(t))) return false;
        }
        foreach (var h in method.Body.ExceptionHandlers)
            if ((h.TryStart != null && !set.Contains(h.TryStart)) || (h.TryEnd != null && !set.Contains(h.TryEnd)) ||
                (h.HandlerStart != null && !set.Contains(h.HandlerStart)) || (h.HandlerEnd != null && !set.Contains(h.HandlerEnd)) ||
                (h.FilterStart != null && !set.Contains(h.FilterStart))) return false;
    }
    return true;
}
static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
{
    foreach (var t in types) { yield return t; foreach (var n in Flatten(t.NestedTypes)) yield return n; }
}
static TypeDefinition EnumStub(ModuleDefinition module, string ns, string name)
{
    var core = new AssemblyNameReference("mscorlib", new Version(2,0,5,0)) { PublicKeyToken = Convert.FromHexString("7cec85d7bea7798e") };
    var e = new TypeDefinition(ns,name,TypeAttributes.Public|TypeAttributes.Sealed|TypeAttributes.AnsiClass|TypeAttributes.BeforeFieldInit,new TypeReference("System","Enum",module,core));
    e.Fields.Add(new FieldDefinition("value__",FieldAttributes.Public|FieldAttributes.SpecialName|FieldAttributes.RTSpecialName,module.TypeSystem.Int32));
    return e;
}
static AssemblyDefinition Stub(string name,string ns,string type)
{
    var an = new AssemblyNameDefinition(name,new Version(4,0,0,0)) { PublicKeyToken = Convert.FromHexString("842cf8be1de50553") };
    var a = AssemblyDefinition.CreateAssembly(an,name,ModuleKind.Dll); a.MainModule.Types.Add(EnumStub(a.MainModule,ns,type)); return a;
}
sealed class Resolver(IReadOnlyDictionary<string,AssemblyDefinition> stubs) : DefaultAssemblyResolver
{
    public override AssemblyDefinition Resolve(AssemblyNameReference name) => stubs.TryGetValue(name.Name,out var a) ? a : base.Resolve(name);
    public override AssemblyDefinition Resolve(AssemblyNameReference name,ReaderParameters parameters) => stubs.TryGetValue(name.Name,out var a) ? a : base.Resolve(name,parameters);
}
static class InstructionExtensions
{
    public static IEnumerable<Instruction> GetSelfAndFollowing(this Instruction i) { for (var p=i;p!=null;p=p.Next) yield return p; }
}
