// ObjectCatalogDump: offline dumper of Hardspace: Shipbreaker object definitions (ObjectInfoAsset,
// CategoryAsset, StructurePartAsset + linked cutting/salvage/blueprint assets) with English names
// resolved from Data/LocDB. Reads the game install strictly read-only.
//
// Usage: dotnet run --project ObjectCatalogDump.csproj -c Release -- ["<gameDir>"] ["<outDir>"]
// The output contains game text: write it outside of any source repository.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

const string DefaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker";

var gameDir = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : DefaultGameDir;
var outDir = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : Path.Combine(Environment.CurrentDirectory, "catalog");
var dataDir = Path.Combine(gameDir, "Shipbreaker_Data");
var aaDir = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
var managedDir = Path.Combine(dataDir, "Managed");
var locDir = Path.Combine(gameDir, "Data", "LocDB");
foreach (var d in new[] { aaDir, managedDir, locDir })
    if (!Directory.Exists(d)) { Console.Error.WriteLine($"Not found: {d}\nUsage: ObjectCatalogDump [\"<gameDir>\"] [\"<outDir>\"]"); return 2; }

var sw = System.Diagnostics.Stopwatch.StartNew();
var loc = LocDb.Load(locDir, "en");
Console.WriteLine($"LocDB: {loc.Count} English entries ({loc.ContinuationLines} continuation lines, {loc.Duplicates} duplicate ids)");

var scan = new Scanner(aaDir, managedDir);
scan.ScanBundles();
var playerAssetHits = scan.CheckPlayerAssets(dataDir);
Console.WriteLine($"Scanned {scan.BundleCount} bundles in {sw.Elapsed.TotalSeconds:F1}s; indexed {scan.Index.Count} ScriptableObject MonoBehaviours");

var catalog = CatalogBuilder.Build(scan, loc, gameDir, playerAssetHits);
Directory.CreateDirectory(outDir);
var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
var jsonPath = Path.Combine(outDir, "catalog.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(catalog, jsonOpts), new UTF8Encoding(false));
var txtPath = Path.Combine(outDir, "catalog.txt");
File.WriteAllLines(txtPath, CatalogBuilder.TextLines(catalog), new UTF8Encoding(false));

var m = catalog.Meta;
Console.WriteLine($"objectInfos={catalog.ObjectInfos.Count} categories={catalog.Categories.Count} structureParts={catalog.StructureParts.Count} cuttingTargetables={catalog.CuttingTargetables.Count}");
Console.WriteLine($"loc: resolved {m.LocResolved}/{m.LocFieldsWithId} loc-id fields (empty English text: {m.LocEmpty.Count}), unresolved: {m.LocUnresolved.Count}");
if (m.RawHeaderNameMismatches > 0) Console.WriteLine($"WARNING: {m.RawHeaderNameMismatches} raw MonoBehaviour header names disagreed with the type tree read");
Console.WriteLine($"structurePart links: objectInfo {m.StructurePartsWithObjectInfo}/{catalog.StructureParts.Count}, cutting {m.StructurePartsWithCutting}/{catalog.StructureParts.Count}, unresolved PPtrs {m.UnresolvedReferences.Count}");
if (playerAssetHits.Count > 0) Console.WriteLine("WARNING: target classes also found in player .assets files (not dumped): " + string.Join(", ", playerAssetHits));
Console.WriteLine($"Wrote {jsonPath}\nWrote {txtPath}\nDone in {sw.Elapsed.TotalSeconds:F1}s");
return 0;

// ---------------------------------------------------------------------------------------------
// Localization (port of Carbon.Localization.Core.SingleLanguageCacheLocalizationDatabase)
// ---------------------------------------------------------------------------------------------
sealed class LocDb
{
    readonly Dictionary<ulong, string> mEntries = new();
    public int Count => mEntries.Count;
    public int ContinuationLines { get; private set; }
    public int Duplicates { get; private set; }

    // Format: UTF-8 text file per language (no extension, e.g. "en"). Each entry line is "<ulong id>,<escaped text>";
    // a line that doesn't start with "<ulong>," is a continuation appended to the previous entry with '\n'.
    public static LocDb Load(string locDir, string language)
    {
        var db = new LocDb();
        ulong last = 0;
        using var reader = File.OpenText(Path.Combine(locDir, language));
        while (reader.ReadLine() is { } line)
        {
            int comma = line.IndexOf(',');
            if (comma >= 0 && ulong.TryParse(line.AsSpan(0, comma), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                if (db.mEntries.ContainsKey(id)) db.Duplicates++;
                db.mEntries[id] = UnEscape(line[(comma + 1)..]);
                last = id;
            }
            else if (db.mEntries.TryGetValue(last, out var prev))
            {
                db.mEntries[last] = prev + "\n" + UnEscape(line);
                db.ContinuationLines++;
            }
        }
        return db;
    }

    // Same result as Carbon.Core.EscapeCharacterHelper.UnEscape: \n \r \t \f \b \" \\ are decoded,
    // any other backslash sequence (and a trailing backslash) is kept literally.
    static string UnEscape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'f': sb.Append('\f'); break;
                case 'b': sb.Append('\b'); break;
                case '"': case '\\': sb.Append(n); break;
                default: sb.Append('\\').Append(n); break;
            }
        }
        return sb.ToString();
    }

    // Port of LocalizationUtilities.FieldValueToLocID: optional "LocID:" prefix, then a ulong.
    public static ulong? ParseLocId(string? field)
    {
        if (string.IsNullOrEmpty(field)) return null;
        if (field.StartsWith("LocID:", StringComparison.Ordinal)) field = field["LocID:".Length..];
        return ulong.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    public bool TryLocalize(string? field, out string text)
    {
        text = string.Empty;
        var id = ParseLocId(field);
        return id.HasValue && mEntries.TryGetValue(id.Value, out text!);
    }
}

// ---------------------------------------------------------------------------------------------
// Asset scanning
// ---------------------------------------------------------------------------------------------
readonly record struct AssetKey(string Cab, long PathId)
{
    public override string ToString() => $"{Cab}:{PathId}";
}

sealed record IndexedAsset(AssetKey Key, string Class, string Name, string Bundle);

sealed class ExtractedAsset
{
    public required IndexedAsset Info { get; init; }
    public required AssetTypeValueField Field { get; init; }
    public required Func<AssetTypeValueField, AssetKey?> Resolve { get; init; }
}

sealed class Scanner(string aaDir, string managedDir)
{
    public static readonly HashSet<string> TargetClasses = ["ObjectInfoAsset", "CategoryAsset", "StructurePartAsset", "CuttingTargetableAsset"];
    static readonly HashSet<string> FullReadClasses = [.. TargetClasses, "SalvageableComponentAsset", "EntityBlueprintAsset"];

    readonly AssetsManager mAm = new() { MonoTempGenerator = new MonoCecilTempGenerator(managedDir) };
    public Dictionary<AssetKey, IndexedAsset> Index { get; } = new();
    public Dictionary<string, List<ExtractedAsset>> Extracted { get; } = FullReadClasses.ToDictionary(c => c, _ => new List<ExtractedAsset>());
    public SortedDictionary<string, SortedDictionary<string, int>> TargetCountsByBundle { get; } = new(StringComparer.Ordinal);
    public int BundleCount { get; private set; }
    public int RawNameMismatches { get; private set; }

    public void ScanBundles()
    {
        var monoPath = Directory.GetFiles(aaDir, "*_monoscripts_*.bundle").Single();
        var monoBun = mAm.LoadBundleFile(monoPath, true);
        for (int i = 0; i < monoBun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            if (monoBun.file.IsAssetsFile(i)) mAm.LoadAssetsFileFromBundle(monoBun, i, false);

        foreach (var path in Directory.GetFiles(aaDir, "*.bundle").Order(StringComparer.Ordinal))
        {
            if (path == monoPath) continue;
            BundleCount++;
            var bun = mAm.LoadBundleFile(path, true);
            try
            {
                for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    if (!bun.file.IsAssetsFile(i)) continue;
                    var inst = mAm.LoadAssetsFileFromBundle(bun, i, false);
                    ScanAssetsFile(inst, Path.GetFileName(path), bun.file.GetFileName(i));
                }
            }
            finally { mAm.UnloadBundleFile(bun); }
        }
    }

    static string CabOf(string pathName)
    {
        // Bundle externals look like "archive:/CAB-xxxx/CAB-xxxx"
        var slash = pathName.LastIndexOf('/');
        return (slash >= 0 ? pathName[(slash + 1)..] : pathName).ToLowerInvariant();
    }

    void ScanAssetsFile(AssetsFileInstance inst, string bundleName, string cabName)
    {
        var cab = cabName.ToLowerInvariant();
        var externals = inst.file.Metadata.Externals.Select(e => CabOf(e.PathName)).ToArray();
        AssetKey? Resolve(AssetTypeValueField pptr)
        {
            if (pptr.IsDummy) return null;
            int fid = pptr["m_FileID"].AsInt;
            long pid = pptr["m_PathID"].AsLong;
            if (pid == 0) return null;
            if (fid == 0) return new AssetKey(cab, pid);
            return fid - 1 < externals.Length ? new AssetKey(externals[fid - 1], pid) : new AssetKey($"?file{fid}", pid);
        }

        var classBySi = new Dictionary<int, string?>();
        var reader = inst.file.Reader;
        foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            int si = info.GetScriptIndex(inst.file);
            if (!classBySi.TryGetValue(si, out var cls))
            {
                cls = null;
                if (si >= 0 && si < inst.file.Metadata.ScriptTypes.Count)
                {
                    var p = inst.file.Metadata.ScriptTypes[si];
                    try { cls = mAm.GetExtAsset(inst, p.FileId, p.PathId).baseField?["m_ClassName"].AsString; } catch { cls = null; }
                }
                classBySi[si] = cls;
            }
            if (cls == null) continue;

            // MonoBehaviour header: PPtr m_GameObject (12) | UInt8 m_Enabled (+align = 4) | PPtr m_Script (12) | string m_Name
            long start = info.GetAbsoluteByteOffset(inst.file);
            reader.Position = start + 4;
            if (reader.ReadInt64() != 0) continue; // component on a GameObject, not a ScriptableObject asset
            reader.Position = start + 28;
            int len = reader.ReadInt32();
            string name = len is >= 0 and < 4096 ? Encoding.UTF8.GetString(reader.ReadBytes(len)) : "";

            var key = new AssetKey(cab, info.PathId);
            var entry = new IndexedAsset(key, cls, name, bundleName);
            Index[key] = entry;

            if (TargetClasses.Contains(cls))
            {
                if (!TargetCountsByBundle.TryGetValue(bundleName, out var counts)) TargetCountsByBundle[bundleName] = counts = new(StringComparer.Ordinal);
                counts[cls] = counts.GetValueOrDefault(cls) + 1;
            }
            if (FullReadClasses.Contains(cls))
            {
                var bf = mAm.GetBaseField(inst, info);
                if (bf["m_Name"].AsString != name) RawNameMismatches++;
                Extracted[cls].Add(new ExtractedAsset { Info = entry, Field = bf, Resolve = Resolve });
            }
        }
    }

    // Coverage check: player .assets/level files have no type trees, so only identify script classes
    // (MonoScripts are parsed raw) and report any target class that would be missed by the bundle scan.
    public List<string> CheckPlayerAssets(string dataDir)
    {
        var hits = new List<string>();
        var am = new AssetsManager();
        var files = Directory.GetFiles(dataDir).Where(p => p.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith("level", StringComparison.Ordinal));
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            try
            {
                var inst = am.LoadAssetsFile(path, false);
                var counts = new Dictionary<string, int>();
                var bySi = new Dictionary<int, string?>();
                foreach (var info in inst.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    int si = info.GetScriptIndex(inst.file);
                    if (!bySi.TryGetValue(si, out var cls)) bySi[si] = cls = RawMonoScriptClass(am, inst, si);
                    if (cls != null && TargetClasses.Contains(cls)) counts[cls] = counts.GetValueOrDefault(cls) + 1;
                }
                hits.AddRange(counts.Select(kv => $"{Path.GetFileName(path)}:{kv.Key}={kv.Value}"));
            }
            catch (Exception ex) { Console.Error.WriteLine($"warn: could not check {Path.GetFileName(path)}: {ex.Message}"); }
        }
        am.UnloadAll();
        return hits;
    }

    static string? RawMonoScriptClass(AssetsManager am, AssetsFileInstance inst, int si)
    {
        if (si < 0 || si >= inst.file.Metadata.ScriptTypes.Count) return null;
        var p = inst.file.Metadata.ScriptTypes[si];
        var ext = p.FileId == 0 ? inst : inst.GetDependency(am, p.FileId - 1);
        var mi = ext?.file.GetAssetInfo(p.PathId);
        if (ext == null || mi == null) return null;
        var r = ext.file.Reader;
        long start = mi.GetAbsoluteByteOffset(ext.file);
        r.Position = start;
        string Str()
        {
            int n = r.ReadInt32();
            var s = Encoding.UTF8.GetString(r.ReadBytes(n));
            long rel = r.Position - start;
            r.Position = start + ((rel + 3) & ~3L);
            return s;
        }
        // MonoScript: m_Name, m_ExecutionOrder (int), m_PropertiesHash (Hash128), m_ClassName, ...
        Str(); r.ReadInt32(); r.ReadBytes(16);
        return Str();
    }
}

// ---------------------------------------------------------------------------------------------
// Output model
// ---------------------------------------------------------------------------------------------
sealed class Catalog
{
    public required CatalogMeta Meta { get; init; }
    public required List<ObjectInfoOut> ObjectInfos { get; init; }
    public required List<CategoryOut> Categories { get; init; }
    public required List<StructurePartOut> StructureParts { get; init; }
    public required List<CuttingOut> CuttingTargetables { get; init; }
}

sealed class CatalogMeta
{
    public string Tool { get; init; } = "ObjectCatalogDump";
    public string GameDir { get; init; } = "";
    public string? BuildInfo { get; init; }
    public string LocLanguage { get; init; } = "en";
    public int LocEntries { get; init; }
    public int LocFieldsWithId { get; set; }
    public int LocResolved { get; set; }
    public List<string> LocUnresolved { get; init; } = [];
    public List<string> LocEmpty { get; init; } = [];
    public int IndexedScriptableObjects { get; init; }
    public int RawHeaderNameMismatches { get; init; }
    public int StructurePartsWithObjectInfo { get; set; }
    public int StructurePartsWithCutting { get; set; }
    public List<string> UnresolvedReferences { get; init; } = [];
    public SortedDictionary<string, SortedDictionary<string, int>> SourceBundles { get; init; } = new();
    public List<string> PlayerAssetsTargetHits { get; init; } = [];
    public Dictionary<string, string> Notes { get; init; } = new();
}

sealed class CategoryRef
{
    public string AssetName { get; init; } = "";
    public string? Id { get; init; }
    public string? Name { get; init; }
}

sealed class ObjectInfoOut
{
    public string AssetName { get; init; } = "";
    public string? Id { get; init; }
    public int[]? IdInt4 { get; init; }
    public string Bundle { get; init; } = "";
    public long PathId { get; init; }
    public string? ObjectNameLocId { get; init; }
    public string? ObjectName { get; init; }
    public string? DestroyedObjectNameLocId { get; init; }
    public string? DestroyedObjectName { get; init; }
    public string? DescriptionLocId { get; init; }
    public string? Description { get; init; }
    public int Priority { get; init; }
    public string? CategoryColor { get; init; }
    public string? SpeechAsset { get; init; }
    public bool TriggerSpeechMoreThanOnce { get; init; }
    public List<CategoryRef> Categories { get; init; } = [];
    public List<string> StructureParts { get; init; } = [];
    public List<CuttingOut> Cutting { get; init; } = [];
    public List<string> SalvageDestinations { get; init; } = [];
    public List<string> BlueprintComponentClasses { get; init; } = [];
}

sealed class CategoryOut
{
    public string AssetName { get; init; } = "";
    public string? Id { get; init; }
    public int[]? IdInt4 { get; init; }
    public string Bundle { get; init; } = "";
    public long PathId { get; init; }
    public string? NameLocId { get; init; }
    public string? Name { get; init; }
    public int ObjectInfoCount { get; set; }
}

sealed class CuttingOut
{
    public string AssetName { get; init; } = "";
    public int PowerRating { get; init; }
    public bool IsCuttable { get; init; }
    public bool IsBlocking { get; init; }
    public int CuttableBy { get; init; }
    public List<string> CuttableByFlags { get; init; } = [];
    public float[]? MeltingTimeRange { get; init; }
    public float[]? MeltingMassRange { get; init; }
}

sealed class CurrencyOut
{
    public string? Currency { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public bool MassBased { get; init; }
}

sealed class SalvageOut
{
    public string AssetName { get; init; } = "";
    public int Options { get; init; }
    public List<string> Destinations { get; init; } = [];
    public string? Denomination { get; init; }
    public List<CurrencyOut> AwardedCurrencies { get; init; } = [];
}

sealed class BlueprintOut
{
    public string AssetName { get; init; } = "";
    public List<string> Components { get; init; } = [];
}

sealed class StructurePartOut
{
    public string AssetName { get; init; } = "";
    public string Bundle { get; init; } = "";
    public long PathId { get; init; }
    public bool DisplaysInfo { get; init; }
    public bool ApplySurfaceDamage { get; init; }
    public string? ObjectInfo { get; init; }
    public string? ObjectInfoName { get; init; }
    public CuttingOut? CuttingTargetable { get; init; }
    public SalvageOut? Salvageable { get; init; }
    public string? SalvageableSource { get; init; }
    public BlueprintOut? MigrationEntityBlueprint { get; init; }
    public SortedDictionary<string, string?> OtherRefs { get; init; } = new(StringComparer.Ordinal);
}

// ---------------------------------------------------------------------------------------------
// Catalog building / linking
// ---------------------------------------------------------------------------------------------
static class CatalogBuilder
{
    static readonly string[] CuttableByNames = ["QuickCut", "AimCut", "MegaCut", "Scalpel", "DemoCharge"];
    static readonly string[] SalvageNames = ["Pickup", "Barge", "Furnace", "Processor", "Destroy"];

    static List<string> Flags(int value, string[] names) =>
        Enumerable.Range(0, names.Length).Where(b => (value & (1 << b)) != 0).Select(b => names[b]).ToList();

    static int[]? GuidToInt4(string? guid)
    {
        if (!Guid.TryParse(guid, out var g)) return null;
        var b = g.ToByteArray();
        return [BitConverter.ToInt32(b, 0), BitConverter.ToInt32(b, 4), BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12)];
    }

    static string Hex(AssetTypeValueField c)
    {
        static int B(float f) => (int)Math.Round(Math.Clamp(f, 0f, 1f) * 255f);
        return $"#{B(c["r"].AsFloat):X2}{B(c["g"].AsFloat):X2}{B(c["b"].AsFloat):X2}{B(c["a"].AsFloat):X2}";
    }

    static string? Str(AssetTypeValueField f) => f.IsDummy ? null : f.AsString;

    static string Camel(string fieldName) =>
        JsonNamingPolicy.CamelCase.ConvertName(fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName[2..] : fieldName);

    public static Catalog Build(Scanner scan, LocDb loc, string gameDir, List<string> playerAssetHits)
    {
        string? buildInfo = null;
        var buildInfoPath = Path.Combine(gameDir, "Shipbreaker_Data", "buildinfo.txt");
        if (File.Exists(buildInfoPath)) buildInfo = File.ReadAllText(buildInfoPath).Trim();

        var meta = new CatalogMeta
        {
            GameDir = gameDir,
            BuildInfo = buildInfo,
            LocEntries = loc.Count,
            IndexedScriptableObjects = scan.Index.Count,
            RawHeaderNameMismatches = scan.RawNameMismatches,
            SourceBundles = scan.TargetCountsByBundle,
            PlayerAssetsTargetHits = playerAssetHits,
            Notes =
            {
                ["id"] = "TypeAsset ID = AssetTypeID<T> built from serialized m_AssetGUID (idInt4 = Guid bytes as 4 little-endian ints, as LynxAddressables.StringGuidToInt4Guid).",
                ["locId"] = "Raw [LocalizeField] value ('LocID:<ulong>'), resolved against Data/LocDB/en.",
                ["cuttableBy"] = "Flags: QuickCut=1, AimCut=2, MegaCut=4, Scalpel=8, DemoCharge=16. powerRating is compared against the cutter's power.",
                ["salvage"] = "SalvageableComponentAsset.PossibleSalvageableOptions flags: Pickup=1, Barge=2, Furnace=4, Processor=8, Destroy=16.",
            },
        };

        string? L(string? field, string ctx)
        {
            if (LocDb.ParseLocId(field) == null) return string.IsNullOrEmpty(field) ? null : field;
            meta.LocFieldsWithId++;
            if (loc.TryLocalize(field, out var text))
            {
                meta.LocResolved++;
                if (text.Length == 0) meta.LocEmpty.Add($"{ctx}={field}");
                return text;
            }
            meta.LocUnresolved.Add($"{ctx}={field}");
            return null;
        }

        string RefName(AssetKey? key, string ctx)
        {
            if (key == null) return "";
            if (scan.Index.TryGetValue(key.Value, out var a)) return $"{a.Class}:{a.Name}";
            meta.UnresolvedReferences.Add($"{ctx} -> {key}");
            return $"unresolved({key})";
        }

        // Categories
        var categoriesByKey = new Dictionary<AssetKey, CategoryOut>();
        foreach (var e in scan.Extracted["CategoryAsset"])
        {
            var bf = e.Field;
            var guid = Str(bf["m_AssetGUID"]);
            var nameField = Str(bf["m_CategoryName"]);
            categoriesByKey[e.Info.Key] = new CategoryOut
            {
                AssetName = e.Info.Name,
                Id = guid,
                IdInt4 = GuidToInt4(guid),
                Bundle = e.Info.Bundle,
                PathId = e.Info.Key.PathId,
                NameLocId = nameField,
                Name = L(nameField, e.Info.Name + ".m_CategoryName"),
            };
        }

        // Cutting targetables
        var cuttingByKey = new Dictionary<AssetKey, CuttingOut>();
        foreach (var e in scan.Extracted["CuttingTargetableAsset"])
        {
            var d = e.Field["Data"];
            int by = d["m_CuttableBy"].AsInt;
            cuttingByKey[e.Info.Key] = new CuttingOut
            {
                AssetName = e.Info.Name,
                PowerRating = d["m_PowerRating"].AsInt,
                IsCuttable = d["m_IsCuttable"].AsBool,
                IsBlocking = d["m_IsBlocking"].AsBool,
                CuttableBy = by,
                CuttableByFlags = Flags(by, CuttableByNames),
                MeltingTimeRange = d["m_MeltingTimeRange"].IsDummy ? null : [d["m_MeltingTimeRange"]["Min"].AsFloat, d["m_MeltingTimeRange"]["Max"].AsFloat],
                MeltingMassRange = d["m_MeltingMassRange"].IsDummy ? null : [d["m_MeltingMassRange"]["Min"].AsFloat, d["m_MeltingMassRange"]["Max"].AsFloat],
            };
        }

        // Salvageables
        var salvageByKey = new Dictionary<AssetKey, SalvageOut>();
        foreach (var e in scan.Extracted["SalvageableComponentAsset"])
        {
            var d = e.Field["m_Data"];
            int opts = d["m_PossibleSalvageableOptions"].AsInt;
            var currencies = new List<CurrencyOut>();
            if (!d["m_AwardedCurrencies"].IsDummy)
                foreach (var c in d["m_AwardedCurrencies"]["Array"].Children)
                {
                    var ck = e.Resolve(c["m_CurrencyType"]);
                    currencies.Add(new CurrencyOut
                    {
                        Currency = ck != null && scan.Index.TryGetValue(ck.Value, out var ca) ? ca.Name : ck?.ToString(),
                        Min = c["m_MinInitialValue"].AsFloat,
                        Max = c["m_MaxInitialValue"].AsFloat,
                        MassBased = c["m_MassBasedValue"].AsBool,
                    });
                }
            salvageByKey[e.Info.Key] = new SalvageOut
            {
                AssetName = e.Info.Name,
                Options = opts,
                Destinations = Flags(opts, SalvageNames),
                Denomination = Str(d["m_Denomination"]),
                AwardedCurrencies = currencies,
            };
        }

        // Entity blueprints (component data asset lists)
        var blueprintComponents = new Dictionary<AssetKey, List<AssetKey>>();
        foreach (var e in scan.Extracted["EntityBlueprintAsset"])
        {
            var list = new List<AssetKey>();
            var arr = e.Field["m_ComponentDataAssets"];
            if (!arr.IsDummy)
                foreach (var c in arr["Array"].Children)
                    if (e.Resolve(c) is { } k) list.Add(k);
            blueprintComponents[e.Info.Key] = list;
        }

        // Object infos
        var objectInfosByKey = new Dictionary<AssetKey, ObjectInfoOut>();
        foreach (var e in scan.Extracted["ObjectInfoAsset"])
        {
            var bf = e.Field;
            var d = bf["m_Data"];
            var guid = Str(bf["m_AssetGUID"]);
            var cats = new List<CategoryRef>();
            foreach (var c in d["m_Categories"]["Array"].Children)
            {
                var ck = e.Resolve(c);
                if (ck == null) continue;
                if (categoriesByKey.TryGetValue(ck.Value, out var cat))
                {
                    cat.ObjectInfoCount++;
                    cats.Add(new CategoryRef { AssetName = cat.AssetName, Id = cat.Id, Name = cat.Name });
                }
                else cats.Add(new CategoryRef { AssetName = RefName(ck, e.Info.Name + ".m_Categories") });
            }
            var speechKey = e.Resolve(d["m_SpeechAsset"]);
            string on = Str(d["m_ObjectName"]) ?? "", dn = Str(d["m_DestroyedObjectName"]) ?? "", ds = Str(d["m_Description"]) ?? "";
            objectInfosByKey[e.Info.Key] = new ObjectInfoOut
            {
                AssetName = e.Info.Name,
                Id = guid,
                IdInt4 = GuidToInt4(guid),
                Bundle = e.Info.Bundle,
                PathId = e.Info.Key.PathId,
                ObjectNameLocId = on.Length > 0 ? on : null,
                ObjectName = L(on, e.Info.Name + ".m_ObjectName"),
                DestroyedObjectNameLocId = dn.Length > 0 ? dn : null,
                DestroyedObjectName = L(dn, e.Info.Name + ".m_DestroyedObjectName"),
                DescriptionLocId = ds.Length > 0 ? ds : null,
                Description = L(ds, e.Info.Name + ".m_Description"),
                Priority = d["m_Priority"].AsInt,
                CategoryColor = d["m_CategoryColor"].IsDummy ? null : Hex(d["m_CategoryColor"]),
                SpeechAsset = speechKey == null ? null : (scan.Index.TryGetValue(speechKey.Value, out var sa) ? sa.Name : $"external({speechKey})"),
                TriggerSpeechMoreThanOnce = !d["m_TriggerSpeechMoreThanOnce"].IsDummy && d["m_TriggerSpeechMoreThanOnce"].AsBool,
                Categories = cats,
            };
        }

        // Structure parts
        var structureParts = new List<StructurePartOut>();
        foreach (var e in scan.Extracted["StructurePartAsset"])
        {
            var d = e.Field["Data"];
            string ctx = e.Info.Name;
            var oiKey = e.Resolve(d["m_ObjectInfoAsset"]);
            ObjectInfoOut? oi = oiKey != null && objectInfosByKey.TryGetValue(oiKey.Value, out var o) ? o : null;
            if (oiKey != null && oi == null) RefName(oiKey, ctx + ".m_ObjectInfoAsset");

            var ctKey = e.Resolve(d["m_CuttingTargetableAsset"]);
            CuttingOut? ct = ctKey != null && cuttingByKey.TryGetValue(ctKey.Value, out var c) ? c : null;
            if (ctKey != null && ct == null) RefName(ctKey, ctx + ".m_CuttingTargetableAsset");

            BlueprintOut? bp = null;
            SalvageOut? salv = null;
            string? salvSource = null;
            var salvKey = e.Resolve(d["m_SalvageableAsset"]);
            if (salvKey != null && salvageByKey.TryGetValue(salvKey.Value, out var s)) { salv = s; salvSource = "m_SalvageableAsset"; }
            else if (salvKey != null) RefName(salvKey, ctx + ".m_SalvageableAsset");

            var bpKey = e.Resolve(d["m_MigrationEntityBlueprint"]);
            if (bpKey != null)
            {
                var bpName = scan.Index.TryGetValue(bpKey.Value, out var bpa) ? bpa.Name : RefName(bpKey, ctx + ".m_MigrationEntityBlueprint");
                var comps = new List<string>();
                if (blueprintComponents.TryGetValue(bpKey.Value, out var compKeys))
                    foreach (var ck in compKeys)
                    {
                        comps.Add(RefName(ck, ctx + ".blueprint." + bpName));
                        if (salv == null && salvageByKey.TryGetValue(ck, out var bs)) { salv = bs; salvSource = "migrationEntityBlueprint"; }
                    }
                bp = new BlueprintOut { AssetName = bpName, Components = comps };
            }

            var refs = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (var f in d.Children)
            {
                if (!f.TypeName.StartsWith("PPtr<", StringComparison.Ordinal)) continue;
                if (f.FieldName is "m_ObjectInfoAsset" or "m_CuttingTargetableAsset" or "m_SalvageableAsset" or "m_MigrationEntityBlueprint") continue;
                var k = e.Resolve(f);
                refs[Camel(f.FieldName)] = k == null ? null : RefName(k, ctx + "." + f.FieldName);
            }

            var sp = new StructurePartOut
            {
                AssetName = e.Info.Name,
                Bundle = e.Info.Bundle,
                PathId = e.Info.Key.PathId,
                DisplaysInfo = d["m_DisplaysInfo"].AsBool,
                ApplySurfaceDamage = !d["m_ApplySurfaceDamage"].IsDummy && d["m_ApplySurfaceDamage"].AsBool,
                ObjectInfo = oi?.AssetName,
                ObjectInfoName = oi?.ObjectName,
                CuttingTargetable = ct,
                Salvageable = salv,
                SalvageableSource = salvSource,
                MigrationEntityBlueprint = bp,
                OtherRefs = refs,
            };
            structureParts.Add(sp);
            if (oi != null) meta.StructurePartsWithObjectInfo++;
            if (ct != null) meta.StructurePartsWithCutting++;

            if (oi != null)
            {
                oi.StructureParts.Add(sp.AssetName);
                if (ct != null && !oi.Cutting.Any(x => x.AssetName == ct.AssetName)) oi.Cutting.Add(ct);
                if (salv != null) foreach (var dst in salv.Destinations) if (!oi.SalvageDestinations.Contains(dst)) oi.SalvageDestinations.Add(dst);
                if (bp != null)
                    foreach (var comp in bp.Components)
                    {
                        var cls = comp.Split(':')[0];
                        if (!oi.BlueprintComponentClasses.Contains(cls)) oi.BlueprintComponentClasses.Add(cls);
                    }
            }
        }

        foreach (var oi in objectInfosByKey.Values)
        {
            oi.StructureParts.Sort(StringComparer.Ordinal);
            oi.Cutting.Sort((a, b) => a.PowerRating != b.PowerRating ? a.PowerRating.CompareTo(b.PowerRating) : string.CompareOrdinal(a.AssetName, b.AssetName));
            oi.SalvageDestinations.Sort(StringComparer.Ordinal);
            oi.BlueprintComponentClasses.Sort(StringComparer.Ordinal);
        }
        meta.LocUnresolved.Sort(StringComparer.Ordinal);
        meta.LocEmpty.Sort(StringComparer.Ordinal);
        meta.UnresolvedReferences.Sort(StringComparer.Ordinal);

        return new Catalog
        {
            Meta = meta,
            ObjectInfos = objectInfosByKey.Values.OrderBy(o => SortName(o.ObjectName, o.AssetName), StringComparer.OrdinalIgnoreCase).ThenBy(o => o.AssetName, StringComparer.Ordinal).ToList(),
            Categories = categoriesByKey.Values.OrderBy(c => SortName(c.Name, c.AssetName), StringComparer.OrdinalIgnoreCase).ThenBy(c => c.AssetName, StringComparer.Ordinal).ToList(),
            StructureParts = structureParts.OrderBy(s => s.AssetName, StringComparer.Ordinal).ToList(),
            CuttingTargetables = cuttingByKey.Values.OrderBy(c => c.PowerRating).ThenBy(c => c.AssetName, StringComparer.Ordinal).ToList(),
        };
    }

    // Objects without English text sort last.
    static string SortName(string? name, string assetName) => string.IsNullOrWhiteSpace(name) ? "\uffff" + assetName : name;

    static string OneLine(string? s) => string.Join(' ', (s ?? "").Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries));

    public static IEnumerable<string> TextLines(Catalog catalog)
    {
        foreach (var o in catalog.ObjectInfos)
        {
            var name = !string.IsNullOrWhiteSpace(o.ObjectName) ? o.ObjectName
                : o.ObjectNameLocId == null ? "(no name)"
                : o.ObjectName == null ? $"(unresolved {o.ObjectNameLocId})"
                : $"(empty EN text {o.ObjectNameLocId})";
            var cats = string.Join(", ", o.Categories.Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.AssetName : c.Name));
            var desc = OneLine(o.Description);
            if (desc.Length > 120) desc = desc[..120];
            yield return $"{OneLine(name)} | {o.AssetName} | {o.Id} | {cats} | {desc}";
        }
    }
}
