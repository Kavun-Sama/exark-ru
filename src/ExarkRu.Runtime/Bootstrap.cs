using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ExarkRu.Runtime
{
    public static class Bootstrap
    {
        private const string LocaleCode = "ru";
        private const string LocaleName = "Русский (RU)";
        private const string PersistKey = "exark-ru-selected";
        private const string TranslationFileName = "translations.ru.json";

        private static bool initialized;
        private static Locale englishLocale;
        private static Locale russianLocale;
        private static RussianTablePostprocessor tablePostprocessor;
        private static ITablePostprocessor originalTablePostprocessor;
        private static bool selectingBuiltInLocale;

        public static Locale ResolveSelectedLocale(Locale requested)
        {
            return !selectingBuiltInLocale && russianLocale != null
                && PlayerPrefs.GetInt(PersistKey, 0) == 1
                ? russianLocale
                : requested;
        }

        internal static void SelectBuiltInLocale(Action select)
        {
            selectingBuiltInLocale = true;
            try
            {
                PrepareBuiltInLocaleSelection();
                select();
            }
            finally
            {
                selectingBuiltInLocale = false;
            }
        }

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            try
            {
                AsyncOperationHandle<LocalizationSettings> operation = LocalizationSettings.InitializationOperation;
                if (operation.IsDone)
                {
                    OnLocalizationInitialized(operation);
                }
                else
                {
                    operation.Completed += OnLocalizationInitialized;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[Exark RU] Failed to initialize localization patch.");
                Debug.LogException(exception);
            }
        }

        public static string TranslateVisibleText(string source)
        {
            if (selectingBuiltInLocale
                || LocalizationSettings.SelectedLocale?.Identifier.Code != LocaleCode
                || tablePostprocessor == null
                || string.IsNullOrEmpty(source)
                || !tablePostprocessor.TryTranslateVisibleText(source, out string translated))
            {
                return source;
            }

            return translated;
        }

        public static StringBuilder TranslateVisibleTextBuilder(StringBuilder source)
        {
            if (source == null)
            {
                return null;
            }

            string original = source.ToString();
            string translated = TranslateVisibleText(original);
            return string.Equals(original, translated, StringComparison.Ordinal)
                ? source
                : new StringBuilder(translated);
        }

        internal static bool ContainsCyrillic(string value)
        {
            foreach (char character in value)
            {
                if ((character >= '\u0400' && character <= '\u052f')
                    || (character >= '\u2de0' && character <= '\u2dff')
                    || (character >= '\ua640' && character <= '\ua69f'))
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnLocalizationInitialized(AsyncOperationHandle<LocalizationSettings> operation)
        {
            if (operation.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError("[Exark RU] Unity Localization initialization failed.");
                return;
            }

            try
            {
                TranslationCatalog catalog = TranslationCatalog.Load(GetTranslationFilePath());
                if (catalog.EntryCount == 0)
                {
                    Debug.LogError("[Exark RU] Translation catalog is empty; Russian locale will not be added.");
                    return;
                }

                englishLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
                if (englishLocale == null)
                {
                    Debug.LogError("[Exark RU] English locale was not found; Russian fallback cannot be installed.");
                    return;
                }

                russianLocale = LocalizationSettings.AvailableLocales.GetLocale(LocaleCode);
                if (russianLocale == null || russianLocale.Identifier.Code != LocaleCode)
                {
                    russianLocale = Locale.CreateLocale(LocaleCode);
                    russianLocale.name = LocaleName;
                    russianLocale.LocaleName = LocaleName;
                    russianLocale.SortOrder = 6;
                    russianLocale.Metadata.AddMetadata(new FallbackLocale(englishLocale));
                    LocalizationSettings.AvailableLocales.AddLocale(russianLocale);
                }

                originalTablePostprocessor = LocalizationSettings.StringDatabase.TablePostprocessor;
                tablePostprocessor = new RussianTablePostprocessor(catalog);
                LocalizationSettings.StringDatabase.UseFallback = true;
                LocalizationSettings.AssetDatabase.UseFallback = true;
                LocalizationSettings.StringDatabase.TablePostprocessor =
                    new CompositeTablePostprocessor(originalTablePostprocessor, tablePostprocessor);

                InstallCyrillicFontFallback();
                LanguageDropdownBridge.Install(russianLocale);
                CachedUiTranslationBridge.Install(tablePostprocessor);
                LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;

                if (PlayerPrefs.GetInt(PersistKey, 0) == 1)
                {
                    SelectRussianLocale();
                }

                Debug.Log($"[Exark RU] Loaded {catalog.EntryCount} translated strings.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[Exark RU] Failed to install Russian locale.");
                Debug.LogException(exception);
            }
        }

        private static void OnSelectedLocaleChanged(Locale locale)
        {
            bool isRussian = locale != null && locale.Identifier.Code == LocaleCode;

            if (isRussian)
            {
                PlayerPrefs.SetInt(PersistKey, 1);
            }
            else
            {
                PlayerPrefs.DeleteKey(PersistKey);
                CachedUiTranslationBridge.RestoreAll();
                tablePostprocessor?.RestoreAll();
            }

            PlayerPrefs.Save();
            Debug.Log($"[Exark RU] Selected locale changed to '{locale?.Identifier.Code ?? "<null>"}'.");
        }

        internal static void SelectRussianLocale()
        {
            if (russianLocale == null || englishLocale == null || tablePostprocessor == null)
            {
                return;
            }

            tablePostprocessor.ApplyAll(englishLocale);
            LocalizationSettings.SelectedLocale = russianLocale;
            CachedUiTranslationBridge.RefreshAll();
        }

        internal static void PrepareBuiltInLocaleSelection()
        {
            if (LocalizationSettings.SelectedLocale?.Identifier.Code == LocaleCode)
            {
                CachedUiTranslationBridge.RestoreAll();
                tablePostprocessor?.RestoreAll();
            }
        }

        private static void InstallCyrillicFontFallback()
        {
            TMP_FontAsset fallback = TMP_FontAsset.CreateFontAsset("Segoe UI", "Regular", 90);
            if (fallback == null)
            {
                Debug.LogWarning("[Exark RU] Segoe UI was not found. Cyrillic glyphs may be missing.");
                return;
            }

            fallback.name = "Exark RU - Segoe UI Fallback";
            fallback.isMultiAtlasTexturesEnabled = true;

            List<TMP_FontAsset> fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks == null)
            {
                fallbacks = new List<TMP_FontAsset>();
                TMP_Settings.fallbackFontAssets = fallbacks;
            }

            if (!fallbacks.Contains(fallback))
            {
                fallbacks.Insert(0, fallback);
            }
        }

        private static string GetTranslationFilePath()
        {
            string directory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
            return Path.Combine(directory ?? string.Empty, TranslationFileName);
        }
    }

    internal static class CachedUiTranslationBridge
    {
        private const float TextScanInterval = 0.25f;

        private sealed class PatchedText
        {
            public string Original { get; set; }

            public string Applied { get; set; }
        }

        private static readonly Dictionary<TMP_Text, PatchedText> patchedTexts =
            new Dictionary<TMP_Text, PatchedText>();
        private static readonly Dictionary<TMP_Text, string> observedTexts =
            new Dictionary<TMP_Text, string>();
        private static readonly Dictionary<Text, PatchedText> patchedLegacyTexts =
            new Dictionary<Text, PatchedText>();
        private static readonly Dictionary<Text, string> observedLegacyTexts =
            new Dictionary<Text, string>();

        private static RussianTablePostprocessor tablePostprocessor;
        private static bool applyingTranslation;
        private static bool installed;
        private static float nextTextScanTime;

        public static void Install(RussianTablePostprocessor postprocessor)
        {
            tablePostprocessor = postprocessor;
            if (installed)
            {
                return;
            }

            installed = true;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
        }

        public static void RefreshAll()
        {
            RefreshExistingTexts();
        }

        public static void RestoreAll()
        {
            RestorePatchedTexts();
        }

        private static void OnWillRenderCanvases()
        {
            if (applyingTranslation
                || LocalizationSettings.SelectedLocale?.Identifier.Code != "ru"
                || Time.unscaledTime < nextTextScanTime)
            {
                return;
            }

            nextTextScanTime = Time.unscaledTime + TextScanInterval;
            RefreshTmpTexts();
            RefreshLegacyTexts();
        }

        private static void RefreshExistingTexts()
        {
            if (LocalizationSettings.SelectedLocale?.Identifier.Code != "ru")
            {
                return;
            }

            RefreshTmpTexts();
            RefreshLegacyTexts();
        }

        private static void RefreshTmpTexts()
        {
            RemoveDestroyedTexts();

            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null)
                {
                    continue;
                }

                string current = text.text;
                if (observedTexts.TryGetValue(text, out string observed)
                    && string.Equals(current, observed, StringComparison.Ordinal))
                {
                    continue;
                }

                observedTexts[text] = current;
                ApplyTranslation(text);
            }
        }

        private static bool IsTooltipText(TMP_Text text)
        {
            Transform current = text.transform;
            while (current != null)
            {
                if (current.name.StartsWith("Tooltip", StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void ApplyTranslation(TMP_Text text)
        {
            if (text == null
                || tablePostprocessor == null
                || string.IsNullOrEmpty(text.text)
                || !tablePostprocessor.TryTranslateVisibleText(text.text, out string translated)
                || string.Equals(text.text, translated, StringComparison.Ordinal))
            {
                return;
            }

            patchedTexts[text] = new PatchedText
            {
                Original = text.text,
                Applied = translated
            };

            applyingTranslation = true;
            try
            {
                text.text = translated;
                observedTexts[text] = translated;
                if (IsTooltipText(text))
                {
                    text.SetAllDirty();
                    text.ForceMeshUpdate();
                }
            }
            finally
            {
                applyingTranslation = false;
            }
        }

        private static void RefreshLegacyTexts()
        {
            RemoveDestroyedLegacyTexts();

            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || !text.gameObject.scene.IsValid())
                {
                    continue;
                }

                string current = text.text;
                if (observedLegacyTexts.TryGetValue(text, out string observed)
                    && string.Equals(current, observed, StringComparison.Ordinal))
                {
                    continue;
                }

                observedLegacyTexts[text] = current;
                ApplyTranslation(text);
            }
        }

        private static void ApplyTranslation(Text text)
        {
            if (text == null
                || tablePostprocessor == null
                || string.IsNullOrEmpty(text.text)
                || !tablePostprocessor.TryTranslateVisibleText(text.text, out string translated)
                || string.Equals(text.text, translated, StringComparison.Ordinal))
            {
                return;
            }

            patchedLegacyTexts[text] = new PatchedText
            {
                Original = text.text,
                Applied = translated
            };

            applyingTranslation = true;
            try
            {
                text.text = translated;
                observedLegacyTexts[text] = translated;
            }
            finally
            {
                applyingTranslation = false;
            }
        }

        private static void RestorePatchedTexts()
        {
            applyingTranslation = true;
            try
            {
                foreach (KeyValuePair<TMP_Text, PatchedText> item in patchedTexts)
                {
                    TMP_Text text = item.Key;
                    PatchedText patch = item.Value;
                    if (text != null && string.Equals(text.text, patch.Applied, StringComparison.Ordinal))
                    {
                        text.text = patch.Original;
                    }
                }

                foreach (KeyValuePair<Text, PatchedText> item in patchedLegacyTexts)
                {
                    Text text = item.Key;
                    PatchedText patch = item.Value;
                    if (text != null && string.Equals(text.text, patch.Applied, StringComparison.Ordinal))
                    {
                        text.text = patch.Original;
                    }
                }
            }
            finally
            {
                patchedTexts.Clear();
                observedTexts.Clear();
                patchedLegacyTexts.Clear();
                observedLegacyTexts.Clear();
                applyingTranslation = false;
            }
        }

        private static void RemoveDestroyedTexts()
        {
            List<TMP_Text> destroyed = null;
            foreach (TMP_Text text in observedTexts.Keys)
            {
                if (text != null)
                {
                    continue;
                }

                if (destroyed == null)
                {
                    destroyed = new List<TMP_Text>();
                }

                destroyed.Add(text);
            }

            if (destroyed == null)
            {
                return;
            }

            foreach (TMP_Text text in destroyed)
            {
                observedTexts.Remove(text);
                patchedTexts.Remove(text);
            }
        }

        private static void RemoveDestroyedLegacyTexts()
        {
            List<Text> destroyed = null;
            foreach (Text text in observedLegacyTexts.Keys)
            {
                if (text != null)
                {
                    continue;
                }

                if (destroyed == null)
                {
                    destroyed = new List<Text>();
                }

                destroyed.Add(text);
            }

            if (destroyed == null)
            {
                return;
            }

            foreach (Text text in destroyed)
            {
                observedLegacyTexts.Remove(text);
                patchedLegacyTexts.Remove(text);
            }
        }
    }

    internal sealed class LanguageDropdownBridge : MonoBehaviour
    {
        private const string RussianOption = "Русский (RU)";
        private static readonly string[] BuiltInLanguageCodes =
        {
            "(EN)",
            "(FR)",
            "(DE)",
            "(ZH)",
            "(JP)",
            "(KR)"
        };

        private readonly List<TMP_Dropdown> hookedDropdowns = new List<TMP_Dropdown>();
        private Locale russianLocale;
        private float nextScanTime;

        public static void Install(Locale locale)
        {
            GameObject host = new GameObject("Exark RU - Language Bridge");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;

            LanguageDropdownBridge bridge = host.AddComponent<LanguageDropdownBridge>();
            bridge.russianLocale = locale;
            bridge.ScanForLanguageDropdowns();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextScanTime)
            {
                return;
            }

            nextScanTime = Time.unscaledTime + 0.5f;
            hookedDropdowns.RemoveAll(dropdown => dropdown == null);
            ScanForLanguageDropdowns();
        }

        private void ScanForLanguageDropdowns()
        {
            foreach (TMP_Dropdown dropdown in Resources.FindObjectsOfTypeAll<TMP_Dropdown>())
            {
                if (dropdown == null || !LooksLikeLanguageDropdown(dropdown))
                {
                    continue;
                }

                int russianIndex = EnsureRussianOption(dropdown);
                if (!hookedDropdowns.Contains(dropdown))
                {
                    TMP_Dropdown capturedDropdown = dropdown;
                    TMP_Dropdown.DropdownEvent originalEvent = dropdown.onValueChanged;
                    TMP_Dropdown.DropdownEvent bridgeEvent = new TMP_Dropdown.DropdownEvent();
                    bridgeEvent.AddListener(
                        index => OnLanguageChanged(capturedDropdown, originalEvent, index));
                    dropdown.onValueChanged = bridgeEvent;
                    hookedDropdowns.Add(dropdown);
                    Debug.Log($"[Exark RU] Added Russian language option to '{dropdown.gameObject.name}'.");
                }

                if (LocalizationSettings.SelectedLocale?.Identifier.Code == "ru"
                    && dropdown.value != russianIndex)
                {
                    dropdown.SetValueWithoutNotify(russianIndex);
                    dropdown.RefreshShownValue();
                }
            }
        }

        private static bool LooksLikeLanguageDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown.options == null || dropdown.options.Count < BuiltInLanguageCodes.Length)
            {
                return false;
            }

            for (int i = 0; i < BuiltInLanguageCodes.Length; i++)
            {
                string text = dropdown.options[i]?.text;
                if (string.IsNullOrEmpty(text)
                    || !text.EndsWith(BuiltInLanguageCodes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static int EnsureRussianOption(TMP_Dropdown dropdown)
        {
            for (int i = 0; i < dropdown.options.Count; i++)
            {
                if (string.Equals(dropdown.options[i]?.text, RussianOption, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            dropdown.options.Add(new TMP_Dropdown.OptionData(RussianOption));
            dropdown.RefreshShownValue();
            return dropdown.options.Count - 1;
        }

        private void OnLanguageChanged(
            TMP_Dropdown dropdown,
            TMP_Dropdown.DropdownEvent originalEvent,
            int index)
        {
            if (dropdown == null || index < 0 || index >= dropdown.options.Count)
            {
                return;
            }

            if (string.Equals(dropdown.options[index]?.text, RussianOption, StringComparison.Ordinal))
            {
                Bootstrap.SelectRussianLocale();
                return;
            }

            Bootstrap.SelectBuiltInLocale(() => originalEvent?.Invoke(index));
        }
    }

    internal sealed class CompositeTablePostprocessor : ITablePostprocessor
    {
        private readonly ITablePostprocessor original;
        private readonly RussianTablePostprocessor russian;

        public CompositeTablePostprocessor(
            ITablePostprocessor original,
            RussianTablePostprocessor russian)
        {
            this.original = original;
            this.russian = russian;
        }

        public void PostprocessTable(LocalizationTable table)
        {
            original?.PostprocessTable(table);
            russian.PostprocessTable(table);
        }
    }

    internal sealed class RussianTablePostprocessor : ITablePostprocessor
    {
        private readonly TranslationCatalog catalog;
        private readonly VisibleTextTranslator visibleTextTranslator = new VisibleTextTranslator();
        private readonly TooltipTextFormatter tooltipTextFormatter;
        private readonly Dictionary<StringTable, Dictionary<long, string>> originals =
            new Dictionary<StringTable, Dictionary<long, string>>();

        public RussianTablePostprocessor(TranslationCatalog catalog)
        {
            this.catalog = catalog;
            if (!catalog.TryGetTable("TextUI", out Dictionary<long, string> ui)
                || !ui.TryGetValue(229009789026787329, out string triggered)
                || !ui.TryGetValue(229009789026787330, out string passive))
            {
                throw new InvalidDataException("Tooltip effect labels are missing from the Russian catalog.");
            }

            tooltipTextFormatter = new TooltipTextFormatter(triggered, passive);
        }

        public void PostprocessTable(LocalizationTable table)
        {
            StringTable stringTable = table as StringTable;
            if (stringTable == null
                || stringTable.LocaleIdentifier.Code != "en"
                || LocalizationSettings.SelectedLocale?.Identifier.Code != "ru")
            {
                return;
            }

            Dictionary<long, string> translations;
            if (!catalog.TryGetTable(stringTable.TableCollectionName, out translations))
            {
                return;
            }

            ApplyTable(stringTable, translations);
        }

        public void ApplyAll(Locale englishLocale)
        {
            int appliedTables = 0;
            int appliedStrings = 0;

            foreach (string tableName in catalog.TableNames)
            {
                AsyncOperationHandle<StringTable> operation =
                    LocalizationSettings.StringDatabase.GetTableAsync(tableName, englishLocale);
                StringTable stringTable = operation.IsDone
                    ? operation.Result
                    : operation.WaitForCompletion();
                if (stringTable == null || !catalog.TryGetTable(tableName, out Dictionary<long, string> translations))
                {
                    continue;
                }

                appliedStrings += ApplyTable(stringTable, translations);
                appliedTables++;
            }

            Debug.Log(
                $"[Exark RU] Applied {appliedStrings} strings across {appliedTables} loaded tables.");
        }

        private int ApplyTable(StringTable stringTable, Dictionary<long, string> translations)
        {
            if (originals.ContainsKey(stringTable))
            {
                return 0;
            }

            Dictionary<long, string> originalValues = new Dictionary<long, string>();

            foreach (KeyValuePair<long, string> item in translations)
            {
                StringTableEntry entry = stringTable.GetEntry(item.Key);
                if (entry == null)
                {
                    continue;
                }

                originalValues[item.Key] = entry.Value;
                visibleTextTranslator.Register(entry.Value, item.Value);
                entry.Value = tooltipTextFormatter.Format(item.Value);
            }

            originals[stringTable] = originalValues;
            Debug.Log(
                $"[Exark RU] Applied {originalValues.Count} strings to '{stringTable.TableCollectionName}'.");
            return originalValues.Count;
        }

        public bool TryTranslateVisibleText(string source, out string translated)
        {
            translated = source;
            bool found = !string.IsNullOrEmpty(source) && !Bootstrap.ContainsCyrillic(source)
                && visibleTextTranslator.TryTranslate(source, out translated);
            translated = tooltipTextFormatter.Format(found ? translated : source);
            return found || !string.Equals(source, translated, StringComparison.Ordinal);
        }

        public void RestoreAll()
        {
            foreach (KeyValuePair<StringTable, Dictionary<long, string>> tableItem in originals)
            {
                StringTable table = tableItem.Key;
                if (table == null)
                {
                    continue;
                }

                foreach (KeyValuePair<long, string> entryItem in tableItem.Value)
                {
                    StringTableEntry entry = table.GetEntry(entryItem.Key);
                    if (entry != null)
                    {
                        entry.Value = entryItem.Value;
                    }
                }
            }

            originals.Clear();
        }
    }

    internal sealed class VisibleTextTranslator
    {
        private static readonly Regex NumericPlaceholder = new Regex(
            @"\{(?<index>\d+)(?<format>:[^{}]+)?\}",
            RegexOptions.CultureInvariant);

        private readonly Dictionary<string, string> exactTranslations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguousExactSources =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, FormatTranslation> formatTranslations =
            new Dictionary<string, FormatTranslation>(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguousFormatSources =
            new HashSet<string>(StringComparer.Ordinal);

        public void Register(string source, string translation)
        {
            if (string.IsNullOrEmpty(source)
                || string.IsNullOrEmpty(translation)
                || string.Equals(source, translation, StringComparison.Ordinal))
            {
                return;
            }

            RegisterExact(source, translation);

            if (!NumericPlaceholder.IsMatch(source))
            {
                return;
            }

            if (formatTranslations.TryGetValue(source, out FormatTranslation existing))
            {
                if (!string.Equals(existing.TranslationTemplate, translation, StringComparison.Ordinal))
                {
                    formatTranslations.Remove(source);
                    ambiguousFormatSources.Add(source);
                }

                return;
            }

            if (!ambiguousFormatSources.Contains(source))
            {
                formatTranslations[source] = new FormatTranslation(source, translation);
            }
        }

        public bool TryTranslate(string source, out string translated)
        {
            if (exactTranslations.TryGetValue(source, out translated))
            {
                return true;
            }

            foreach (FormatTranslation translation in formatTranslations.Values)
            {
                if (translation.TryTranslate(source, out translated))
                {
                    return true;
                }
            }

            translated = null;
            return false;
        }

        private void RegisterExact(string source, string translation)
        {
            if (exactTranslations.TryGetValue(source, out string existing))
            {
                if (!string.Equals(existing, translation, StringComparison.Ordinal))
                {
                    exactTranslations.Remove(source);
                    ambiguousExactSources.Add(source);
                }

                return;
            }

            if (!ambiguousExactSources.Contains(source))
            {
                exactTranslations[source] = translation;
            }
        }

        private sealed class FormatTranslation
        {
            private readonly Regex sourcePattern;

            public FormatTranslation(string sourceTemplate, string translationTemplate)
            {
                TranslationTemplate = translationTemplate;
                sourcePattern = new Regex(
                    BuildPattern(sourceTemplate),
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
            }

            public string TranslationTemplate { get; }

            public bool TryTranslate(string source, out string translated)
            {
                Match match = sourcePattern.Match(source);
                if (!match.Success)
                {
                    translated = null;
                    return false;
                }

                translated = NumericPlaceholder.Replace(
                    TranslationTemplate,
                    placeholder =>
                    {
                        string groupName = "arg" + placeholder.Groups["index"].Value;
                        Group group = match.Groups[groupName];
                        return group.Success ? group.Value : placeholder.Value;
                    });
                return true;
            }

            private static string BuildPattern(string template)
            {
                MatchCollection placeholders = NumericPlaceholder.Matches(template);
                StringBuilder pattern = new StringBuilder("^");
                HashSet<string> captured = new HashSet<string>(StringComparer.Ordinal);
                int offset = 0;

                foreach (Match placeholder in placeholders)
                {
                    pattern.Append(Regex.Escape(template.Substring(offset, placeholder.Index - offset)));

                    string index = placeholder.Groups["index"].Value;
                    string groupName = "arg" + index;
                    if (captured.Add(groupName))
                    {
                        pattern.Append("(?<").Append(groupName).Append(">.*?)");
                    }
                    else
                    {
                        pattern.Append("\\k<").Append(groupName).Append('>');
                    }

                    offset = placeholder.Index + placeholder.Length;
                }

                pattern.Append(Regex.Escape(template.Substring(offset)));
                pattern.Append('$');
                return pattern.ToString();
            }
        }
    }

    internal sealed class TranslationCatalog
    {
        private readonly Dictionary<string, Dictionary<long, string>> tables;

        private TranslationCatalog(Dictionary<string, Dictionary<long, string>> tables)
        {
            this.tables = tables;
            int count = 0;
            foreach (Dictionary<long, string> table in tables.Values)
            {
                count += table.Count;
            }

            EntryCount = count;
        }

        public int EntryCount { get; }

        public IEnumerable<string> TableNames => tables.Keys;

        public static TranslationCatalog Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Russian translation catalog was not found.", path);
            }

            string json = File.ReadAllText(path);
            Dictionary<string, Dictionary<long, string>> data =
                JsonConvert.DeserializeObject<Dictionary<string, Dictionary<long, string>>>(json);

            if (data == null)
            {
                throw new InvalidDataException("Russian translation catalog is invalid.");
            }

            return new TranslationCatalog(data);
        }

        public bool TryGetTable(string tableName, out Dictionary<long, string> translations)
        {
            return tables.TryGetValue(tableName, out translations);
        }
    }
}
