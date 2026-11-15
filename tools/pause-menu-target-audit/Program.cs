using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PauseMenuTargetAudit <assembly.dll> <report.json>");
    return 2;
}

using var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});

var ui = assembly.MainModule.Types.SingleOrDefault(t => t.FullName == "Terraria.UI")
    ?? throw new InvalidOperationException("Terraria.UI type not found.");

var requiredFields = new[]
{
    "MENU_PAUSE_COORDS", "uiCoords", "uiHeight", "menuString",
    "numMenuItems", "selectedMenu", "menuHC", "menuScale"
};
var fields = requiredFields.Select(name =>
{
    var matches = ui.Fields.Where(f => f.Name == name).ToArray();
    return new { name, count = matches.Length, signatures = matches.Select(f => f.FieldType.FullName).ToArray() };
}).ToArray();

var methodNames = new[] { ".cctor", "SetMenu", "UpdateMenu", "DrawMenu", "UpdateMouse" };
var methods = methodNames.Select(name =>
{
    var matches = ui.Methods.Where(m => m.Name == name).ToArray();
    return new
    {
        name,
        count = matches.Length,
        methods = matches.Select(DescribeMethod).ToArray()
    };
}).ToArray();

var fieldOk = fields.All(f => f.count == 1);
var methodOk = methods.All(m => m.count >= 1);
var report = new
{
    status = fieldOk && methodOk ? "pause-menu-targets-found" : "pause-menu-targets-missing",
    assembly = Path.GetFileName(args[0]),
    module = assembly.MainModule.Name,
    mvid = assembly.MainModule.Mvid,
    fields,
    methods
};
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Pause-menu target audit: {report.status}");
foreach (var f in fields) Console.WriteLine($"FIELD {f.name}: {f.count}");
foreach (var m in methods) Console.WriteLine($"METHOD {m.name}: {m.count}");
return report.status == "pause-menu-targets-found" ? 0 : 1;

static object DescribeMethod(MethodDefinition method)
{
    if (!method.HasBody)
        return new { method = method.FullName, hasBody = false, instructions = Array.Empty<object>() };

    var body = method.Body;
    var indexes = body.Instructions.Select((instruction, index) => (instruction, index))
        .ToDictionary(x => x.instruction, x => x.index);
    return new
    {
        method = method.FullName,
        hasBody = true,
        maxStack = body.MaxStackSize,
        codeSize = body.CodeSize,
        exceptionHandlers = body.ExceptionHandlers.Count,
        instructions = body.Instructions.Select((instruction, index) => new
        {
            index,
            offset = $"0x{instruction.Offset:x4}",
            opcode = instruction.OpCode.Code.ToString(),
            operand = Operand(instruction, indexes)
        }).ToArray()
    };
}

static string Operand(Instruction instruction, Dictionary<Instruction, int> indexes)
    => instruction.Operand switch
    {
        null => "",
        Instruction target => $"target-index:{indexes.GetValueOrDefault(target, -1)}",
        Instruction[] targets => "targets:" + string.Join(",", targets.Select(t => indexes.GetValueOrDefault(t, -1))),
        MethodReference method => method.FullName,
        FieldReference field => field.FullName,
        TypeReference type => type.FullName,
        VariableDefinition variable => $"var:{variable.Index}:{variable.VariableType.FullName}",
        ParameterDefinition parameter => $"arg:{parameter.Index}:{parameter.ParameterType.FullName}",
        string value => value,
        IFormattable value => value.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        _ => instruction.Operand.ToString() ?? ""
    };
