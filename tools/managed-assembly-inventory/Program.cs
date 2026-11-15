using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes)
        foreach (var child in Flatten(nested))
            yield return child;
}

static bool BranchTargetsValid(MethodDefinition method)
{
    var body = method.Body;
    var instructions = body.Instructions.ToHashSet();
    foreach (var instruction in body.Instructions)
    {
        if (instruction.Operand is Instruction target && !instructions.Contains(target))
            return false;
        if (instruction.Operand is Instruction[] targets && targets.Any(target => !instructions.Contains(target)))
            return false;
    }

    foreach (var handler in body.ExceptionHandlers)
    {
        if (handler.TryStart is not null && !instructions.Contains(handler.TryStart)) return false;
        if (handler.TryEnd is not null && !instructions.Contains(handler.TryEnd)) return false;
        if (handler.HandlerStart is not null && !instructions.Contains(handler.HandlerStart)) return false;
        if (handler.HandlerEnd is not null && !instructions.Contains(handler.HandlerEnd)) return false;
        if (handler.FilterStart is not null && !instructions.Contains(handler.FilterStart)) return false;
    }
    return true;
}

if (args.Length != 3 || args[1] != "--output")
{
    Console.Error.WriteLine("Usage: ManagedAssemblyInventory <assembly.dll> --output <report.json>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[2]);
if (!File.Exists(input))
{
    Console.Error.WriteLine("Input assembly not found: " + input);
    return 2;
}

var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
var reader = new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
};

using var assembly = AssemblyDefinition.ReadAssembly(input, reader);
var module = assembly.MainModule;
var allTypes = module.Types.SelectMany(Flatten).ToList();
var allMethods = allTypes.SelectMany(type => type.Methods).ToList();
var uiType = allTypes.SingleOrDefault(type => type.FullName == "Terraria.UI");
if (uiType is null)
{
    Console.Error.WriteLine("Terraria.UI type was not found.");
    return 1;
}

var watchedNames = new[] { "SetMenu", "UpdateMenu", "UpdateMouse", "DrawMenu", "Draw" };
var watchedMethods = uiType.Methods
    .Where(method => watchedNames.Contains(method.Name))
    .Select(method => new
    {
        name = method.Name,
        signature = method.FullName,
        hasBody = method.HasBody,
        instructionCount = method.HasBody ? method.Body.Instructions.Count : 0,
        exceptionHandlerCount = method.HasBody ? method.Body.ExceptionHandlers.Count : 0,
        branchTargetsValid = method.HasBody && BranchTargetsValid(method),
        branchInstructionCount = method.HasBody
            ? method.Body.Instructions.Count(instruction =>
                instruction.OpCode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget or OperandType.InlineSwitch)
            : 0
    })
    .ToList();

var methodsWithBody = allMethods.Where(method => method.HasBody).ToList();
var invalidBodies = methodsWithBody
    .Where(method => !BranchTargetsValid(method))
    .Select(method => method.FullName)
    .ToList();

var externalConstants = allTypes.SelectMany(type => type.Methods)
    .SelectMany(method => method.Parameters.Select(parameter => new
    {
        owner = method.FullName,
        name = parameter.Name,
        type = parameter.ParameterType.FullName,
        scope = parameter.ParameterType.Scope?.Name ?? "",
        hasConstant = parameter.HasConstant,
        constant = parameter.HasConstant ? Convert.ToString(parameter.Constant, System.Globalization.CultureInfo.InvariantCulture) : null
    }))
    .Where(parameter => parameter.hasConstant && parameter.scope.Length > 0)
    .ToArray();

var report = new
{
    status = "read-only-cecil-inventory",
    input = Path.GetFileName(input),
    inputSha256 = originalHash,
    assemblyName = assembly.Name.FullName,
    moduleName = module.Name,
    moduleMvid = module.Mvid,
    runtimeVersion = module.RuntimeVersion,
    architecture = module.Architecture.ToString(),
    kind = module.Kind.ToString(),
    typeCount = allTypes.Count,
    methodCount = allMethods.Count,
    methodBodyCount = methodsWithBody.Count,
    assemblyReferences = module.AssemblyReferences.Select(reference => reference.FullName).ToArray(),
    watchedMethods,
    invalidMethodBodies = invalidBodies,
    sourceWasOpenedReadOnly = true,
    assemblyWasWritten = false
};

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Mono.Cecil read-only Terraria assembly inventory: PASS");
Console.WriteLine($"Types: {allTypes.Count}; methods: {allMethods.Count}; bodies: {methodsWithBody.Count}");
Console.WriteLine($"Terraria.UI watched methods: {watchedMethods.Count}");
Console.WriteLine($"Invalid branch/EH bodies: {invalidBodies.Count}");
Console.WriteLine("Assembly was read only; no write or rebuild was performed.");
return invalidBodies.Count == 0 ? 0 : 1;
