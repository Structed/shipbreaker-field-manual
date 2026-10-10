using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace FieldManual
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Shipbreaker.exe")]
    public class FieldManualPlugin : BaseUnityPlugin
    {
        public const string Guid = "structed.shipbreaker.fieldmanual";
        public const string Name = "Field Manual";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> VisibleOnStart;
        internal static ConfigEntry<bool> ShowKeyHint;

        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> Scale;
        internal static ConfigEntry<float> PanelWidth;
        internal static ConfigEntry<float> TopOffset;

        internal static ConfigEntry<bool> ShowDescription;
        internal static ConfigEntry<bool> ShowStats;
        internal static ConfigEntry<bool> ShowSystems;

        internal static ConfigEntry<KeyboardShortcut> ReloadGuideKey;
        internal static ConfigEntry<KeyboardShortcut> InspectKey;
        internal static ConfigEntry<bool> LogUnknownObjects;
        internal static ConfigEntry<bool> VerboseLogging;

        internal static string ConfigFolder => Path.Combine(Paths.ConfigPath, "FieldManual");

        private void Awake()
        {
            Log = Logger;

            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F1),
                "Shows or hides the field manual panel.");
            VisibleOnStart = Config.Bind("General", "VisibleOnStart", false,
                "Show the panel automatically when the game starts.");
            ShowKeyHint = Config.Bind("General", "ShowKeyHint", true,
                "Briefly show a small reminder of the toggle key at the start of every shift.");

            Opacity = Config.Bind("Appearance", "Opacity", 0.82f,
                new ConfigDescription("Panel background opacity.", new AcceptableValueRange<float>(0.2f, 1f)));
            Scale = Config.Bind("Appearance", "Scale", 1f,
                new ConfigDescription("Panel size multiplier.", new AcceptableValueRange<float>(0.5f, 2.5f)));
            PanelWidth = Config.Bind("Appearance", "PanelWidth", 430f,
                new ConfigDescription("Panel width in pixels at 1080p (scaled with resolution).", new AcceptableValueRange<float>(280f, 900f)));
            TopOffset = Config.Bind("Appearance", "TopOffset", 0.16f,
                new ConfigDescription("Distance of the panel from the top of the screen, as a fraction of the screen height.", new AcceptableValueRange<float>(0f, 0.6f)));

            ShowDescription = Config.Bind("Content", "ShowDescription", true,
                "Show the game's own description of the object.");
            ShowStats = Config.Bind("Content", "ShowStats", true,
                "Show value, mass and cut-grade details.");
            ShowSystems = Config.Bind("Content", "ShowSystems", true,
                "Show which ship systems (power, fuel, coolant, pressure) the object is part of.");

            ReloadGuideKey = Config.Bind("Debug", "ReloadGuideKey", new KeyboardShortcut(KeyCode.F1, KeyCode.LeftControl),
                "Reloads guide.json files from disk without restarting the game.");
            InspectKey = Config.Bind("Debug", "InspectKey", new KeyboardShortcut(KeyCode.F1, KeyCode.LeftShift),
                "Writes everything known about the current target (asset names, ECS components) to the BepInEx log.");
            LogUnknownObjects = Config.Bind("Debug", "LogUnknownObjects", false,
                "Log objects that have no curated guide entry (useful when writing your own guide.json).");
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Log target changes and other details to BepInEx\\LogOutput.log.");

            GuideDatabase.Load();

            var host = new GameObject("FieldManual");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<HelpPanel>();

            Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} during a shift to toggle the field manual.");
        }
    }
}
