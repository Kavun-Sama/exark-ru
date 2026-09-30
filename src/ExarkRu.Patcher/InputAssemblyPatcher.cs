using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ExarkRu.Patcher;

internal sealed record InputAssemblyPatcher(
    string FileName,
    string TypeName,
    string[] MethodNames,
    string InputTypeName,
    string HookName)
{
    private const string BootstrapTypeName = "ExarkRu.Runtime.Bootstrap";

    public static IReadOnlyList<InputAssemblyPatcher> All { get; } =
    [
        new("Unity.TextMeshPro.dll", "TMPro.TMP_Text", ["set_text", "SetText"],
            "System.String", "TranslateVisibleText"),
        new("Unity.TextMeshPro.dll", "TMPro.TMP_Text", ["SetText"],
            "System.Text.StringBuilder", "TranslateVisibleTextBuilder"),
        new("Febucci.TextAnimator.Runtime.dll", "Febucci.UI.Core.TAnimCore", ["ConvertText"],
            "System.String", "TranslateVisibleText"),
        new("Unity.Localization.dll", "UnityEngine.Localization.Settings.LocalizationSettings",
            ["set_SelectedLocale"], "UnityEngine.Localization.Locale", "ResolveSelectedLocale")
    ];

    public bool IsPatched(string path)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);
        MethodDefinition[] methods = GetMethods(assembly);
        return methods.Length > 0 && methods.All(method => method.Body.Instructions.Any(IsHook));
    }

    public void Patch(string path, string runtimePath)
    {
        using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(runtimePath);
        MethodDefinition hook = runtime.MainModule.GetType(BootstrapTypeName).Methods
            .Single(method => method.Name == HookName && method.IsStatic);
        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + ".exarkru.tmp";
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path,
                   new ReaderParameters { AssemblyResolver = resolver }))
        {
            MethodDefinition[] methods = GetMethods(assembly);
            if (methods.Length == 0)
            {
                throw new InvalidOperationException($"Методы {TypeName} не найдены в {FileName}.");
            }

            MethodReference reference = assembly.MainModule.ImportReference(hook);
            foreach (MethodDefinition method in methods)
            {
                if (method.Body.Instructions.Any(IsHook))
                {
                    continue;
                }

                ILProcessor il = method.Body.GetILProcessor();
                Instruction first = method.Body.Instructions[0];
                ParameterDefinition input = method.Parameters[0];
                il.InsertBefore(first, il.Create(OpCodes.Ldarg, input));
                il.InsertBefore(first, il.Create(OpCodes.Call, reference));
                il.InsertBefore(first, il.Create(OpCodes.Starg, input));
            }

            assembly.Write(temporaryPath);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private MethodDefinition[] GetMethods(AssemblyDefinition assembly)
    {
        return assembly.MainModule.GetType(TypeName)?.Methods
            .Where(method => method.HasBody && MethodNames.Contains(method.Name)
                && method.Parameters.Count > 0
                && method.Parameters[0].ParameterType.FullName == InputTypeName)
            .ToArray() ?? [];
    }

    private bool IsHook(Instruction instruction)
    {
        return instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference method
            && method.DeclaringType.FullName == BootstrapTypeName
            && method.Name == HookName;
    }
}
