using System;
using System.Collections.Generic;
using System.Linq;
using BBI.Unity.Game;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace FieldManual
{
    internal enum SalvageDestination
    {
        Unknown,
        Barge,
        Processor,
        Furnace,
        Pickup,
        Destroy,
    }

    [Flags]
    internal enum Hazard
    {
        None = 0,
        Explosive = 1 << 0,
        Exploding = 1 << 1,
        Exploded = 1 << 2,
        Flammable = 1 << 3,
        Burning = 1 << 4,
        IgnitesFromElectricity = 1 << 5,
        Electrifiable = 1 << 6,
        Electrified = 1 << 7,
        ElectricitySource = 1 << 8,
        Freezable = 1 << 9,
        Frozen = 1 << 10,
        Leakable = 1 << 11,
        Leaking = 1 << 12,
        Radioactive = 1 << 13,
        Irradiated = 1 << 14,
        Reactor = 1 << 15,
        ActiveReactor = 1 << 16,
        PowerGenerator = 1 << 17,
        SealsPressurizedRoom = 1 << 18,
        SealsRoom = 1 << 19,
    }

    internal sealed class InteractionInfo
    {
        public string Name;
        public bool Available;
        public bool Done;
    }

    /// <summary>Snapshot of everything the panel shows about one target.</summary>
    internal sealed class PartInfo
    {
        public StructurePart Part;
        public int PartId;

        public string ObjectAsset;
        public string ObjectGuid;
        public string PartAsset;
        public List<string> CategoryAssets = new List<string>();

        public string Name;
        public string Description;
        public List<string> Categories = new List<string>();
        public Color CategoryColor = Color.white;

        public SalvageDestination Destination;
        public string Value;
        public float Mass;
        public float AttachedMass;
        public bool IsGroup;

        public bool HasCutInfo;
        public bool Cuttable;
        public int PowerRating;
        public CuttingTargetable.CuttableBy CuttableBy;
        public int? StingerRating;
        public int? SplitsawRating;

        public bool IsJoint;
        public float JointDurability;
        public BreakableJointComponent.BreakableBy JointBreakableBy;

        public Hazard Hazards;
        public ElementalType ExplosiveType;
        public float? ExplodesIn;
        public bool GeneratorInert;
        public PowerGeneratorComponent.PowerDownType InertReason;
        public int FusesTotal;
        public int FusesLive;

        // Power generator fuse (breaker) state; only safe to pull while no indicator light is lit (stage 0).
        public BreakerController Breaker;
        public bool IsBreaker;
        public bool BreakerInert;
        /// <summary>Number of lit indicator lights, as displayed by the fuse; -1 if unknown.</summary>
        public int BreakerStage = -1;
        public int BreakerStages;
        public float BreakerStageLength;
        public float BreakerStageElapsed;
        public int PressurizedRooms;
        public bool SealsAirlock;
        public bool IsSpaceTruckPart;

        public List<string> Systems = new List<string>();
        public List<InteractionInfo> Interactions = new List<InteractionInfo>();

        public bool Has(Hazard h) => (Hazards & h) != 0;

        internal const CuttingTargetable.CuttableBy SplitsawModes =
            CuttingTargetable.CuttableBy.QuickCut | CuttingTargetable.CuttableBy.AimCut | CuttingTargetable.CuttableBy.MegaCut;

        public bool StingerCanCut => (CuttableBy & CuttingTargetable.CuttableBy.Scalpel) != 0 && (!StingerRating.HasValue || StingerRating.Value >= PowerRating);
        public bool SplitsawCanCut => (CuttableBy & SplitsawModes) != 0 && (!SplitsawRating.HasValue || SplitsawRating.Value >= PowerRating);

        /// <summary>Best grade among the player's tools that are allowed to cut this object, or null if unknown.</summary>
        public int? CutterRating
        {
            get
            {
                int? best = null;
                if ((CuttableBy & CuttingTargetable.CuttableBy.Scalpel) != 0 && StingerRating.HasValue) best = StingerRating;
                if ((CuttableBy & SplitsawModes) != 0 && SplitsawRating.HasValue && (!best.HasValue || SplitsawRating.Value > best.Value)) best = SplitsawRating;
                return best;
            }
        }

        public bool AboveCutter => HasCutInfo && Cuttable && CutterRating.HasValue && CutterRating.Value < PowerRating;
    }

    internal static class PartInspector
    {
        private static readonly (Type type, string system, string role)[] sSystemComponents = BuildSystemTable();
        private static readonly System.Reflection.FieldInfo sBreakerStageField =
            typeof(BreakerController).GetField("mCurrentStage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static PartInfo Inspect(StructurePart part, CutterRatings ratings)
        {
            var info = new PartInfo
            {
                Part = part,
                PartId = part.GetInstanceID(),
                StingerRating = ratings.Stinger,
                SplitsawRating = ratings.Splitsaw,
            };

            Safe("identity", () => ReadIdentity(part, info));
            Safe("cutting", () => ReadCutting(part, info));
            Safe("joint", () => ReadJoint(part, info));
            Safe("value", () =>
            {
                if (StructureHelpers.TryGetSalvageValue(part, out var value, "F0"))
                {
                    info.Value = value;
                }
            });
            Safe("spacetruck", () => info.IsSpaceTruckPart = part.IsSpaceTruckPart);
            Safe("ecs", () => ReadEcs(part, info));
            Safe("interactions", () => ReadInteractions(part, info));
            Safe("breaker-find", () => FindBreaker(part, info));
            Safe("breaker", () => RefreshBreaker(info));
            return info;
        }

        /// <summary>Re-reads the state that can change while the player keeps looking at the same object.</summary>
        public static void RefreshDynamic(PartInfo info, CutterRatings ratings)
        {
            if (!TargetResolver.IsAlive(info.Part))
            {
                return;
            }
            info.StingerRating = ratings.Stinger;
            info.SplitsawRating = ratings.Splitsaw;
            info.Hazards = Hazard.None;
            info.ExplodesIn = null;
            info.Systems.Clear();
            info.Interactions.Clear();
            info.PressurizedRooms = 0;
            info.SealsAirlock = false;
            Safe("joint", () => ReadJoint(info.Part, info));
            Safe("ecs", () => ReadEcs(info.Part, info));
            Safe("interactions", () => ReadInteractions(info.Part, info));
            Safe("breaker", () => RefreshBreaker(info));
        }

        /// <summary>Cheap per-frame update of a fuse's indicator-light stage. Returns true if the displayed state changed.</summary>
        public static bool RefreshBreaker(PartInfo info)
        {
            if (!info.IsBreaker || info.Breaker == null)
            {
                return false;
            }
            var ebc = info.Breaker.GetComponent<EntityBlueprintComponent>();
            if (!EntityBlueprintComponent.IsValid(ebc))
            {
                return false;
            }
            var em = ebc.EntityManager;
            if (!em.Exists(ebc.Entity) || !em.TryGetComponent<BreakerComponent>(ebc.Entity, out var breaker))
            {
                return false;
            }
            bool inert = (bool)breaker.IsInert;
            // BreakerSystem hands the controller the stage *before* advancing the ECS counter, so the lights (and
            // whether pulling is safe) follow the controller's own stage, which lags the ECS value by one step.
            int stage = sBreakerStageField != null ? (int)sBreakerStageField.GetValue(info.Breaker) : -1;
            bool changed = inert != info.BreakerInert || stage != info.BreakerStage || breaker.StageCount != info.BreakerStages;
            info.BreakerInert = inert;
            info.BreakerStage = stage;
            info.BreakerStages = breaker.StageCount;
            info.BreakerStageLength = breaker.StageLengthSeconds;
            info.BreakerStageElapsed = breaker.StageElapsedSeconds;
            return changed;
        }

        private static void FindBreaker(StructurePart part, PartInfo info)
        {
            BreakerController breaker = part.GetComponentInParent<BreakerController>();
            if (breaker == null && part.IsPartOfGroup && part.Group != null)
            {
                breaker = part.Group.GetComponentInChildren<BreakerController>(true);
            }
            info.Breaker = breaker;
            info.IsBreaker = breaker != null;
        }

        private static void ReadIdentity(StructurePart part, PartInfo info)
        {
            info.IsGroup = part.IsPartOfGroup;
            var partAsset = part.StructurePartAsset;
            info.PartAsset = partAsset != null ? partAsset.name : null;

            var objectInfo = part.ObjectInfoAsset;
            if (objectInfo == null)
            {
                info.Name = Pretty(info.PartAsset) ?? part.gameObject.name;
                return;
            }

            info.ObjectAsset = objectInfo.name;
            info.ObjectGuid = objectInfo.AssetGUID;
            var data = objectInfo.Data;
            info.Name = Loc.Get(data.ObjectName) ?? Pretty(objectInfo.name);
            info.Description = Loc.Get(data.Description);
            info.CategoryColor = data.CategoryColor;
            if (data.Categories != null)
            {
                foreach (var category in data.Categories)
                {
                    if (category == null) continue;
                    info.CategoryAssets.Add(category.name);
                    var label = Loc.Get(category.CategoryName);
                    if (!string.IsNullOrEmpty(label) && !info.Categories.Contains(label))
                    {
                        info.Categories.Add(label);
                    }
                }
            }
        }

        private static void ReadCutting(StructurePart part, PartInfo info)
        {
            var targetable = part.CuttingTargetable;
            if (targetable == null || targetable.TargetableAsset == null)
            {
                return;
            }
            info.HasCutInfo = true;
            info.Cuttable = targetable.IsCuttable;
            info.PowerRating = targetable.PowerRating;
            info.CuttableBy = targetable.IsCuttableBy;
        }

        private static void ReadJoint(StructurePart part, PartInfo info)
        {
            var joint = part.BreakableJoint;
            info.IsJoint = joint != null && !joint.IsBroken;
            info.JointDurability = info.IsJoint ? joint.GetJointDurability : 0f;
            info.JointBreakableBy = 0;
            if (info.IsJoint && joint.BreakableJointAsset != null)
            {
                info.JointBreakableBy = joint.BreakableJointAsset.Data.BreakableBy;
            }
        }

        private static void ReadEcs(StructurePart part, PartInfo info)
        {
            if (!part.TryGetEntity(out var em, out var partEntity) || !em.Exists(partEntity))
            {
                return;
            }

            var entities = new List<Entity> { partEntity };
            Entity groupEntity = Entity.Null;
            if (part.IsPartOfGroup && part.Group.TryGetEntity(out _, out groupEntity) && em.Exists(groupEntity) && groupEntity != partEntity)
            {
                entities.Add(groupEntity);
            }

            // Mass, the same way the vanilla HUD reads it.
            if (em.TryGetComponent<MassComponent>(partEntity, out var mass))
            {
                info.Mass = mass.Mass;
            }
            if (em.TryGetComponent<StructureGroupMember>(partEntity, out var member) && em.Exists(member.GroupEntity) && em.TryGetComponent<MassComponent>(member.GroupEntity, out var groupMass))
            {
                info.Mass = groupMass.Mass;
            }
            if (em.TryGetComponent<HierarchyMember>(partEntity, out var hierarchy) && em.Exists(hierarchy.Parent) && em.TryGetComponent<MassComponent>(hierarchy.Parent, out var totalMass))
            {
                info.AttachedMass = totalMass.Mass;
            }

            // Salvage destination: group entity wins, like ObjectSalvageInfoUIController.
            Entity salvageEntity = Entity.Null;
            if (groupEntity != Entity.Null && em.HasComponent<SalvageableComponent>(groupEntity)) salvageEntity = groupEntity;
            else if (em.HasComponent<SalvageableComponent>(partEntity)) salvageEntity = partEntity;
            info.Destination = SalvageDestination.Unknown;
            if (salvageEntity != Entity.Null)
            {
                var s = em.GetComponentData<SalvageableComponent>(salvageEntity);
                if ((bool)s.CanBeBargeSalvaged) info.Destination = SalvageDestination.Barge;
                else if ((bool)s.CanBeProcessorSalvaged) info.Destination = SalvageDestination.Processor;
                else if ((bool)s.CanBeFurnaceSalvaged) info.Destination = SalvageDestination.Furnace;
                else if ((bool)s.CanBePickupSalvaged) info.Destination = SalvageDestination.Pickup;
                else if ((bool)s.CanBeDestroySalvaged) info.Destination = SalvageDestination.Destroy;
            }

            foreach (var e in entities)
            {
                if (em.TryGetComponent<ExplosiveComponent>(e, out var explosive))
                {
                    info.Hazards |= Hazard.Explosive;
                    info.ExplosiveType = explosive.ExplosiveType;
                }
                Flag<ExplodingComponent>(em, e, info, Hazard.Exploding);
                if (em.TryGetComponent<ExplodingComponent>(e, out var exploding))
                {
                    info.ExplodesIn = Mathf.Max(0f, exploding.RemainingTime);
                }
                Flag<ExplodedComponent>(em, e, info, Hazard.Exploded);
                Flag<FlammableComponent>(em, e, info, Hazard.Flammable);
                Flag<BurningComponent>(em, e, info, Hazard.Burning);
                Flag<IgnitesFromElectricity>(em, e, info, Hazard.IgnitesFromElectricity);
                Flag<ElectrifiableComponent>(em, e, info, Hazard.Electrifiable);
                Flag<ElectrifiedComponent>(em, e, info, Hazard.Electrified);
                Flag<ElectricitySource>(em, e, info, Hazard.ElectricitySource);
                Flag<FreezableComponent>(em, e, info, Hazard.Freezable);
                Flag<FrozenComponent>(em, e, info, Hazard.Frozen);
                Flag<LeakableComponent>(em, e, info, Hazard.Leakable);
                Flag<LeakComponent>(em, e, info, Hazard.Leaking);
                Flag<RadioactiveComponent>(em, e, info, Hazard.Radioactive);
                Flag<IrradiatedComponent>(em, e, info, Hazard.Irradiated);
                Flag<ReactorComponent>(em, e, info, Hazard.Reactor);
                Flag<ActiveReactorComponent>(em, e, info, Hazard.ActiveReactor);
                if (em.TryGetComponent<PowerGeneratorComponent>(e, out var generator))
                {
                    info.Hazards |= Hazard.PowerGenerator;
                    info.GeneratorInert = (bool)generator.IsInert;
                    info.InertReason = generator.InertReason;
                }

                foreach (var (type, system, role) in sSystemComponents)
                {
                    if (em.HasComponent(e, ComponentType.ReadOnly(type)))
                    {
                        AddSystem(info, system, role);
                    }
                }
            }

            ReadRooms(em, partEntity, info);
            if (info.Has(Hazard.PowerGenerator))
            {
                CountFuses(em, info);
            }
        }

        private static void CountFuses(EntityManager em, PartInfo info)
        {
            info.FusesTotal = 0;
            info.FusesLive = 0;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<BreakerComponent>(), ComponentType.ReadOnly<BreakerController>());
            try
            {
                if (query.IsEmptyIgnoreFilter)
                {
                    return;
                }
                using (var breakers = query.ToComponentDataArray<BreakerComponent>(Allocator.TempJob))
                {
                    info.FusesTotal = breakers.Length;
                    for (int i = 0; i < breakers.Length; i++)
                    {
                        if (!(bool)breakers[i].IsInert) info.FusesLive++;
                    }
                }
            }
            finally
            {
                query.Dispose();
            }
        }

        private static void ReadRooms(EntityManager em, Entity panel, PartInfo info)
        {
            var rooms = new HashSet<Entity>();
            if (em.HasComponent<RoomConnection>(panel))
            {
                var buffer = em.GetBuffer<RoomConnection>(panel);
                for (int i = 0; i < buffer.Length; i++) rooms.Add(buffer[i].Value);
            }
            if (em.HasComponent<OpeningConnection>(panel))
            {
                var buffer = em.GetBuffer<OpeningConnection>(panel);
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (em.Exists(buffer[i].Value) && em.TryGetComponent<OpeningComponent>(buffer[i].Value, out var opening))
                    {
                        if (opening.RoomA.IsValid) rooms.Add(opening.RoomA.Entity);
                        if (opening.RoomB.IsValid) rooms.Add(opening.RoomB.Entity);
                    }
                }
            }
            if (rooms.Count == 0)
            {
                return;
            }
            info.Hazards |= Hazard.SealsRoom;
            foreach (var room in rooms)
            {
                if (!em.Exists(room) || !em.TryGetComponent<RoomComponent>(room, out var rc))
                {
                    continue;
                }
                if (rc.AirPressure > 0.01f && !(bool)rc.Breached)
                {
                    info.PressurizedRooms++;
                    info.Hazards |= Hazard.SealsPressurizedRoom;
                }
                if ((bool)rc.IsAirlock)
                {
                    info.SealsAirlock = true;
                }
            }
        }

        private static void ReadInteractions(StructurePart part, PartInfo info)
        {
            var root = part.IsPartOfGroup && part.Group != null ? part.Group.gameObject : part.gameObject;
            var interactables = root.GetComponentsInChildren<InteractableObject>(false);
            foreach (var io in interactables)
            {
                if (io == null || io.Asset == null) continue;
                var data = io.Asset.Data;
                bool available = io.IsInteractable;
                string name = available ? Loc.Get(data.ObjectName) : (Loc.Get(data.DisabledObjectName) ?? Loc.Get(data.ObjectName));
                if (string.IsNullOrEmpty(name))
                {
                    name = Pretty(io.Asset.name);
                }
                if (string.IsNullOrEmpty(name) || info.Interactions.Any(x => x.Name == name))
                {
                    continue;
                }
                info.Interactions.Add(new InteractionInfo { Name = name, Available = available, Done = io.IsComplete });
            }
        }

        /// <summary>Everything known about a target, for the debug inspect key.</summary>
        public static string Describe(PartInfo info)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Target '{info.Name}'");
            sb.AppendLine($"  ObjectInfoAsset: {info.ObjectAsset ?? "-"} (guid {info.ObjectGuid ?? "-"})");
            sb.AppendLine($"  StructurePartAsset: {info.PartAsset ?? "-"}  grouped: {info.IsGroup}");
            sb.AppendLine($"  Categories: {string.Join(", ", info.CategoryAssets)}  [{string.Join(", ", info.Categories)}]");
            sb.AppendLine($"  Destination: {info.Destination}  value: {info.Value ?? "-"}  mass: {info.Mass:0.#} (attached {info.AttachedMass:0.#})");
            sb.AppendLine($"  Cut: has={info.HasCutInfo} cuttable={info.Cuttable} grade={info.PowerRating} by={info.CuttableBy} stinger={info.StingerRating?.ToString() ?? "-"} splitsaw={info.SplitsawRating?.ToString() ?? "-"}");
            sb.AppendLine($"  Joint: {info.IsJoint} durability={info.JointDurability:0.00} breakableBy={info.JointBreakableBy}");
            sb.AppendLine($"  Hazards: {info.Hazards}  explosive={info.ExplosiveType}  generatorInert={info.GeneratorInert}/{info.InertReason}  fuses={info.FusesLive}/{info.FusesTotal}  pressurizedRooms={info.PressurizedRooms} airlock={info.SealsAirlock}");
            sb.AppendLine($"  Fuse: {info.IsBreaker} inert={info.BreakerInert} stage={info.BreakerStage}/{info.BreakerStages} stageLength={info.BreakerStageLength:0.00}s");
            sb.AppendLine($"  Systems: {string.Join(", ", info.Systems)}");
            sb.AppendLine($"  Interactions: {string.Join(", ", info.Interactions.Select(i => $"{i.Name}({(i.Available ? "on" : "off")}{(i.Done ? ",done" : "")})"))}");
            var match = GuideDatabase.Match(info);
            sb.AppendLine($"  Guide entries: {(match.Count == 0 ? "none" : string.Join(", ", match.Select(m => m.Id)))}");

            Safe("describe-ecs", () =>
            {
                var part = info.Part;
                if (TargetResolver.IsAlive(part) && part.TryGetEntity(out var em, out var e) && em.Exists(e))
                {
                    sb.AppendLine("  Part components: " + ComponentList(em, e));
                    if (part.IsPartOfGroup && part.Group.TryGetEntity(out _, out var g) && em.Exists(g))
                    {
                        sb.AppendLine("  Group components: " + ComponentList(em, g));
                    }
                }
            });
            return sb.ToString();
        }

        private static string ComponentList(EntityManager em, Entity e)
        {
            var types = em.GetComponentTypes(e, Allocator.Temp);
            try
            {
                var names = new List<string>(types.Length);
                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i].GetManagedType();
                    names.Add(t != null ? t.Name : types[i].ToString());
                }
                names.Sort(StringComparer.Ordinal);
                return string.Join(", ", names);
            }
            finally
            {
                types.Dispose();
            }
        }

        private static void Flag<T>(EntityManager em, Entity e, PartInfo info, Hazard hazard) where T : struct, IComponentData
        {
            if (em.HasComponent<T>(e))
            {
                info.Hazards |= hazard;
            }
        }

        private static void AddSystem(PartInfo info, string system, string role)
        {
            var line = $"{system}: {role}";
            if (!info.Systems.Contains(line))
            {
                info.Systems.Add(line);
            }
        }

        private static (Type, string, string)[] BuildSystemTable()
        {
            var list = new List<(Type, string, string)>();
            var asm = typeof(MassComponent).Assembly;
            string[] systems = { "Electricity", "Fuel", "Cryo", "Pressure" };
            string[] labels = { "Power", "Fuel", "Coolant", "Pressure" };
            string[] roles = { "Source", "Provider", "Receiver" };
            string[] roleLabels = { "source", "supplies others", "consumer" };
            for (int s = 0; s < systems.Length; s++)
            {
                for (int r = 0; r < roles.Length; r++)
                {
                    foreach (var scope in new[] { "Local", "Global" })
                    {
                        var type = asm.GetType($"BBI.Unity.Game.Machine{scope}{systems[s]}{roles[r]}Component", false);
                        if (type != null)
                        {
                            list.Add((type, labels[s], roleLabels[r]));
                        }
                    }
                }
            }
            return list.ToArray();
        }

        internal static string Pretty(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;
            var s = assetName.Replace('_', ' ').Trim();
            foreach (var prefix in new[] { "OI ", "ObjectInfo ", "SPA ", "SP " })
            {
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s.Substring(prefix.Length);
            }
            return s;
        }

        private static void Safe(string key, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                TargetResolver.LogOnce("inspect-" + key, ex);
            }
        }
    }
}
