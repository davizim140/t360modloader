using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: MaxStackAudit <assembly.dll>");
    return 2;
}

using var assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters
{
    InMemory = true,
    ReadSymbols = false,
    ReadingMode = ReadingMode.Immediate
});

var results = new List<object>();
var failures = new List<string>();
var analyzed = 0;

foreach (var type in Flatten(assembly.MainModule.Types))
foreach (var method in type.Methods)
{
    if (!method.HasBody || method.Body.Instructions.Count == 0)
        continue;

    analyzed++;
    try
    {
        var observed = Analyze(method);
        var declared = method.Body.MaxStackSize;
        var valid = observed <= declared;
        results.Add(new { method = method.FullName, declaredMaxStack = declared, observedMaxStack = observed, valid });
        if (!valid)
            failures.Add($"{method.FullName}: observed {observed} > declared {declared}");
    }
    catch (Exception ex)
    {
        failures.Add($"{method.FullName}: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine($"Methods analyzed: {analyzed}");
Console.WriteLine($"Failures: {failures.Count}");
foreach (var failure in failures.Take(50))
    Console.WriteLine("FAIL " + failure);

var report = new
{
    status = failures.Count == 0 ? "maxstack-audit-pass" : "maxstack-audit-fail",
    assembly = Path.GetFileName(args[0]),
    methodsAnalyzed = analyzed,
    failureCount = failures.Count,
    failures,
    methods = results
};
var reportPath = Path.GetFullPath(args[0]) + ".maxstack-audit.json";
File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(report,
    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

return failures.Count == 0 ? 0 : 1;

static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in Flatten(type.NestedTypes))
            yield return nested;
    }
}

static int Analyze(MethodDefinition method)
{
    var body = method.Body;
    var instructions = body.Instructions;
    var indexes = instructions.Select((instruction, index) => (instruction, index))
        .ToDictionary(x => x.instruction, x => x.index);
    var heights = new int?[instructions.Count];
    var queue = new Queue<int>();

    void Seed(Instruction? instruction, int height)
    {
        if (instruction is null) return;
        if (!indexes.TryGetValue(instruction, out var index))
            throw new InvalidOperationException("Exception handler points outside method body.");
        Merge(index, height);
    }

    void Merge(int index, int height)
    {
        if (index < 0 || index >= instructions.Count)
            throw new InvalidOperationException($"Control flow target index {index} is outside method body.");
        if (height < 0)
            throw new InvalidOperationException("Evaluation stack underflow.");
        if (heights[index] is null)
        {
            heights[index] = height;
            queue.Enqueue(index);
        }
        else if (heights[index]!.Value != height)
            throw new InvalidOperationException($"Inconsistent stack height at IL_{instructions[index].Offset:x4}: {heights[index]} vs {height}.");
    }

    Merge(0, 0);
    foreach (var handler in body.ExceptionHandlers)
    {
        if (handler.HandlerType == ExceptionHandlerType.Filter)
            Seed(handler.FilterStart, 1);
        Seed(handler.HandlerStart,
            handler.HandlerType is ExceptionHandlerType.Catch or ExceptionHandlerType.Filter ? 1 : 0);
    }

    var maximum = 0;
    while (queue.Count > 0)
    {
        var index = queue.Dequeue();
        var instruction = instructions[index];
        var input = heights[index]!.Value;
        var (pop, push) = StackEffect(method, instruction);
        if (input < pop)
            throw new InvalidOperationException($"Stack underflow at IL_{instruction.Offset:x4} ({instruction.OpCode}).");
        var output = instruction.OpCode.Code is Code.Leave or Code.Leave_S ? 0 : input - pop + push;
        maximum = Math.Max(maximum, Math.Max(input, output));

        var code = instruction.OpCode.Code;
        if (code is Code.Ret or Code.Throw or Code.Rethrow or Code.Endfinally or Code.Endfilter or Code.Jmp)
            continue;

        if (instruction.OpCode.FlowControl == FlowControl.Branch)
        {
            if (instruction.Operand is not Instruction target)
                throw new InvalidOperationException($"Missing branch target at IL_{instruction.Offset:x4}.");
            Merge(indexes[target], output);
            continue;
        }

        if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
        {
            if (instruction.Operand is Instruction target)
                Merge(indexes[target], output);
            else if (instruction.Operand is Instruction[] targets)
                foreach (var branchTarget in targets)
                    Merge(indexes[branchTarget], output);
            else if (code != Code.Switch)
                throw new InvalidOperationException($"Unsupported conditional branch operand at IL_{instruction.Offset:x4}.");
        }

        if (index + 1 < instructions.Count)
            Merge(index + 1, output);
    }

    return maximum;
}

static (int Pop, int Push) StackEffect(MethodDefinition owner, Instruction instruction)
{
    var code = instruction.OpCode.Code;
    if (code is Code.Leave or Code.Leave_S or Code.Endfinally or Code.Rethrow)
        return (0, 0);
    if (code == Code.Endfilter || code == Code.Throw)
        return (1, 0);
    if (code == Code.Ret)
        return (owner.ReturnType.MetadataType == MetadataType.Void ? 0 : 1, 0);
    if (code == Code.Jmp)
        return (0, 0);

    if (instruction.OpCode.StackBehaviourPop == StackBehaviour.Varpop)
    {
        return instruction.Operand switch
        {
            MethodReference called when code == Code.Newobj => (called.Parameters.Count, 1),
            MethodReference called => (called.Parameters.Count + (called.HasThis ? 1 : 0), called.ReturnType.MetadataType == MetadataType.Void ? 0 : 1),
            CallSite site => (site.Parameters.Count + (site.HasThis ? 1 : 0) + 1, site.ReturnType.MetadataType == MetadataType.Void ? 0 : 1),
            _ => throw new InvalidOperationException($"Unsupported variable-pop instruction {instruction.OpCode}.")
        };
    }

    var pop = PopCount(instruction.OpCode.StackBehaviourPop);
    var push = instruction.OpCode.StackBehaviourPush == StackBehaviour.Varpush
        ? throw new InvalidOperationException($"Unexpected variable-push instruction {instruction.OpCode}.")
        : PushCount(instruction.OpCode.StackBehaviourPush);
    return (pop, push);
}

static int PopCount(StackBehaviour behaviour) => behaviour.ToString() switch
{
    "Pop0" => 0,
    "Pop1" or "Popi" or "Popref" => 1,
    "Pop1_pop1" or "Popi_pop1" or "Popi_popi" or "Popi_popi8" or "Popi_popr4" or "Popi_popr8" or "Popref_pop1" or "Popref_popi" => 2,
    "Popi_popi_popi" or "Popref_popi_pop1" or "Popref_popi_popi" or "Popref_popi_popi8" or "Popref_popi_popr4" or "Popref_popi_popr8" or "Popref_popi_popref" => 3,
    "PopAll" => throw new InvalidOperationException("PopAll requires instruction-specific handling."),
    _ => throw new InvalidOperationException($"Unsupported pop behavior: {behaviour}")
};

static int PushCount(StackBehaviour behaviour) => behaviour.ToString() switch
{
    "Push0" => 0,
    "Push1" or "Pushi" or "Pushi8" or "Pushr4" or "Pushr8" or "Pushref" => 1,
    "Push1_push1" => 2,
    _ => throw new InvalidOperationException($"Unsupported push behavior: {behaviour}")
};
