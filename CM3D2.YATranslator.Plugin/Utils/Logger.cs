﻿using System;
using System.Collections.Generic;
using System.IO;
using CM3D2.YATranslator.Hook;
using UnityEngine;
using UnityInjector.ConsoleUtil;

namespace CM3D2.YATranslator.Plugin.Utils
{
    public enum DumpType
    {
        Strings = 0,
        Textures = 1,
        Assets = 2,
        TexSprites = 3,
        Voices = 4
    }

    public class LogLevel
    {
        public static readonly LogLevel Error = new LogLevel(ConsoleColor.Red);
        public static readonly LogLevel Minor = new LogLevel(ConsoleColor.DarkGray);
        public static readonly LogLevel Normal = new LogLevel(ConsoleColor.Gray);
        public static readonly LogLevel Warning = new LogLevel(ConsoleColor.Yellow);

        public LogLevel(ConsoleColor color)
        {
            Color = color;
        }

        public ConsoleColor Color { get; }
    }

    public static class Logger
    {
        public const string TAG = "YATranslator";
        public static string TextureNameTemplate = "{0}";
        private static readonly HashSet<int> AllowedDumpLevels;
        private static readonly HashSet<DumpType> AllowedDumpTypes;
        private static HashSet<string> cachedDumps;
        private const string DUMP_FILENAME = "TRANSLATION_DUMP";
        private static bool dumpAllLevels;
        private static bool dumpFileLoaded;

        private static readonly string[] DumpFolderNames = { "Strings", "Textures", "Assets", "Textures_Sprites", "Audio" };

        private static TextWriter dumpStream;

        static Logger()
        {
            AllowedDumpTypes = new HashSet<DumpType>();
            AllowedDumpLevels = new HashSet<int>();
        }

        public static bool DumpEnabled => AllowedDumpTypes.Count != 0 && !string.IsNullOrEmpty(DumpPath);

        public static int[] DumpLevels
        {
            set
            {
                AllowedDumpLevels.Clear();
                dumpAllLevels = false;
                foreach (int level in value)
                {
                    if (level == -1)
                    {
                        dumpAllLevels = true;
                        return;
                    }

                    AllowedDumpLevels.Add(level);
                }
            }
        }

        public static string DumpPath { get; set; }

        public static DumpType[] DumpTypes
        {
            set
            {
                AllowedDumpTypes.Clear();
                foreach (var dumpType in value)
                    AllowedDumpTypes.Add(dumpType);
            }
        }

        public static ResourceType Verbosity { get; set; }

        public static bool CanDump(DumpType type)
        {
            return AllowedDumpTypes.Contains(type);
        }

        public static bool InitDump()
        {
            if (!DumpEnabled)
                return false;
            if (dumpFileLoaded)
                return true;
            WriteLine("Trying to initialize dumping");
            try
            {
                if (!Directory.Exists(DumpPath))
                    Directory.CreateDirectory(DumpPath);
                for (int index = 0; index < DumpFolderNames.Length; index++)
                {
                    string folderPath = Path.Combine(DumpPath, DumpFolderNames[index]);
                    if (!Directory.Exists(folderPath))
                        Directory.CreateDirectory(folderPath);
                    DumpFolderNames[index] = folderPath;
                }
            }
            catch (Exception e)
            {
                WriteLine(LogLevel.Error, $"Failed to create dump directory because {e.Message}");
                return false;
            }

            string dumpFilePath =
                Path.Combine(GetDumpFolderName(DumpType.Strings), $"{DUMP_FILENAME}.{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");
            try
            {
                dumpStream = File.CreateText(dumpFilePath);
                dumpFileLoaded = true;
                cachedDumps = new HashSet<string>();
            }
            catch (Exception e)
            {
                WriteLine(LogLevel.Error, $"Failed to create dump to {dumpFilePath}. Reason: {e.Message}");
                return false;
            }

            return true;
        }

        public static void Dispose()
        {
            if (!dumpFileLoaded)
                return;

            dumpStream.Flush();
            dumpStream.Dispose();
        }

        public static void DumpLine(string line, int level, DumpType dumpType = DumpType.Strings)
        {
            if (!CanDump(dumpType) || !InitDump())
                return;
            if (!dumpAllLevels && !AllowedDumpLevels.Contains(level))
                return;
            if (dumpType == DumpType.Strings)
                line = line.Replace("\n", "").Trim().Escape();
            if (cachedDumps.Contains(line))
                return;
            cachedDumps.Add(line);
            lock (dumpStream)
            {
                dumpStream.WriteLine($"[{dumpType}][LEVEL {level}] {line}");
                dumpStream.Flush();
            }
        }

        public static void WriteLine(ResourceType verbosity, LogLevel logLevel, string message)
        {
            if (IsLogging(verbosity))
                WriteLine(logLevel, message);
        }

        public static void WriteLine(ResourceType verbosity, string message)
        {
            WriteLine(verbosity, LogLevel.Normal, message);
        }

        public static bool IsLogging(ResourceType verbosity)
        {
            return (verbosity & Verbosity) != 0;
        }

        public static void WriteLine(LogLevel logLevel, string message)
        {
            var oldColor = SafeConsole.ForegroundColor;
            SafeConsole.ForegroundColor = logLevel.Color;
            Console.WriteLine($"{TAG}::{message}");
            SafeConsole.ForegroundColor = oldColor;
        }

        public static void WriteLine(string message)
        {
            WriteLine(LogLevel.Normal, message);
        }

        public static void DumpTexture(DumpType dumpType, TextureTranslationEventArgs args, bool duplicate, int level)
        {
            var texture = args.OriginalTexture ?? args.Data.CreateTexture2D();
            if (!CanDump(dumpType) || texture == null || !InitDump())
                return;
            if (!dumpAllLevels && !AllowedDumpLevels.Contains(level))
                return;

            if (cachedDumps.Contains(args.Name))
                return;
            cachedDumps.Add(args.Name);

            if (args.Name.StartsWith("!"))
                return;

            string fileName = string.Format(TextureNameTemplate, args.Name, args.CompoundHash, args.Meta, level);

            string path = Path.Combine(GetDumpFolderName(dumpType), $"{fileName}.png");
            if (File.Exists(path))
                return;

            var tex = duplicate || texture.format == TextureFormat.DXT1 || texture.format == TextureFormat.DXT5
                ? Duplicate(texture)
                : texture;
            WriteLine($"Dumping {fileName}.png");
            using (var fs = File.Create(path))
            {
                var pngData = tex.EncodeToPNG();
                fs.Write(pngData, 0, pngData.Length);
                fs.Flush();
            }
        }

        public static void DumpVoice(string name, AudioClip clip, int level)
        {
            if (!CanDump(DumpType.Voices) || clip == null || !InitDump())
                return;
            if (!dumpAllLevels && !AllowedDumpLevels.Contains(level))
                return;

            if (cachedDumps.Contains(name))
                return;
            cachedDumps.Add(name);

            string path = Path.Combine(GetDumpFolderName(DumpType.Voices), $"{name}.wav");
            if (File.Exists(path))
                return;

            WriteLine($"Dumping {name}.wav");

            using (var fs = File.Create(path))
            {
                var wavData = clip.ToWavData();
                fs.Write(wavData, 0, wavData.Length);
                fs.Flush();
            }
        }

        private static string GetDumpFolderName(DumpType dumpType)
        {
            return DumpFolderNames[(int)dumpType];
        }

        private static Texture2D Duplicate(Texture texture)
        {
            WriteLine($"Duplicating texture {texture.name}");
            var render = RenderTexture.GetTemporary(texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, render);
            var previous = RenderTexture.active;
            RenderTexture.active = render;
            var result = new Texture2D(texture.width, texture.height);
            result.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(render);
            return result;
        }
    }
}
