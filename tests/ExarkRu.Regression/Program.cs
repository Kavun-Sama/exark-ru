using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ExarkRu.Patcher;
using Mono.Cecil;
using Mono.Cecil.Cil;

string root = Path.GetFullPath(args[0]);
string managed = Path.GetFullPath(args[1]);
string runtimePath = Path.Combine(root, "src/ExarkRu.Runtime/bin/Release/netstandard2.1/ExarkRu.Runtime.dll");
Assembly runtime = Assembly.LoadFrom(runtimePath);
Type translatorType = runtime.GetType("ExarkRu.Runtime.VisibleTextTranslator", throwOnError: true)!;
object translator = Activator.CreateInstance(translatorType, nonPublic: true)!;
MethodInfo register = translatorType.GetMethod("Register")!;
MethodInfo translate = translatorType.GetMethod("TryTranslate")!;
using JsonDocument source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "localization/source.en.json")));
using JsonDocument russian = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "localization/translations.ru.json")));
foreach (JsonProperty table in source.RootElement.EnumerateObject())
{
    foreach (JsonElement row in table.Value.EnumerateArray())
    {
        string id = row.GetProperty("id").GetInt64().ToString();
        register.Invoke(translator, [row.GetProperty("text").GetString(),
            russian.RootElement.GetProperty(table.Name).GetProperty(id).GetString()]);
    }
}

CheckTranslation("UI_BUTTON_SKIP", [], preserveTemplate: true);
CheckTranslation("UI_BUTTON_SKIP", ["3"]);
CheckTranslation("UI_TOOLTIP_STATS", ["0", "2", "0"]);
CheckTranslation("UI_TOOLTIP_STATS", ["25", "3.5", "100"]);
CheckTranslation("UI_TOOLTIPOBJECT_UPGRADE", ["1", "3"]);
CheckTranslation("UI_SETTINGS_TITLE", []);
CheckTranslation("UI_SETTINGS_SHOW_KEYWORDS", []);

Type formatterType = runtime.GetType("ExarkRu.Runtime.TooltipTextFormatter", throwOnError: true)!;
string triggeredLabel = GetUiTranslation("UI_EFFECT_TYPE_TRIGGERED");
string passiveLabel = GetUiTranslation("UI_EFFECT_TYPE_PASSIVE");
object formatter = Activator.CreateInstance(formatterType, [triggeredLabel, passiveLabel])!;
MethodInfo format = formatterType.GetMethod("Format")!;
string triggeredHeader = GetUiSource("UI_TOOLTIP_TRIGGERED");
string passiveHeader = GetUiSource("UI_TOOLTIP_PASSIVE");
string effectBody = "Назначьте на <sprite name=\"building\">: получите <color=#0000b0>10</color>.";
CheckEffectLabel(triggeredHeader + effectBody, triggeredLabel + effectBody);
CheckEffectLabel(passiveHeader + effectBody, passiveLabel + effectBody);
CheckEffectLabel(effectBody, effectBody);
CheckEffectLabel(triggeredHeader + effectBody + "<br>" + passiveHeader + effectBody,
    triggeredLabel + effectBody + "<br>" + passiveLabel + effectBody);
CheckEffectLabel(triggeredLabel + effectBody, triggeredLabel + effectBody);
Console.WriteLine("PASS: triggered/passive sprite labels and mixed Cyrillic descriptions");

string temporary = Path.Combine(Path.GetTempPath(), "exark-ru-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    foreach (InputAssemblyPatcher patcher in InputAssemblyPatcher.All)
    {
        string original = Path.Combine(managed, patcher.FileName);
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(original));
        string copy = Path.Combine(temporary, patcher.FileName);
        File.Copy(original, copy, overwrite: true);
        patcher.Patch(copy, runtimePath);
        Require(patcher.IsPatched(copy), $"Missing hook: {patcher.FileName}");
        byte[] firstPatch = File.ReadAllBytes(copy);
        patcher.Patch(copy, runtimePath);
        Require(firstPatch.SequenceEqual(File.ReadAllBytes(copy)), $"Non-idempotent patch: {patcher.FileName}");
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(copy);
        foreach (MethodDefinition method in assembly.MainModule.GetType(patcher.TypeName).Methods
                     .Where(method => method.HasBody && patcher.MethodNames.Contains(method.Name)
                         && method.Parameters.Count > 0
                         && method.Parameters[0].ParameterType.FullName == patcher.InputTypeName))
        {
            var instructions = method.Body.Instructions;
            Require(instructions[0].OpCode == OpCodes.Ldarg
                && instructions[1].OpCode == OpCodes.Call
                && instructions[2].OpCode == OpCodes.Starg,
                $"Hook must precede parsing/formatting: {method.FullName}");
            Require(instructions.Count(instruction => instruction.Operand is MethodReference reference
                && reference.DeclaringType.FullName == "ExarkRu.Runtime.Bootstrap"
                && reference.Name == patcher.HookName) == 1, $"Duplicate hook: {method.FullName}");
        }

        Require(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))),
            $"Original changed: {patcher.FileName}");
        Console.WriteLine($"PASS: {patcher.FileName} hook ordering and idempotence");
    }

    string uninstallManaged = Path.Combine(temporary, "Exark_Data", "Managed");
    Directory.CreateDirectory(uninstallManaged);
    string[] restoredFiles = ["Assembly-CSharp.dll", "Unity.Localization.dll",
        "Unity.TextMeshPro.dll", "Febucci.TextAnimator.Runtime.dll"];
    foreach (string file in restoredFiles)
    {
        string installed = Path.Combine(managed, file);
        string backup = installed + ".exarkru.bak";
        File.Copy(installed, Path.Combine(uninstallManaged, file));
        File.Copy(File.Exists(backup) ? backup : installed,
            Path.Combine(uninstallManaged, file + ".exarkru.bak"));
    }

    File.Copy(runtimePath, Path.Combine(uninstallManaged, "ExarkRu.Runtime.dll"));
    File.Copy(Path.Combine(root, "localization/translations.ru.json"),
        Path.Combine(uninstallManaged, "translations.ru.json"));
    Assembly installer = Assembly.LoadFrom(Path.Combine(root,
        "src/ExarkRu.Patcher/bin/Release/net8.0-windows/ExarkRu.Patcher.dll"));
    Type program = installer.GetType("ExarkRu.Patcher.Program", throwOnError: true)!;
    Type pathsType = program.GetNestedType("Paths", BindingFlags.NonPublic)!;
    object paths = pathsType.GetMethod("Create")!.Invoke(null, [temporary])!;
    program.GetMethod("Uninstall", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [paths]);
    foreach (string file in restoredFiles)
    {
        string installed = Path.Combine(managed, file);
        string backup = installed + ".exarkru.bak";
        Require(File.ReadAllBytes(File.Exists(backup) ? backup : installed)
            .SequenceEqual(File.ReadAllBytes(Path.Combine(uninstallManaged, file))),
            $"Uninstall failed to restore {file}");
        Require(!File.Exists(Path.Combine(uninstallManaged, file + ".exarkru.bak")),
            $"Uninstall left backup for {file}");
    }

    Require(!File.Exists(Path.Combine(uninstallManaged, "ExarkRu.Runtime.dll"))
        && !File.Exists(Path.Combine(uninstallManaged, "translations.ru.json")),
        "Uninstall left localization payload");
    Console.WriteLine("PASS: uninstall restores all four assemblies and removes payload");
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

void CheckTranslation(string key, string[] values, bool preserveTemplate = false)
{
    JsonElement row = source.RootElement.GetProperty("TextUI").EnumerateArray()
        .Single(row => row.GetProperty("key").GetString() == key);
    string input = row.GetProperty("text").GetString()!;
    string expected = russian.RootElement.GetProperty("TextUI")
        .GetProperty(row.GetProperty("id").GetInt64().ToString()).GetString()!;
    if (!preserveTemplate)
    {
        for (int index = 0; index < values.Length; index++)
        {
            input = input.Replace("{" + index + "}", values[index]);
            expected = expected.Replace("{" + index + "}", values[index]);
        }
    }

    object?[] parameters = [input, null];
    Require((bool)translate.Invoke(translator, parameters)! && (string?)parameters[1] == expected,
        $"Incorrect translation: {key}: {parameters[1]}");
    Console.WriteLine($"PASS: {key} ({string.Join(", ", values)})");
}

string GetUiSource(string key)
{
    return source.RootElement.GetProperty("TextUI").EnumerateArray()
        .Single(row => row.GetProperty("key").GetString() == key)
        .GetProperty("text").GetString()!;
}

string GetUiTranslation(string key)
{
    JsonElement row = source.RootElement.GetProperty("TextUI").EnumerateArray()
        .Single(row => row.GetProperty("key").GetString() == key);
    return russian.RootElement.GetProperty("TextUI")
        .GetProperty(row.GetProperty("id").GetInt64().ToString()).GetString()!;
}

void CheckEffectLabel(string input, string expected)
{
    Require((string?)format.Invoke(formatter, [input]) == expected,
        "Effect label changed its type, body or inline sprites");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
