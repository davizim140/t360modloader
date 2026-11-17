using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MainMenuAudit <input.dll> <report.json>");
    return 2;
}
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { InMemory = true, ReadSymbols = false, ReadingMode = ReadingMode.Immediate });
var ui = assembly.MainModule.Types.Single(t => t.FullName == "Terraria.UI");
var update = ui.Methods.Single(m => m.Name == "UpdateMenu" && m.HasBody);
var setMenu = ui.Methods.Single(m => m.Name == "SetMenu" && m.Parameters.Count == 3 && m.HasBody);
var title = setMenu.Body.Instructions.Select((i, n) => (i, n)).Where(x =>
    x.i.OpCode.Code == Code.Ldsfld && x.i.Operand is FieldReference f && f.Name == "MENU_TITLE_COORDS").ToArray();
var hits = new List<object>();
for (var n = 0; n < update.Body.Instructions.Count; n++)
{
    var i = update.Body.Instructions[n];
    var field = i.Operand as FieldReference;
    if (field is null || field.Name is not ("menuString" or "numMenuItems" or "selectedMenu" or "MENU_TITLE_COORDS")) continue;
    var start = Math.Max(0, n - 5);
    var end = Math.Min(update.Body.Instructions.Count - 1, n + 7);
    var window = new List<string>();
    for (var k = start; k <= end; k++)
    {
        var x = update.Body.Instructions[k];
        var operand = x.Operand switch
        {
            null => "",
            Instruction target => " -> IL_" + target.Offset.ToString("x4"),
            VariableDefinition v => " V_" + v.Index,
            ParameterDefinition p => " P_" + p.Index,
            FieldReference fr => " " + fr.DeclaringType.Name + "::" + fr.Name,
            MethodReference mr => " " + mr.DeclaringType.Name + "::" + mr.Name,
            _ => " " + x.Operand
        };
        window.Add($"IL_{x.Offset:x4}: {x.OpCode}{operand}");
    }
    hits.Add(new { offset = $"IL_{i.Offset:x4}", field = field.Name, window });
}
var report = new
{
    status = "main-menu-il-audit-pass",
    sourceSha256Before = before,
    sourceSha256After = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant(),
    updateMenuMethod = update.FullName,
    localVariables = update.Body.Variables.Select((v, n) => new { index = n, type = v.VariableType.FullName }).ToArray(),
    titleCoordinateReferencesInSetMenu = title.Select(x => new { offset = $"IL_{x.i.Offset:x4}", instruction = x.i.ToString() }).ToArray(),
    relevantUpdateMenuInstructionWindows = hits
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Main menu IL audit: {report.status}; relevant field references={hits.Count}; locals={update.Body.Variables.Count}");
return report.sourceSha256Before == report.sourceSha256After ? 0 : 1;
