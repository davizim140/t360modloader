using Mono.Cecil;
using Mono.Cecil.Cil;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static MethodDefinition FindFixtureMethod(AssemblyDefinition assembly)
{
    var type = assembly.MainModule.Types.Single(t => t.FullName == "T360Fixture.BranchFixture");
    return type.Methods.Single(m => m.Name == "Choose");
}

static void ValidateMethod(MethodDefinition method)
{
    Require(method.HasBody, "Fixture method has no body.");
    var instructions = method.Body.Instructions;
    Require(instructions.Count >= 10, "Fixture method instruction body is unexpectedly short.");
    Require(method.Body.ExceptionHandlers.Count == 1, "Expected exactly one exception handler.");
    Require(method.Body.ExceptionHandlers[0].HandlerType == ExceptionHandlerType.Catch,
        "Expected a catch exception handler.");

    foreach (var instruction in instructions)
    {
        if (instruction.Operand is Instruction target)
            Require(instructions.Contains(target), "Branch target is outside the method body.");
        if (instruction.Operand is Instruction[] targets)
            foreach (var branchTarget in targets)
                Require(instructions.Contains(branchTarget), "Switch target is outside the method body.");
    }

    var handler = method.Body.ExceptionHandlers[0];
    Require(handler.TryStart is not null && instructions.Contains(handler.TryStart), "Invalid try start.");
    Require(handler.TryEnd is not null && instructions.Contains(handler.TryEnd), "Invalid try end.");
    Require(handler.HandlerStart is not null && instructions.Contains(handler.HandlerStart), "Invalid handler start.");
    Require(handler.HandlerEnd is not null && instructions.Contains(handler.HandlerEnd), "Invalid handler end.");
    Require(instructions.Any(i => i.OpCode.Code == Code.Brfalse_S), "Conditional branch was not preserved.");
    Require(instructions.Any(i => i.OpCode.Code == Code.Leave_S || i.OpCode.Code == Code.Leave),
        "Exception-safe leave branch was not preserved.");
}

var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "managed-patcher-smoke");
Directory.CreateDirectory(root);
var fixturePath = Path.Combine(root, "T360.PatcherFixture.dll");
var roundTripPath = Path.Combine(root, "T360.PatcherFixture.roundtrip.dll");

var assembly = AssemblyDefinition.CreateAssembly(
    new AssemblyNameDefinition("T360.PatcherFixture", new Version(1, 0, 0, 0)),
    "T360.PatcherFixture",
    ModuleKind.Dll);

var module = assembly.MainModule;
var type = new TypeDefinition(
    "T360Fixture",
    "BranchFixture",
    TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
    module.TypeSystem.Object);
module.Types.Add(type);

var method = new MethodDefinition(
    "Choose",
    MethodAttributes.Public | MethodAttributes.Static,
    module.TypeSystem.Int32);
method.Parameters.Add(new ParameterDefinition("useFirstPath", ParameterAttributes.None, module.TypeSystem.Boolean));
method.Body.InitLocals = true;
method.Body.MaxStackSize = 2;
var result = new VariableDefinition(module.TypeSystem.Int32);
method.Body.Variables.Add(result);
type.Methods.Add(method);

var il = method.Body.GetILProcessor();
var tryStart = Instruction.Create(OpCodes.Ldarg_0);
var fallback = Instruction.Create(OpCodes.Ldc_I4_2);
var catchStart = Instruction.Create(OpCodes.Pop);
var returnTarget = Instruction.Create(OpCodes.Ldloc, result);

il.Append(tryStart);
il.Append(Instruction.Create(OpCodes.Brfalse_S, fallback));
il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
il.Append(Instruction.Create(OpCodes.Stloc, result));
il.Append(Instruction.Create(OpCodes.Leave_S, returnTarget));
il.Append(fallback);
il.Append(Instruction.Create(OpCodes.Stloc, result));
il.Append(Instruction.Create(OpCodes.Leave_S, returnTarget));
il.Append(catchStart);
il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
il.Append(Instruction.Create(OpCodes.Stloc, result));
il.Append(Instruction.Create(OpCodes.Leave_S, returnTarget));
il.Append(returnTarget);
il.Append(Instruction.Create(OpCodes.Ret));

method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
{
    CatchType = module.ImportReference(typeof(Exception)),
    TryStart = tryStart,
    TryEnd = catchStart,
    HandlerStart = catchStart,
    HandlerEnd = returnTarget
});

assembly.Write(fixturePath);
assembly.Dispose();

using (var fixture = AssemblyDefinition.ReadAssembly(fixturePath, new ReaderParameters { InMemory = true }))
    ValidateMethod(FindFixtureMethod(fixture));

using (var fixture = AssemblyDefinition.ReadAssembly(fixturePath, new ReaderParameters { InMemory = true }))
    fixture.Write(roundTripPath);

using (var roundTrip = AssemblyDefinition.ReadAssembly(roundTripPath, new ReaderParameters { InMemory = true }))
    ValidateMethod(FindFixtureMethod(roundTrip));

Console.WriteLine("Mono.Cecil managed IL writer smoke test: PASS");
Console.WriteLine("Fixture: synthetic .NET assembly only");
Console.WriteLine("Verified: conditional branch targets, leave branches, catch region, reopen, and round-trip write");
Console.WriteLine("Terraria.Managed.dll was not opened or modified by this smoke test.");
