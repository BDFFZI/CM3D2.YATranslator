﻿using System;
using System.Collections.Generic;
using System.IO;
using CM3D2.YATranslator.Hook;
using CM3D2.YATranslator.Plugin.Features;
using CM3D2.YATranslator.Plugin.Translation;
using CM3D2.YATranslator.Plugin.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityInjector;
using UnityInjector.Attributes;
using Logger = CM3D2.YATranslator.Plugin.Utils.Logger;

namespace CM3D2.YATranslator.Plugin
{
    [PluginName("Yet Another Translator")]
    public class YATranslator : PluginBase
    {
        private const string TEMPLATE_STRING_PREFIX = "\u200B";

        private bool isRetranslating;

        private string lastFoundAsset;
        private string lastFoundTexture;
        private string lastLoadedAsset;
        private string lastLoadedTexture;

        private Action<UILabel> processAndRequest;

        public PluginConfiguration Settings { get; private set; }

        private Clipboard Clipboard { get; set; }

        private int CurrentLevel { get; set; }

        private TranslationMemory Memory { get; set; }

        private Subtitles Subtitles { get; set; }

        public void Awake()
        {
            DontDestroyOnLoad(this);

            var processAndRequestMethod = typeof(UILabel).GetMethod("ProcessAndRequest");
            processAndRequest = label => processAndRequestMethod?.Invoke(label, null);

            Memory = new TranslationMemory(DataPath);
            Clipboard = gameObject.AddComponent<Clipboard>();
            Subtitles = gameObject.AddComponent<Subtitles>();

            InitConfig();

            Memory.LoadTranslations();

            TranslationHooks.TranslateText += OnTranslateString;
            TranslationHooks.AssetTextureLoad += OnAssetTextureLoad;
            TranslationHooks.ArcTextureLoad += OnTextureLoad;
            TranslationHooks.SpriteLoad += OnTextureLoad;
            TranslationHooks.ArcTextureLoaded += OnArcTextureLoaded;
            TranslationHooks.TranslateGraphic += OnTranslateGraphic;
            TranslationHooks.PlaySound += OnPlaySound;
            TranslationHooks.GetOppositePair += OnGetOppositePair;
            TranslationHooks.GetOriginalText += OnGetOriginalText;
            Logger.WriteLine("Hooking complete");
        }

        public void OnLevelWasLoaded(int level)
        {
            CurrentLevel = level;
            Memory.ActivateLevelTranslations(level);
            TranslateExisting(true);
        }

        public void Update()
        {
            if (Input.GetKeyDown(Settings.ReloadTranslationsKeyCode))
            {
                Logger.WriteLine("Reloading config");
                ReloadConfig();
                InitConfig();

                if (Settings.EnableTranslationReload)
                {
                    Logger.WriteLine("Reloading translations");
                    Memory.LoadTranslations();
                    Memory.ActivateLevelTranslations(CurrentLevel, false);
                    TranslateExisting();
                }
            }
        }

        public void OnDestroy()
        {
            Logger.WriteLine("Removing hooks");
            TranslationHooks.TranslateText -= OnTranslateString;
            TranslationHooks.AssetTextureLoad -= OnAssetTextureLoad;
            TranslationHooks.ArcTextureLoad -= OnTextureLoad;
            TranslationHooks.SpriteLoad -= OnTextureLoad;
            TranslationHooks.ArcTextureLoaded -= OnArcTextureLoaded;
            TranslationHooks.TranslateGraphic -= OnTranslateGraphic;
            TranslationHooks.PlaySound -= OnPlaySound;
            TranslationHooks.GetOppositePair -= OnGetOppositePair;
            TranslationHooks.GetOriginalText -= OnGetOriginalText;

            Destroy(Subtitles);
            Destroy(Clipboard);

            Logger.Dispose();
        }

        private void OnGetOriginalText(object sender, StringTranslationEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            if (Memory.TryGetOriginal(e.Text, out string original))
                e.Translation = original;
        }

        private void OnGetOppositePair(object sender, StringTranslationEventArgs e)
        {
            string text = e.Text;
            if (string.IsNullOrEmpty(text))
                return;
            e.Translation = Memory.TryGetOriginal(text, out string original) ? original : Memory.GetTextTranslation(text).Text;
        }

        private void OnPlaySound(object sender, SoundEventArgs e)
        {
            if (!Settings.Subtitles.Enable || e.AudioSourceMgr.SoundType < AudioSourceMgr.Type.Voice)
                return;

            Logger.WriteLine(ResourceType.Voices, $"Voices {e.AudioSourceMgr.FileName}");

            Subtitles.DisplayFor(e.AudioSourceMgr);
        }

        private void InitConfig()
        {
            Settings = ConfigurationLoader.LoadConfig<PluginConfiguration>(Preferences);
            SaveConfig();
            Memory.OptimizationFlags = Settings.OptimizationFlags;
            Memory.LoadResource = Settings.LoadResourceTypes;
            Memory.RetranslateText = Settings.EnableTranslationReload;
            Clipboard.Configuration = Settings.Clipboard;
            Subtitles.Configuration = Settings.Subtitles;
            Logger.DumpPath = Path.Combine(DataPath, "TranslationDumps");
        }

        private void OnTranslateGraphic(object sender, GraphicTranslationEventArgs e)
        {
            if (e.Graphic == null)
                return;

            switch (e.Graphic)
            {
                case Image img:
                    if (img.sprite == null)
                        return;
                    var currentSprite = img.sprite;
                    img.sprite = currentSprite;
                    break;
                case Text text:
                    if (text.text == null)
                        return;
                    string str = text.text;
                    text.text = str;
                    break;
            }
        }

        private void OnArcTextureLoaded(object sender, TextureTranslationEventArgs e)
        {
            if (Logger.CanDump(DumpType.Textures))
                Logger.DumpTexture(DumpType.Textures, e, false, CurrentLevel);
        }

        private void OnAssetTextureLoad(object sender, TextureTranslationEventArgs e)
        {
            if (lastFoundAsset != e.Name)
            {
                lastFoundAsset = e.Name;
                Logger.WriteLine(ResourceType.Assets, LogLevel.Minor, $"FindAsset::{e.Name} [{e.Meta}::{e.CompoundHash}]");
            }

            string[] namePossibilities = {
                e.CompoundHash + "@" + SceneManager.GetActiveScene().buildIndex,
                e.Name + "@" + SceneManager.GetActiveScene().buildIndex, e.CompoundHash, e.Name
            };

            foreach (string assetName in namePossibilities)
            {
                if (lastFoundAsset != assetName)
                {
                    lastFoundAsset = assetName;
                    Logger.WriteLine(ResourceType.Assets, LogLevel.Minor, $"TryFindAsset::{assetName}");
                }

                string assetPath = Memory.GetAssetPath(assetName);

                if (assetPath == null)
                    continue;
                if (lastLoadedAsset != assetName)
                    Logger.WriteLine(ResourceType.Assets, $"LoadAsset::{assetName}");
                lastLoadedAsset = assetName;

                e.Data = new TextureResource(1, 1, TextureFormat.ARGB32, null, File.ReadAllBytes(assetPath));
                return;
            }

            Logger.DumpTexture(DumpType.Assets, e, true, CurrentLevel);
        }

        private void OnTranslateString(object sender, StringTranslationEventArgs e)
        {
            string inputText = e.Text;
            if (string.IsNullOrEmpty(inputText))
                return;

            if (inputText.StartsWith(TEMPLATE_STRING_PREFIX))
            {
                if (!isRetranslating)
                {
                    e.Translation = inputText;
                    return;
                }

                inputText = inputText.Substring(1);
            }

            bool isAudioClipName = inputText.StartsWith(Subtitles.AUDIOCLIP_PREFIX);
            if (isAudioClipName)
                inputText = inputText.Substring(Subtitles.AUDIOCLIP_PREFIX.Length);

            var translation = Memory.GetTextTranslation(inputText);

            if (translation.Result == TranslationResult.Ok || isRetranslating && translation.Result == TranslationResult.NotFound)
                e.Translation = translation.Text;

            if (e.Type == StringType.Template && e.Translation != null)
            {
                e.Translation = TEMPLATE_STRING_PREFIX + e.Translation;
                return;
            }

            if (translation.Result == TranslationResult.Ok || translation.Result == TranslationResult.Translated)
                return;

            if (!isAudioClipName)
            {
                if (e.Type != StringType.Template) // Don't put templates to clipboard -- let the game replace the values first
                    Clipboard.AddText(inputText, CurrentLevel);
                // Still going to dump, since templates are useful to translators, but not all translateable strings are templates
                Logger.DumpLine(inputText, CurrentLevel);
            }
            else
            {
                e.Translation = inputText;
                Logger.DumpLine(inputText, CurrentLevel, DumpType.Voices);
            }
        }

        private void OnTextureLoad(object sender, TextureTranslationEventArgs e)
        {
            string textureName = e.Name;

            if (lastFoundTexture != textureName)
            {
                lastFoundTexture = textureName;
                Logger.WriteLine(ResourceType.Textures, LogLevel.Minor, $"FindTexture::{textureName}");
            }

            var replacement = Memory.GetTexture(textureName);
            TextureResource resource = null;

            switch (replacement.TextureType)
            {
                case TextureType.PNG:
                    resource = new TextureResource(1, 1, TextureFormat.ARGB32, null, File.ReadAllBytes(replacement.FilePath));
                    break;
                case TextureType.TEX:
                    resource = TexUtils.ReadTexture(File.ReadAllBytes(replacement.FilePath), textureName);
                    break;
                case TextureType.None:
                default:
                    if (e.OriginalTexture != null)
                        Logger.DumpTexture(DumpType.TexSprites, e, true, CurrentLevel);
                    return;
            }

            if (lastLoadedTexture != textureName)
                Logger.WriteLine(ResourceType.Textures, $"Texture::{textureName}");
            lastLoadedTexture = textureName;

            e.Data = resource;
        }

        private void TranslateExisting(bool levelChanged = false)
        {
            isRetranslating = !levelChanged && Settings.EnableTranslationReload;
            var processedTextures = new HashSet<string>();
            foreach (var widget in FindObjectsOfType<UIWidget>())
                if (widget is UILabel label)
                {
                    processAndRequest(label);
                }
                else
                {
                    string texName = widget.mainTexture?.name;
                    if (string.IsNullOrEmpty(texName) || processedTextures.Contains(texName))
                        continue;
                    processedTextures.Add(texName);

                    switch (widget)
                    {
                        case UI2DSprite sprite:
                            TranslationHooks.OnAssetTextureLoad(1, sprite);
                            break;
                        case UITexture tex:
                            TranslationHooks.OnAssetTextureLoad(1, tex);
                            break;
                        default:
                            TranslationHooks.OnAssetTextureLoad(1, widget);
                            break;
                    }
                }

            isRetranslating = false;

            foreach (var graphic in FindObjectsOfType<MaskableGraphic>())
            {
                if (graphic is Image img && img.sprite != null)
                    if (img.sprite.name.StartsWith("!"))
                        img.sprite.name = img.sprite.name.Substring(1);
                TranslationHooks.OnTranslateGraphic(graphic);
            }
        }
    }
}
