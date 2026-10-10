using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace FieldManual
{
#pragma warning disable CS0649 // fields are populated by Json.NET
    internal sealed class GuideFile
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("entries")] public List<GuideEntry> Entries = new List<GuideEntry>();
    }

    internal sealed class GuideMatch
    {
        /// <summary>ObjectInfoAsset names or asset GUIDs (exact, case-insensitive).</summary>
        [JsonProperty("objects")] public List<string> Objects;
        /// <summary>StructurePartAsset names (exact, case-insensitive).</summary>
        [JsonProperty("parts")] public List<string> Parts;
        /// <summary>Text contained in the object's displayed name or asset name (case-insensitive).</summary>
        [JsonProperty("nameContains")] public List<string> NameContains;
        /// <summary>Text the displayed name or asset name must NOT contain.</summary>
        [JsonProperty("nameExcludes")] public List<string> NameExcludes;
        /// <summary>Hazard flags detected from game data, e.g. "Explosive", "Reactor", "SealsPressurizedRoom".</summary>
        [JsonProperty("hazards")] public List<string> Hazards;
        /// <summary>CategoryAsset names or displayed category labels.</summary>
        [JsonProperty("categories")] public List<string> Categories;
        /// <summary>Salvage destinations, e.g. "Barge", "Processor", "Furnace".</summary>
        [JsonProperty("destinations")] public List<string> Destinations;
    }

    internal sealed class GuideEntry
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("match")] public GuideMatch Match = new GuideMatch();
        [JsonProperty("priority")] public int Priority;
        [JsonProperty("title")] public string Title;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("actions")] public List<string> Actions;
        [JsonProperty("dismantle")] public List<string> Dismantle;
        [JsonProperty("warnings")] public List<string> Warnings;
        [JsonProperty("tips")] public List<string> Tips;
        /// <summary>Hide the automatically generated dismantle steps when this entry matches.</summary>
        [JsonProperty("replaceAutomatic")] public bool ReplaceAutomatic;
        /// <summary>The object's body must not be cut; suppresses the automatic "Cut with ..." suggestions.</summary>
        [JsonProperty("dontCut")] public bool DontCut;

        [JsonIgnore] public string Origin;
        [JsonIgnore] internal Hazard HazardMask;
        [JsonIgnore] internal HashSet<SalvageDestination> DestinationSet;
    }
#pragma warning restore CS0649

    internal sealed class GuideHit
    {
        public GuideEntry Entry;
        public int Score;
        public string Id => Entry.Id;
    }

    internal static class GuideDatabase
    {
        private const string kResourceName = "FieldManual.guide.json";

        private static List<GuideEntry> sEntries = new List<GuideEntry>();

        public static int Count => sEntries.Count;
        public static string UserFile => Path.Combine(FieldManualPlugin.ConfigFolder, "guide.json");
        public static string DefaultCopyFile => Path.Combine(FieldManualPlugin.ConfigFolder, "guide.default.json");

        public static void Load()
        {
            var entries = new List<GuideEntry>();
            string builtIn = ReadEmbedded();
            if (builtIn != null)
            {
                var file = Parse(builtIn, "built-in guide");
                if (file != null)
                {
                    foreach (var e in file.Entries) e.Origin = "built-in";
                    entries.AddRange(file.Entries);
                }
                WriteDefaultCopy(builtIn);
            }

            int userCount = 0;
            try
            {
                if (File.Exists(UserFile))
                {
                    var file = Parse(File.ReadAllText(UserFile), UserFile);
                    if (file != null)
                    {
                        foreach (var e in file.Entries)
                        {
                            e.Origin = "user";
                            int existing = string.IsNullOrEmpty(e.Id) ? -1 : entries.FindIndex(x => string.Equals(x.Id, e.Id, StringComparison.OrdinalIgnoreCase));
                            if (existing >= 0) entries[existing] = e;
                            else entries.Add(e);
                            userCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FieldManualPlugin.Log.LogError($"Could not read {UserFile}: {ex.Message}");
            }

            foreach (var e in entries) Prepare(e);
            sEntries = entries;
            FieldManualPlugin.Log.LogInfo($"Guide loaded: {entries.Count} entries ({userCount} from your guide.json).");
        }

        public static List<GuideHit> Match(PartInfo info)
        {
            var hits = new List<GuideHit>();
            if (info == null) return hits;
            foreach (var entry in sEntries)
            {
                int score = Score(entry, info);
                if (score > 0)
                {
                    hits.Add(new GuideHit { Entry = entry, Score = score + entry.Priority });
                }
            }
            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
            return hits;
        }

        /// <summary>True when an entry matched on something more specific than a hazard, category or destination.</summary>
        public static bool HasSpecificMatch(List<GuideHit> hits) => hits.Any(h => h.Score - h.Entry.Priority >= 300);

        private static int Score(GuideEntry entry, PartInfo info)
        {
            var m = entry.Match;
            if (m == null) return 0;

            if (m.NameExcludes != null && m.NameExcludes.Any(x => Contains(info.Name, x) || Contains(info.ObjectAsset, x)))
            {
                return 0;
            }

            // Identity selectors (objects, parts, nameContains) are alternatives: any one of them may match.
            // Hazards, categories and destinations are filters: every populated one must match as well.
            int identity = 0;
            bool hasIdentity = HasItems(m.Objects) || HasItems(m.Parts) || HasItems(m.NameContains);
            if (AnyEquals(m.Objects, info.ObjectAsset) || AnyEquals(m.Objects, info.ObjectGuid)) identity = 500;
            else if (AnyEquals(m.Parts, info.PartAsset)) identity = 450;
            else if (HasItems(m.NameContains) && m.NameContains.Any(x => Contains(info.Name, x) || Contains(info.ObjectAsset, x) || Contains(info.PartAsset, x))) identity = 300;
            if (hasIdentity && identity == 0) return 0;

            bool hasHazards = entry.HazardMask != Hazard.None;
            bool hasCategories = HasItems(m.Categories);
            bool hasDestinations = entry.DestinationSet != null && entry.DestinationSet.Count > 0;
            if (hasHazards && (info.Hazards & entry.HazardMask) == 0) return 0;
            if (hasCategories && !m.Categories.Any(c => info.CategoryAssets.Any(a => Eq(a, c)) || info.Categories.Any(l => Eq(l, c)))) return 0;
            if (hasDestinations && !entry.DestinationSet.Contains(info.Destination)) return 0;

            if (identity > 0) return identity;
            if (hasHazards) return 200;
            if (hasCategories) return 100;
            if (hasDestinations) return 50;
            return 0;
        }

        private static bool HasItems(List<string> list) => list != null && list.Count > 0;

        private static void Prepare(GuideEntry entry)
        {
            entry.Match = entry.Match ?? new GuideMatch();
            entry.HazardMask = Hazard.None;
            if (entry.Match.Hazards != null)
            {
                foreach (var h in entry.Match.Hazards)
                {
                    if (Enum.TryParse<Hazard>(h, true, out var flag)) entry.HazardMask |= flag;
                    else FieldManualPlugin.Log.LogWarning($"Guide entry '{entry.Id}': unknown hazard '{h}'.");
                }
            }
            if (entry.Match.Destinations != null)
            {
                entry.DestinationSet = new HashSet<SalvageDestination>();
                foreach (var d in entry.Match.Destinations)
                {
                    if (Enum.TryParse<SalvageDestination>(d, true, out var dest)) entry.DestinationSet.Add(dest);
                    else FieldManualPlugin.Log.LogWarning($"Guide entry '{entry.Id}': unknown destination '{d}'.");
                }
            }
        }

        private static GuideFile Parse(string json, string source)
        {
            try
            {
                var file = JsonConvert.DeserializeObject<GuideFile>(json);
                if (file?.Entries == null)
                {
                    FieldManualPlugin.Log.LogWarning($"{source} has no \"entries\" array.");
                    return null;
                }
                file.Entries.RemoveAll(e => e == null);
                return file;
            }
            catch (Exception ex)
            {
                FieldManualPlugin.Log.LogError($"Could not parse {source}: {ex.Message}");
                return null;
            }
        }

        private static string ReadEmbedded()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(kResourceName))
                {
                    if (stream == null)
                    {
                        FieldManualPlugin.Log.LogError("Built-in guide resource is missing.");
                        return null;
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                FieldManualPlugin.Log.LogError($"Could not read built-in guide: {ex.Message}");
                return null;
            }
        }

        // Keeps an up-to-date copy of the built-in guide next to the user's file as a reference for editing.
        private static void WriteDefaultCopy(string json)
        {
            try
            {
                Directory.CreateDirectory(FieldManualPlugin.ConfigFolder);
                if (!File.Exists(DefaultCopyFile) || File.ReadAllText(DefaultCopyFile) != json)
                {
                    File.WriteAllText(DefaultCopyFile, json);
                }
            }
            catch (Exception ex)
            {
                FieldManualPlugin.Log.LogWarning($"Could not write {DefaultCopyFile}: {ex.Message}");
            }
        }

        private static bool AnyEquals(List<string> list, string value) => list != null && !string.IsNullOrEmpty(value) && list.Any(x => Eq(x, value));

        private static bool Eq(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && !string.IsNullOrEmpty(needle) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
