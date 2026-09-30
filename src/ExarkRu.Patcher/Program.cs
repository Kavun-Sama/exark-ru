using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ExarkRu.Patcher;

internal static class Program
{
    private const string RuntimeFileName = "ExarkRu.Runtime.dll";
    private const string TranslationFileName = "translations.ru.json";
    private const string BackupSuffix = ".exarkru.bak";
    private const string PlayerPrefsRegistryPath = @"Software\Robot Cat Limited\Exark";
    private const string LudiqSavedVariablesPrefix = "LudiqSavedVariables_";

    private static int Main(string[] args)
    {
        try
        {
            Arguments options = Arguments.Parse(args);
            string gameRoot = options.GameRoot ?? SteamLocator.FindExarkInstall()
                ?? throw new InvalidOperationException(
                    "Exark не найден автоматически. Запустите с --game \"D:\\...\\Exark\".");

            Paths paths = Paths.Create(gameRoot);
            paths.ValidateGame();

            if (options.Uninstall)
            {
                Uninstall(paths);
                return 0;
            }

            Install(paths);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Ошибка: {exception.Message}");
            return 1;
        }
    }

    private static void Install(Paths paths)
    {
        string payloadDirectory = AppContext.BaseDirectory;
        string runtimeSource = Path.Combine(payloadDirectory, RuntimeFileName);
        string translationsSource = Path.Combine(payloadDirectory, TranslationFileName);

        if (!File.Exists(runtimeSource) || !File.Exists(translationsSource))
        {
            throw new FileNotFoundException(
                $"Рядом с патчером должны лежать {RuntimeFileName} и {TranslationFileName}.");
        }

        bool gameAlreadyPatched = AssemblyPatcher.IsPatched(paths.AssemblyCSharp);
        if (!gameAlreadyPatched)
        {
            File.Copy(paths.AssemblyCSharp, paths.BackupAssembly, overwrite: true);
            AssemblyPatcher.Patch(paths.AssemblyCSharp, runtimeSource);
        }

        bool localizationAlreadyPatched = LocalizationAssemblyPatcher.IsPatched(paths.UnityLocalization);
        if (!localizationAlreadyPatched)
        {
            File.Copy(paths.UnityLocalization, paths.BackupUnityLocalization, overwrite: true);
            LocalizationAssemblyPatcher.Patch(paths.UnityLocalization, runtimeSource);
        }

        foreach (InputAssemblyPatcher patcher in InputAssemblyPatcher.All)
        {
            string path = Path.Combine(paths.ManagedDirectory, patcher.FileName);
            if (!patcher.IsPatched(path))
            {
                string backup = path + BackupSuffix;
                if (!File.Exists(backup))
                {
                    File.Copy(path, backup);
                }

                patcher.Patch(path, runtimeSource);
            }

            if (!patcher.IsPatched(path))
            {
                throw new InvalidOperationException($"Не удалось проверить перехват в {patcher.FileName}.");
            }
        }

        File.Copy(runtimeSource, paths.RuntimeAssembly, overwrite: true);
        File.Copy(translationsSource, paths.TranslationCatalog, overwrite: true);
        MigrateInvalidLanguageIndex();

        if (!AssemblyPatcher.IsPatched(paths.AssemblyCSharp))
        {
            throw new InvalidOperationException("Проверка после установки не обнаружила вызов русского модуля.");
        }

        if (!LocalizationAssemblyPatcher.IsPatched(paths.UnityLocalization))
        {
            throw new InvalidOperationException(
                "Проверка после установки не обнаружила перехват локализованных строк.");
        }

        Console.WriteLine(
            gameAlreadyPatched && localizationAlreadyPatched
                ? "Русификатор обновлён."
                : "Русификатор установлен.");
        Console.WriteLine("В настройках языка появится «Русский (RU)». ");
        Console.WriteLine($"Assembly-CSharp SHA-256: {HashFile(paths.AssemblyCSharp)}");
    }

    private static void MigrateInvalidLanguageIndex()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PlayerPrefsRegistryPath, writable: true);
        if (key == null)
        {
            return;
        }

        string? valueName = key.GetValueNames()
            .FirstOrDefault(name => name.StartsWith(LudiqSavedVariablesPrefix, StringComparison.Ordinal));
        if (valueName == null || key.GetValue(valueName) is not byte[] data)
        {
            return;
        }

        string json = Encoding.UTF8.GetString(data).TrimEnd('\0');
        const string marker = "\"name\":\"settingLanguageIndex\",\"value\":{\"$content\":";
        int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return;
        }

        int valueStart = markerIndex + marker.Length;
        int valueEnd = valueStart;
        while (valueEnd < json.Length && char.IsDigit(json[valueEnd]))
        {
            valueEnd++;
        }

        if (valueEnd == valueStart
            || !int.TryParse(json.Substring(valueStart, valueEnd - valueStart), out int index)
            || index < 6)
        {
            return;
        }

        string migrated = json.Substring(0, valueStart) + "0" + json.Substring(valueEnd);
        byte[] migratedData = Encoding.UTF8.GetBytes(migrated + "\0");
        key.SetValue(valueName, migratedData, RegistryValueKind.Binary);
        Console.WriteLine(
            $"Исправлен сохранённый индекс языка Exark: {index} → 0. Русский выбор сохранён отдельно.");
    }

    private static void Uninstall(Paths paths)
    {
        if (!File.Exists(paths.BackupAssembly))
        {
            throw new FileNotFoundException("Резервная копия Assembly-CSharp.dll не найдена.");
        }

        File.Copy(paths.BackupAssembly, paths.AssemblyCSharp, overwrite: true);
        File.Delete(paths.BackupAssembly);
        if (File.Exists(paths.BackupUnityLocalization))
        {
            File.Copy(paths.BackupUnityLocalization, paths.UnityLocalization, overwrite: true);
            File.Delete(paths.BackupUnityLocalization);
        }
        foreach (InputAssemblyPatcher patcher in InputAssemblyPatcher.All)
        {
            string path = Path.Combine(paths.ManagedDirectory, patcher.FileName);
            string backup = path + BackupSuffix;
            if (File.Exists(backup))
            {
                File.Copy(backup, path, overwrite: true);
                File.Delete(backup);
            }
        }

        File.Delete(paths.RuntimeAssembly);
        File.Delete(paths.TranslationCatalog);

        Console.WriteLine("Русификатор удалён, оригинальный Assembly-CSharp.dll восстановлен.");
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record Arguments(string? GameRoot, bool Uninstall)
    {
        public static Arguments Parse(string[] args)
        {
            string? gameRoot = null;
            bool uninstall = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game" when i + 1 < args.Length:
                        gameRoot = Path.GetFullPath(args[++i]);
                        break;
                    case "--uninstall":
                        uninstall = true;
                        break;
                    default:
                        throw new ArgumentException($"Неизвестный аргумент: {args[i]}");
                }
            }

            return new Arguments(gameRoot, uninstall);
        }
    }

    private sealed record Paths(
        string GameRoot,
        string ManagedDirectory,
        string AssemblyCSharp,
        string BackupAssembly,
        string UnityLocalization,
        string BackupUnityLocalization,
        string RuntimeAssembly,
        string TranslationCatalog)
    {
        public static Paths Create(string gameRoot)
        {
            string managed = Path.Combine(gameRoot, "Exark_Data", "Managed");
            string assembly = Path.Combine(managed, "Assembly-CSharp.dll");
            string unityLocalization = Path.Combine(managed, "Unity.Localization.dll");
            return new Paths(
                gameRoot,
                managed,
                assembly,
                assembly + BackupSuffix,
                unityLocalization,
                unityLocalization + BackupSuffix,
                Path.Combine(managed, RuntimeFileName),
                Path.Combine(managed, TranslationFileName));
        }

        public void ValidateGame()
        {
            if (!File.Exists(Path.Combine(GameRoot, "Exark.exe"))
                || !File.Exists(AssemblyCSharp)
                || !File.Exists(UnityLocalization))
            {
                throw new DirectoryNotFoundException($"Это не папка установленной Exark: {GameRoot}");
            }
        }
    }

    private static class LocalizationAssemblyPatcher
    {
        private const string RuntimeAssemblyName = "ExarkRu.Runtime";
        private const string BootstrapTypeName = "ExarkRu.Runtime.Bootstrap";
        private const string LocalizedStringTypeName = "UnityEngine.Localization.LocalizedString";
        private const string TranslationMethodName = "TranslateVisibleText";

        public static bool IsPatched(string assemblyPath)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            TypeDefinition? localizedString = assembly.MainModule.GetType(LocalizedStringTypeName);
            return localizedString?.Methods
                .Where(IsSynchronousGetter)
                .SelectMany(method => method.Body.Instructions)
                .Any(IsTranslationCall) == true;
        }

        public static void Patch(string assemblyPath, string runtimeAssemblyPath)
        {
            using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(runtimeAssemblyPath);
            MethodDefinition translate = runtime.MainModule.GetType(BootstrapTypeName)
                ?.Methods.FirstOrDefault(
                    method => method.Name == TranslationMethodName
                        && method.IsStatic
                        && method.Parameters.Count == 1)
                ?? throw new InvalidOperationException(
                    "ExarkRu.Runtime.Bootstrap.TranslateVisibleText(string) не найден.");

            DefaultAssemblyResolver resolver = new();
            resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
            ReaderParameters readerParameters = new() { AssemblyResolver = resolver, ReadWrite = false };
            string temporaryPath = assemblyPath + ".exarkru.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters))
            {
                TypeDefinition localizedString = assembly.MainModule.GetType(LocalizedStringTypeName)
                    ?? throw new InvalidOperationException(
                        $"Тип {LocalizedStringTypeName} не найден в Unity.Localization.dll.");

                AssemblyNameReference runtimeReference = assembly.MainModule.AssemblyReferences
                    .FirstOrDefault(reference => reference.Name == RuntimeAssemblyName)
                    ?? new AssemblyNameReference(RuntimeAssemblyName, runtime.Name.Version);
                if (!assembly.MainModule.AssemblyReferences.Contains(runtimeReference))
                {
                    assembly.MainModule.AssemblyReferences.Add(runtimeReference);
                }

                TypeReference bootstrap = new(
                    "ExarkRu.Runtime",
                    "Bootstrap",
                    assembly.MainModule,
                    runtimeReference);
                MethodReference translateReference = new(
                    TranslationMethodName,
                    assembly.MainModule.TypeSystem.String,
                    bootstrap)
                {
                    HasThis = false
                };
                translateReference.Parameters.Add(
                    new ParameterDefinition(assembly.MainModule.TypeSystem.String));

                int patchedMethods = 0;
                foreach (MethodDefinition method in localizedString.Methods.Where(IsSynchronousGetter))
                {
                    if (method.Body.Instructions.Any(IsTranslationCall))
                    {
                        continue;
                    }

                    ILProcessor il = method.Body.GetILProcessor();
                    foreach (Instruction ret in method.Body.Instructions
                                 .Where(instruction => instruction.OpCode == OpCodes.Ret)
                                 .ToArray())
                    {
                        il.InsertBefore(ret, il.Create(OpCodes.Call, translateReference));
                    }

                    patchedMethods++;
                }

                if (patchedMethods == 0)
                {
                    throw new InvalidOperationException(
                        "Синхронные методы LocalizedString.GetLocalizedString() не найдены.");
                }

                assembly.Write(temporaryPath);
            }

            File.Move(temporaryPath, assemblyPath, overwrite: true);
        }

        private static bool IsSynchronousGetter(MethodDefinition method)
        {
            return method.Name == "GetLocalizedString"
                && method.ReturnType.MetadataType == MetadataType.String
                && method.HasBody;
        }

        private static bool IsTranslationCall(Instruction instruction)
        {
            return instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference method
                && method.Name == TranslationMethodName
                && method.DeclaringType.FullName == BootstrapTypeName;
        }
    }

    private static class AssemblyPatcher
    {
        private const string RuntimeAssemblyName = "ExarkRu.Runtime";
        private const string BootstrapTypeName = "ExarkRu.Runtime.Bootstrap";

        public static bool IsPatched(string assemblyPath)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            TypeDefinition? startup = assembly.MainModule.GetType("Startup");
            MethodDefinition? start = startup?.Methods.FirstOrDefault(method => method.Name == "Start");
            return start?.Body.Instructions.Any(IsBootstrapCall) == true;
        }

        public static void Patch(string assemblyPath, string runtimeAssemblyPath)
        {
            using AssemblyDefinition runtime = AssemblyDefinition.ReadAssembly(runtimeAssemblyPath);
            MethodDefinition initialize = runtime.MainModule.GetType(BootstrapTypeName)
                ?.Methods.FirstOrDefault(method => method.Name == "Initialize" && method.IsStatic)
                ?? throw new InvalidOperationException("ExarkRu.Runtime.Bootstrap.Initialize() не найден.");

            DefaultAssemblyResolver resolver = new();
            resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
            ReaderParameters readerParameters = new() { AssemblyResolver = resolver, ReadWrite = false };
            string temporaryPath = assemblyPath + ".exarkru.tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters))
            {
                TypeDefinition startup = assembly.MainModule.GetType("Startup")
                    ?? throw new InvalidOperationException("Тип Startup не найден в Assembly-CSharp.dll.");
                MethodDefinition start = startup.Methods.FirstOrDefault(method => method.Name == "Start")
                    ?? throw new InvalidOperationException("Startup.Start() не найден в Assembly-CSharp.dll.");

                if (start.Body.Instructions.Any(IsBootstrapCall))
                {
                    return;
                }

                AssemblyNameReference runtimeReference = assembly.MainModule.AssemblyReferences
                    .FirstOrDefault(reference => reference.Name == RuntimeAssemblyName)
                    ?? new AssemblyNameReference(RuntimeAssemblyName, runtime.Name.Version);
                if (!assembly.MainModule.AssemblyReferences.Contains(runtimeReference))
                {
                    assembly.MainModule.AssemblyReferences.Add(runtimeReference);
                }

                TypeReference bootstrap = new(
                    "ExarkRu.Runtime",
                    "Bootstrap",
                    assembly.MainModule,
                    runtimeReference);
                MethodReference initializeReference = new("Initialize", assembly.MainModule.TypeSystem.Void, bootstrap)
                {
                    HasThis = false
                };

                ILProcessor il = start.Body.GetILProcessor();
                il.InsertBefore(start.Body.Instructions[0], il.Create(OpCodes.Call, initializeReference));
                assembly.Write(temporaryPath);
            }

            File.Move(temporaryPath, assemblyPath, overwrite: true);
        }

        private static bool IsBootstrapCall(Instruction instruction)
        {
            return instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference method
                && method.Name == "Initialize"
                && method.DeclaringType.FullName == BootstrapTypeName;
        }
    }

    private static class SteamLocator
    {
        public static string? FindExarkInstall()
        {
            foreach (string steamRoot in GetSteamRoots())
            {
                string common = Path.Combine(steamRoot, "steamapps", "common", "Exark");
                if (File.Exists(Path.Combine(common, "Exark.exe")))
                {
                    return common;
                }

                string libraryFolders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFolders))
                {
                    continue;
                }

                foreach (string library in ParseLibraryPaths(File.ReadAllLines(libraryFolders)))
                {
                    string candidate = Path.Combine(library, "steamapps", "common", "Exark");
                    if (File.Exists(Path.Combine(candidate, "Exark.exe")))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> GetSteamRoots()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                yield return Path.GetFullPath(steamPath);
            }

            string defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam");
            if (Directory.Exists(defaultPath))
            {
                yield return defaultPath;
            }
        }

        private static IEnumerable<string> ParseLibraryPaths(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                Match match = Regex.Match(
                    line,
                    "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    continue;
                }

                string path = match.Groups["path"].Value.Replace("\\\\", "\\");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }
    }
}
