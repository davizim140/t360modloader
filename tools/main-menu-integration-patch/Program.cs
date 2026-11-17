using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2) throw new ArgumentException("Usage: MainMenuIntegrationPatch <input.dll> <output.dll>");
var input = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (!File.Exists(input) || input == output) throw new InvalidOperationException("Input must exist; output must be a separate file.");
var sourceHash = Hash(input);
using var resolver = new Resolver();
var rp = new ReaderParameters { InMemory=true, ReadSymbols=false, ReadingMode=ReadingMode.Immediate, AssemblyResolver=resolver };
using var asm = AssemblyDefinition.ReadAssembly(input,rp);
var mod=asm.MainModule;
var ui=mod.Types.Single(t=>t.FullName=="Terraria.UI");
var main=mod.Types.Single(t=>t.FullName=="Terraria.Main");
var update=ui.Methods.Single(m=>m.Name=="UpdateMenu" && m.HasBody);
var il=update.Body.GetILProcessor();
FieldDefinition Field(TypeDefinition t,string n)=>t.Fields.Single(f=>f.Name==n);
var menuString=Field(ui,"menuString"); var count=Field(ui,"numMenuItems");
var coords=Field(ui,"MENU_TITLE_COORDS"); var statusText=Field(ui,"statusText");
var isTrial=Field(main,"isTrial");
var setMenu=ui.Methods.Single(m=>m.Name=="SetMenu"&&m.Parameters.Count==3);
var play=main.Methods.Single(m=>m.Name=="PlaySound"&&m.Parameters.Count==1);
var status=Convert.ToInt32(mod.Types.Single(t=>t.FullName=="Terraria.MenuMode").Fields.Single(f=>f.Name=="STATUS_SCREEN").Constant);
var quit=Convert.ToInt32(mod.Types.Single(t=>t.FullName=="Terraria.MenuMode").Fields.Single(f=>f.Name=="QUIT").Constant);
var ins=update.Body.Instructions;

// Trial: move Quit label from slot 6 to 7, insert T360 at 6.
var trialQuitLabel=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldfld)&&Ref(i.Next,menuString)&&Is(i.Next?.Next,Code.Ldc_I4_6)&&
    Is(i.Next?.Next?.Next,Code.Ldsfld)&&i.Next.Next.Next.Operand is FieldReference lf&&lf.DeclaringType.FullName=="Terraria.Lang"&&lf.Name=="menu"&&
    Is(i.Next?.Next?.Next?.Next,Code.Ldc_I4_S)&&Int(i.Next!.Next!.Next!.Next!)==15);
trialQuitLabel.Next!.Next!.OpCode=OpCodes.Ldc_I4_7;
var trialQuitStelem=FindFirstAfter(trialQuitLabel,i=>Is(i,Code.Stelem_Ref));
var trialInsert=new[]{
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,menuString),Instruction.Create(OpCodes.Ldc_I4_6),
 Instruction.Create(OpCodes.Ldstr,"T360 ModLoader"),Instruction.Create(OpCodes.Stelem_Ref)};
foreach(var x in trialInsert)il.InsertBefore(trialQuitLabel,x);
var trialCount=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldc_I4_7)&&Is(i.Next?.Next,Code.Stfld)&&Ref(i.Next?.Next,count)&&
    Is(i.Next?.Next?.Next,Code.Ldsfld)&&Ref(i.Next?.Next?.Next,coords));
trialCount.Next!.OpCode=OpCodes.Ldc_I4_8;

// Trial coordinate slot 7 is enabled; slot 6 remains the existing Quit coordinate, also enabled.
var trialCoord6=FindUnique(update,i=>Is(i,Code.Ldsfld)&&Ref(i,coords)&&Is(i.Next,Code.Ldc_I4_6)&&Is(i.Next?.Next,Code.Ldelema));
var trialCoord7=new[]{Instruction.Create(OpCodes.Ldsfld,coords),Instruction.Create(OpCodes.Ldc_I4_7),Instruction.Create(OpCodes.Ldelema,trialCoord6.Next!.Next!.Operand as TypeReference ?? coords.FieldType.GetElementType()),Instruction.Create(OpCodes.Ldc_I4,480),Instruction.Create(OpCodes.Stfld,trialCoord6.Next!.Next!.Next!.Next!.Operand as FieldReference ?? throw new InvalidOperationException("X field anchor missing"))};
var xField=(FieldReference)trialCoord6.Next!.Next!.Next!.Next!.Operand!;
foreach(var x in trialCoord7)il.InsertBefore(trialCount,x);

// Full game: slot 5 becomes T360; add Quit at slot 6; enable coordinate slot 6.
var fullQuitLabel=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldfld)&&Ref(i.Next,menuString)&&Is(i.Next?.Next,Code.Ldc_I4_5)&&
    Is(i.Next?.Next?.Next,Code.Ldsfld)&&i.Next.Next.Next.Operand is FieldReference lf&&lf.DeclaringType.FullName=="Terraria.Lang"&&lf.Name=="menu"&&
    Is(i.Next?.Next?.Next?.Next,Code.Ldc_I4_S)&&Int(i.Next!.Next!.Next!.Next!)==15);
var fullQuitStelem=FindFirstAfter(fullQuitLabel,i=>Is(i,Code.Stelem_Ref));
var replaceLang=fullQuitLabel.Next!.Next!.Next!.Next!;
if(!Is(replaceLang,Code.Ldc_I4_S)||Int(replaceLang)!=15)throw new InvalidOperationException("Full-game Quit label anchor changed.");
// Replace Lang.menu[15] with literal T360 label.
var afterLang=replaceLang.Next!.Next!;
il.Replace(fullQuitLabel.Next!.Next!.Next!,Instruction.Create(OpCodes.Ldstr,"T360 ModLoader"));
il.Remove(replaceLang); il.Remove(replaceLang.Next!);
var fullInsert=new[]{Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,menuString),Instruction.Create(OpCodes.Ldc_I4_6),
 Instruction.Create(OpCodes.Ldsfld,mod.Types.Single(t=>t.FullName=="Terraria.Lang").Fields.Single(f=>f.Name=="menu")),
 Instruction.Create(OpCodes.Ldc_I4_S,(sbyte)15),Instruction.Create(OpCodes.Ldelem_Ref),Instruction.Create(OpCodes.Stelem_Ref)};
foreach(var x in fullInsert)il.InsertAfter(fullQuitStelem,x);
var fullCount=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldc_I4_6)&&Is(i.Next?.Next,Code.Stfld)&&Ref(i.Next?.Next,count)&&
    Is(i.Next?.Next?.Next,Code.Ldsfld)&&Ref(i.Next?.Next?.Next,coords));
fullCount.Next!.OpCode=OpCodes.Ldc_I4_7;
var hidden6=FindUnique(update,i=>Is(i,Code.Ldsfld)&&Ref(i,coords)&&Is(i.Next,Code.Ldc_I4_6)&&Is(i.Next?.Next,Code.Ldelema)&&
 Is(i.Next?.Next?.Next,Code.Ldc_I4_0)&&Is(i.Next?.Next?.Next?.Next,Code.Stfld)&&i.Next.Next.Next.Next.Operand is FieldReference xf&&xf.Name=="X");
il.Replace(hidden6.Next!.Next!.Next!,Instruction.Create(OpCodes.Ldc_I4,480));

// T360 click handler: full game index 5, Trial index 6. Intercepts before the original Marketplace/Quit branch.
var selected5=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldfld)&&RefName(i.Next,"selectedMenu")&&
 Is(i.Next?.Next,Code.Ldc_I4_5)&&Is(i.Next?.Next?.Next,Code.Bne_Un_S));
var selectionEnd=FindUnique(update,i=>Is(i,Code.Ldarg_0)&&Is(i.Next,Code.Ldc_I4)&&Int(i.Next!)==16384&&Is(i.Next?.Next,Code.Call)&&
 i.Next.Next.Operand is MethodReference mr&&mr.Name=="IsButtonTriggered");
var skip=Instruction.Create(OpCodes.Nop);
var handler=new[]{
 Instruction.Create(OpCodes.Ldsfld,isTrial),Instruction.Create(OpCodes.Brfalse_S,skip),
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,Field(ui,"selectedMenu")),Instruction.Create(OpCodes.Ldc_I4_6),
 Instruction.Create(OpCodes.Bne_Un,selected5),
 Instruction.Create(OpCodes.Br,skip),
 skip,
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,Field(ui,"selectedMenu")),Instruction.Create(OpCodes.Ldc_I4_5),
 Instruction.Create(OpCodes.Bne_Un,selected5),
 Instruction.Create(OpCodes.Ldc_I4_S,(sbyte)10),Instruction.Create(OpCodes.Call,play),
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldc_I4,status),Instruction.Create(OpCodes.Ldc_I4_1),Instruction.Create(OpCodes.Ldc_I4_0),
 Instruction.Create(OpCodes.Call,setMenu),Instruction.Create(OpCodes.Ldarg_0),
 Instruction.Create(OpCodes.Ldstr,"T360 ModLoader\nGerenciador carregado.\nB: Voltar"),Instruction.Create(OpCodes.Stfld,statusText),
 Instruction.Create(OpCodes.Br,selectionEnd)};
foreach(var x in handler)il.InsertBefore(selected5,x);

// Trial Quit moved to index 7; retain Full-game Quit at index 6.
var trialQuitHandler=new[]{
 Instruction.Create(OpCodes.Ldsfld,isTrial),Instruction.Create(OpCodes.Brfalse,selectionEnd),
 Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldfld,Field(ui,"selectedMenu")),Instruction.Create(OpCodes.Ldc_I4_7),
 Instruction.Create(OpCodes.Bne_Un,selectionEnd),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldc_I4,quit),
 Instruction.Create(OpCodes.Ldc_I4_1),Instruction.Create(OpCodes.Ldc_I4_0),Instruction.Create(OpCodes.Call,setMenu),
 Instruction.Create(OpCodes.Br,selectionEnd)};
foreach(var x in trialQuitHandler)il.InsertBefore(selectionEnd,x);

update.Body.MaxStackSize=Math.Max(update.Body.MaxStackSize,4);
var beforeMvid=mod.Mvid; Directory.CreateDirectory(Path.GetDirectoryName(output)!); asm.Write(output);
using var check=AssemblyDefinition.ReadAssembly(output,rp);
var checkedUpdate=check.MainModule.Types.Single(t=>t.FullName=="Terraria.UI").Methods.Single(m=>m.Name=="UpdateMenu");
var valid=Validate(check.MainModule);
var pass=sourceHash==Hash(input)&&check.MainModule.Mvid==beforeMvid&&valid&&
 checkedUpdate.Body.Instructions.Any(i=>Is(i,Code.Ldstr)&&Equals(i.Operand,"T360 ModLoader"))&&
 checkedUpdate.Body.Instructions.Any(i=>Is(i,Code.Ldc_I4_8))&&checkedUpdate.Body.Instructions.Any(i=>Is(i,Code.Ldstr)&&Equals(i.Operand,"T360 ModLoader\nGerenciador carregado.\nB: Voltar"));
var report=new{status=pass?"main-menu-integration-copy-patch-pass":"main-menu-integration-copy-patch-fail",mode="separate-copy-only",sourceSha256=sourceHash,sourceUnchanged=sourceHash==Hash(input),outputSha256=Hash(output),changes=new[]{"Full game: T360 at index 5; Quit at index 6; count 7","Trial: Marketplace at 5; T360 at 6; Quit at 7; count 8","T360 selection routes to STATUS_SCREEN landing page","Trial Quit selection moved to index 7","Title coordinate slots 6/7 enabled as required"},validation=new{controlFlowTargetsValid=valid,assemblyReopened=true,moduleIdentityPreserved=check.MainModule.Mvid==beforeMvid},safety=new{originalDllOverwritten=false,xexProducedByPatchStep=false,xboxRuntimeTested=false,signatureNotAddressed=true}};
File.WriteAllText(output+".patch-report.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"T360 main menu patch: {report.status}; control flow={valid}");
return pass?0:1;

static string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
static bool Is(Instruction? i,Code c)=>i?.OpCode.Code==c;
static int Int(Instruction i)=>i.OpCode.Code switch{Code.Ldc_I4_S when i.Operand is sbyte n=>n,Code.Ldc_I4 when i.Operand is int n=>n,Code.Ldc_I4_5=>5,Code.Ldc_I4_6=>6,Code.Ldc_I4_7=>7,_=>int.MinValue};
static bool Ref(Instruction? i,FieldDefinition f)=>i?.Operand is FieldReference r&&r.Name==f.Name&&r.DeclaringType.FullName==f.DeclaringType.FullName;
static bool RefName(Instruction? i,string n)=>i?.Operand is FieldReference r&&r.Name==n;
static Instruction FindUnique(MethodDefinition m,Func<Instruction,bool> p){var a=m.Body.Instructions.Where(p).ToArray();if(a.Length!=1)throw new InvalidOperationException($"Expected one IL anchor; found {a.Length}.");return a[0];}
static Instruction FindFirstAfter(Instruction i,Func<Instruction,bool> p)=>i.GetSelfAndFollowing().Skip(1).First(p);
static bool Validate(ModuleDefinition m){foreach(var t in Flat(m.Types))foreach(var x in t.Methods.Where(x=>x.HasBody)){var s=x.Body.Instructions.ToHashSet();foreach(var i in x.Body.Instructions){if(i.Operand is Instruction d&&!s.Contains(d))return false;if(i.Operand is Instruction[] ds&&ds.Any(d=>!s.Contains(d)))return false;}foreach(var h in x.Body.ExceptionHandlers)if((h.TryStart!=null&&!s.Contains(h.TryStart))||(h.TryEnd!=null&&!s.Contains(h.TryEnd))||(h.HandlerStart!=null&&!s.Contains(h.HandlerStart))||(h.HandlerEnd!=null&&!s.Contains(h.HandlerEnd))||(h.FilterStart!=null&&!s.Contains(h.FilterStart)))return false;}return true;}
static IEnumerable<TypeDefinition> Flat(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Flat(t.NestedTypes))yield return n;}}
sealed class Resolver:DefaultAssemblyResolver{}
static class Ext{public static IEnumerable<Instruction> GetSelfAndFollowing(this Instruction i){for(var p=i;p!=null;p=p.Next)yield return p;}}
