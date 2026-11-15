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

static int IndexOf(Instruction? instruction, Dictionary<Instruction, int> indexes)
    => instruction is null ? -1 : indexes.TryGetValue(instruction, out var index) ? index : -2;

static string OperandSignature(Instruction instruction, Dictionary<Instruction, int> indexes)
{
    return instruction.Operand switch
    {
        null => "",
        Instruction target => "target:" + IndexOf(target, indexes),
        Instruction[] targets => "targets:" + string.Join(",", targets.Select(target => IndexOf(target, indexes))),
        VariableDefinition variable => "var:" + variable.Index + ":" + variable.VariableType.FullName,
        ParameterDefinition parameter => "arg:" + parameter.Index + ":" + parameter.ParameterType.FullName,
        MethodReference method => "method:" + method.FullName,
        FieldReference field => "field:" + field.FullName,
        TypeReference type => "type:" + type.FullName,
        CallSite callSite => "callsite:" + callSite.ReturnType.FullName + "(" + string.Join(",", callSite.Parameters.Select(p => p.ParameterType.FullName)) + ")",
        string value => "string:" + value,
        IFormattable value => value.GetType().FullName + ":" + value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => instruction.Operand.GetType().FullName + ":" + instruction.Operand
    };
}

static object MethodFingerprint(MethodDefinition method)
{
    if (!method.HasBody)
        return new { method.FullName, hasBody = false };

    var body = method.Body;
    var indexes = body.Instructions.Select((instruction, index) => (instruction, index))
        .ToDictionary(item => item.instruction, item => item.index);

    var instructions = body.Instructions.Select(instruction => new
    {
        opcode = instruction.OpCode.Code.ToString(),
        operand = OperandSignature(instruction, indexes)
    }).ToArray();

    var handlers = body.ExceptionHandlers.Select(handler => new
    {
        kind = handler.HandlerType.ToString(),
        tryStart = IndexOf(handler.TryStart, indexes),
        tryEnd = IndexOf(handler.TryEnd, indexes),
        handlerStart = IndexOf(handler.HandlerStart, indexes),
        handlerEnd = IndexOf(handler.HandlerEnd, indexes),
        filterStart = IndexOf(handler.FilterStart, indexes),
        catchType = handler.CatchType?.FullName ?? ""
    }).ToArray();

    return new
    {
        method.FullName,
        hasBody = true,
        body.InitLocals,
        body.MaxStackSize,
        variables = body.Variables.Select(v => v.VariableType.FullName).ToArray(),
        instructions,
        handlers
    };
}

static string Json(object value) => JsonSerializer.Serialize(value);

static object AssemblyFingerprint(AssemblyDefinition assembly)
{
    var module = assembly.MainModule;
    var types = module.Types.SelectMany(Flatten).OrderBy(t => t.FullName).ToList();
    var typeData = types.Select(type => new
    {
        type.FullName,
        type.Attributes,
        baseType = type.BaseType?.FullName ?? "",
        interfaces = type.Interfaces.Select(i => i.InterfaceType.FullName).OrderBy(x => x).ToArray(),
        fields = type.Fields.Select(f => f.FullName).OrderBy(x => x).ToArray(),
        methods = type.Methods.Select(MethodFingerprint).OrderBy(m => Json(m)).ToArray(),
        properties = type.Properties.Select(p => p.FullName).OrderBy(x => x).ToArray(),
        events = type.Events.Select(e => e.FullName).OrderBy(x => x).ToArray(),
        nestedTypes = type.NestedTypes.Select(t => t.FullName).OrderBy(x => x).ToArray()
    }).ToArray();

    var resources = module.Resources.Select(resource => new
    {
        resource.Name,
        kind = resource.ResourceType.ToString(),
        hash = resource is EmbeddedResource embedded
            ? Convert.ToHexString(SHA256.HashData(embedded.GetResourceData()))
            : ""
    }).OrderBy(r => r.Name).ToArray();

    return new
    {
        assemblyName = assembly.Name.FullName,
        moduleName = module.Name,
        moduleMvid = module.Mvid,
        moduleKind = module.Kind.ToString(),
        architecture = module.Architecture.ToString(),
        runtimeVersion = module.RuntimeVersion,
        references = module.AssemblyReferences.Select(r => r.FullName).OrderBy(x => x).ToArray(),
        types = typeData,
        resources
    };
}

sealed class TargetedAssemblyResolver : DefaultAssemblyResolver
{
    private readonly AssemblyDefinition stub;

    public TargetedAssemblyResolver(AssemblyDefinition stub) => this.stub = stub;

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
        => name.Name == "Microsoft.Xna.Framework.Net" ? stub : base.Resolve(name);

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        => name.Name == "Microsoft.Xna.Framework.Net" ? stub : base.Resolve(name, parameters);
}

static AssemblyDefinition CreateMetadataOnlyXnaNetStub()
{
    var name = new AssemblyNameDefinition("Microsoft.Xna.Framework.Net", new Version(4, 0, 0, 0))
    {
        PublicKeyToken = Convert.FromHexString("842cf8be1de50553")
    };
    var stub = AssemblyDefinition.CreateAssembly(name, name.Name, ModuleKind.Dll);
    var module = stub.MainModule;
    var core = new AssemblyNameReference("mscorlib", new Version(2, 0, 5, 0))
    {
        PublicKeyToken = Convert.FromHexString("7cec85d7bea7798e")
    };
    var enumType = new TypeDefinition(
        "Microsoft.Xna.Framework.Net",
        "SendDataOptions",
        TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
        new TypeReference("System", "Enum", module, core));
    enumType.Fields.Add(new FieldDefinition(
        "value__",
        FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
        module.TypeSystem.Int32));
    module.Types.Add(enumType);
    return stub;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ManagedRoundTrip <input.dll> <separate-output.dll>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (!File.Exists(input)) throw new FileNotFoundException("Input assembly missing", input);
if (Path.GetFullPath(input) == output) throw new InvalidOperationException("Output must be a separate file.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);

var inputHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
using var xnaNetStub = CreateMetadataOnlyXnaNetStub();
using var resolver = new TargetedAssemblyResolver(xnaNetStub);
var reader = new ReaderParameters { InMemory = true, ReadSymbols = false, ReadingMode = ReadingMode.Immediate, AssemblyResolver = resolver };

object before;
using (var source = AssemblyDefinition.ReadAssembly(input, reader))
{
    before = AssemblyFingerprint(source);
    source.Write(output, new WriterParameters { WriteSymbols = false });
}

var outputHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant();
object after;
using (var roundTrip = AssemblyDefinition.ReadAssembly(output, reader))
    after = AssemblyFingerprint(roundTrip);

var beforeJson = Json(before);
var afterJson = Json(after);
var sourceHashAfter = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();

var report = new
{
    status = beforeJson == afterJson && inputHash == sourceHashAfter ? "roundtrip-pass" : "roundtrip-mismatch",
    input = Path.GetFileName(input),
    output = Path.GetFileName(output),
    inputSha256Before = inputHash,
    inputSha256After = sourceHashAfter,
    outputSha256 = outputHash,
    sourceFileUnchanged = inputHash == sourceHashAfter,
    metadataAndIlFingerprintPreserved = beforeJson == afterJson,
    originalFingerprint = before,
    roundTripFingerprint = after
};

var reportPath = output + ".report.json";
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine("Terraria managed assembly separate-copy round-trip: " + report.status);
Console.WriteLine("Original SHA-256 unchanged: " + report.sourceFileUnchanged);
Console.WriteLine("Metadata/IL/resource fingerprint preserved: " + report.metadataAndIlFingerprintPreserved);
Console.WriteLine("Original file was never overwritten.");
return report.status == "roundtrip-pass" ? 0 : 1;
