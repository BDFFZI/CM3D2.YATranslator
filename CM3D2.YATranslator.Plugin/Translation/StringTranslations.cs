using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CM3D2.YATranslator.Plugin.Utils;

namespace CM3D2.YATranslator.Plugin.Translation
{
    public class StringTranslations
    {
        private readonly Dictionary<Regex, string> loadedRegexTranslations;
        private readonly Dictionary<int, string> loadedStringTranslations;
        private readonly HashSet<string> translationFilePaths;

        public StringTranslations(int level)
        {
            Level = level;

            translationFilePaths = new HashSet<string>();
            loadedRegexTranslations = new Dictionary<Regex, string>();
            loadedStringTranslations = new Dictionary<int, string>(2048);
        }

        public int FileCount => translationFilePaths.Count;

        public int Level { get; }

        public int LoadedRegexCount => loadedRegexTranslations.Count;

        public int LoadedStringCount => loadedStringTranslations.Count;

        public int LoadedTranslationCount => loadedStringTranslations.Count + loadedRegexTranslations.Count;

        public bool TranslationsLoaded { get; private set; }

        public bool TryTranslate(string original, out string result)
        {
            if (!TranslationsLoaded)
                LoadTranslations();

            if (loadedStringTranslations.TryGetValue(original.ToLower().GetHashCode(), out result))
                return true;

            foreach (var regexTranslation in loadedRegexTranslations)
            {
                var m = regexTranslation.Key.Match(original);
                if (!m.Success)
                    continue;
                result = regexTranslation.Value.Template(s => {
                    string capturedString;
                    if (int.TryParse(s, out int index) && index < m.Groups.Count)
                        capturedString = m.Groups[index].Value;
                    else
                        capturedString = m.Groups[s].Value;
                    return loadedStringTranslations.TryGetValue(capturedString.ToLower().GetHashCode(), out string groupTranslation)
                        ? groupTranslation
                        : capturedString;
                });
                return true;
            }

            return false;
        }

        public void AddTranslationFile(string filePath, bool load = false)
        {
            translationFilePaths.Add(filePath);

            if (!load)
                return;

            if (LoadFromFile(filePath))
                TranslationsLoaded = true;
            else
                translationFilePaths.Remove(filePath);
        }

        public void ClearFilePaths()
        {
            translationFilePaths.Clear();
        }

        public void ClearTranslations()
        {
            loadedRegexTranslations.Clear();
            loadedStringTranslations.Clear();
            TranslationsLoaded = false;

            Logger.WriteLine(ResourceType.Strings, $"StringTranslations::Unloaded translations for level {Level}");
        }

        public bool LoadTranslations()
        {
            if (TranslationsLoaded)
                return true;

            var invalidPaths = new List<string>();
            bool loadedValidTranslations = false;
            foreach (string path in translationFilePaths)
                if (LoadFromFile(path))
                    loadedValidTranslations = true;
                else
                    invalidPaths.Add(path);

            foreach (string invalidPath in invalidPaths)
                translationFilePaths.Remove(invalidPath);

            TranslationsLoaded = loadedValidTranslations;

            if (loadedValidTranslations)
                Logger.WriteLine(ResourceType.Strings,
                    $"StringTranslations::Loaded {LoadedStringCount} Strings and {LoadedRegexCount} RegExes for level {Level}");

            return loadedValidTranslations;
        }

        private bool LoadFromFile(string filePath)
        {
            int translated = 0;

            try
            {
                using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string translationLine;
                    while ((translationLine = reader.ReadLine()) != null)
                    {
                        //遍历每一行

                        if (translationLine.StartsWith(";"))
                            continue; //跳过注释文本

                        var textParts = translationLine.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (textParts.Length < 2)
                            continue; //跳过不规范文本

                        string original = textParts[0].Unescape();
                        string translation = textParts[1].Unescape().Trim();
                        if (string.IsNullOrEmpty(translation))
                            continue; //跳过无效翻译文本

                        if (original.StartsWith("$", StringComparison.CurrentCulture))
                        {
                            loadedRegexTranslations[new Regex(original.Substring(1), RegexOptions.Compiled)] = translation;
                            translated++;
                        }
                        else
                        {
                            loadedStringTranslations[original.ToLower().GetHashCode()] = translation;
                            translated++;
                        }
                    }
                }
            }
            catch (IOException ioe)
            {
                Logger.WriteLine(LogLevel.Warning, $"Failed to load {filePath} because {ioe.Message}. Skipping file...");
                return false;
            }

            return translated != 0;
        }
    }
}
