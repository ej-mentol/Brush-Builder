using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Translations;
using HammerTime.BrushBuilder.Operations;

namespace HammerTime.BrushBuilder.UI
{
    [Export(typeof(ISettingsContainer))]
    [Export]
    [AutoTranslate]
    public class BrushBuilderSettingsContainer : ISettingsContainer
    {
        public string Name => "HammerTime.BrushBuilder.Settings";

        public bool ValuesLoaded { get; private set; } = false;

        private readonly TranslationStringsCatalog _catalog;

        [ImportingConstructor]
        public BrushBuilderSettingsContainer(
            [Import] TranslationStringsCatalog catalog,
            [Import(AllowDefault = true)] IApplicationInfo appInfo
        )
        {
            _catalog = catalog;

            if (appInfo != null)
            {
                var folder = appInfo.GetApplicationSettingsFolder("Translations");
                if (folder != null)
                {
                    try
                    {
                        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                        var file = Path.Combine(folder, "HammerTime.BrushBuilder.en.json");
                        if (!File.Exists(file))
                        {
                            File.WriteAllText(file, GetDefaultTranslationContent(), System.Text.Encoding.UTF8);
                        }
                    }
                    catch
                    {
                        // Ignore file system write errors
                    }
                }
            }

            _catalog.Load(typeof(BrushBuilderSettingsContainer));

            // Fallback translation registration in case localized json file isn't loaded
            foreach (var langCode in new[] { "en", "debug_en" })
            {
                if (_catalog.Languages.ContainsKey(langCode))
                {
                    var lang = _catalog.Languages[langCode];
                    
                    if (!lang.Collection.Settings.ContainsKey("@Group.Tools/Plugins/BrushBuilder"))
                        lang.Collection.Settings["@Group.Tools/Plugins/BrushBuilder"] = "Brush Builder";

                    var settingsPrefix = "HammerTime.BrushBuilder.Settings.";
                    var fallbackSettings = new Dictionary<string, string>
                    {
                        { settingsPrefix + "Validation", "Validation Mode" },
                        { settingsPrefix + "ColorFace1", "Face 1 (Blue) Color" },
                        { settingsPrefix + "ColorFace2", "Face 2 (Green) Color" },
                        { settingsPrefix + "ColorFaceClip", "Helper/Clip Color" },
                        { settingsPrefix + "ColorFaceHover", "Hover Highlight Color" },
                        { settingsPrefix + "ColorPreview", "Solid Preview Color" },
                        { settingsPrefix + "ShowHoverPreview", "Show Hover Preview" }
                    };

                    foreach (var kv in fallbackSettings)
                    {
                        if (!lang.Collection.Settings.ContainsKey(kv.Key))
                            lang.Collection.Settings[kv.Key] = kv.Value;
                    }
                }
            }
        }

        private static string GetDefaultTranslationContent()
        {
            return @"{
  ""@Meta"": {
    ""Language"": ""en"",
    ""Base"": ""HammerTime.BrushBuilder"",
    ""LanguageDescription"": ""English"",
    ""Inherit"": """"
  },
  ""@Settings"": {
    ""@Group.Tools/Plugins/BrushBuilder"": ""Brush Builder"",
    ""Settings"": {
      ""Validation"": ""Validation Mode"",
      ""ColorFace1"": ""Face 1 (Blue) Color"",
      ""ColorFace2"": ""Face 2 (Green) Color"",
      ""ColorFaceClip"": ""Helper/Clip Color"",
      ""ColorFaceHover"": ""Hover Highlight Color"",
      ""ColorPreview"": ""Solid Preview Color"",
      ""ShowHoverPreview"": ""Show Hover Preview""
    }
  }
}";
        }

        public string Validation { get; set; } = "Reject Invalid Brushes";

        public Color ColorFace1 { get; set; } = Color.DeepSkyBlue;
        public Color ColorFace2 { get; set; } = Color.LimeGreen;
        public Color ColorFaceClip { get; set; } = Color.Coral;
        public Color ColorFaceHover { get; set; } = Color.Gold;
        public Color ColorPreview { get; set; } = Color.DodgerBlue;

        public bool ShowHoverPreview { get; set; } = true;

        public IEnumerable<SettingKey> GetKeys()
        {
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "Validation", typeof(string)) { EditorType = "Dropdown", EditorHint = "Reject Invalid Brushes,Warn Only,Ignore Warnings" };
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ColorFace1", typeof(Color));
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ColorFace2", typeof(Color));
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ColorFaceClip", typeof(Color));
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ColorFaceHover", typeof(Color));
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ColorPreview", typeof(Color));
            yield return new SettingKey("Tools/Plugins/BrushBuilder", "ShowHoverPreview", typeof(bool));
        }

        public void LoadValues(ISettingsStore store)
        {
            Validation = store.Get("Validation", "Reject Invalid Brushes") ?? "Reject Invalid Brushes";
            ColorFace1 = store.Get("ColorFace1", Color.DeepSkyBlue);
            ColorFace2 = store.Get("ColorFace2", Color.LimeGreen);
            ColorFaceClip = store.Get("ColorFaceClip", Color.Coral);
            ColorFaceHover = store.Get("ColorFaceHover", Color.Gold);
            ColorPreview = store.Get("ColorPreview", Color.DodgerBlue);
            ShowHoverPreview = store.Get("ShowHoverPreview", true);

            UpdateRuntimeColors();

            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("Validation", Validation ?? "Reject Invalid Brushes");
            store.Set("ColorFace1", ColorFace1);
            store.Set("ColorFace2", ColorFace2);
            store.Set("ColorFaceClip", ColorFaceClip);
            store.Set("ColorFaceHover", ColorFaceHover);
            store.Set("ColorPreview", ColorPreview);
            store.Set("ShowHoverPreview", ShowHoverPreview);

            UpdateRuntimeColors();
        }

        private void UpdateRuntimeColors()
        {
            BrushBuilderColors.Face1 = ColorFace1;
            BrushBuilderColors.Face2 = ColorFace2;
            BrushBuilderColors.FaceClip = ColorFaceClip;
            BrushBuilderColors.FaceHover = ColorFaceHover;
            BrushBuilderColors.PreviewColor = Color.FromArgb(64, ColorPreview);
        }
    }
}
