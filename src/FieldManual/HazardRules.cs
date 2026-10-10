using System;
using System.Collections.Generic;
using System.Linq;
using BBI.Unity.Game;
using UnityEngine;

namespace FieldManual
{
    internal enum Severity
    {
        Danger,
        Caution,
        Info,
        Good,
    }

    internal struct Badge
    {
        public string Text;
        public Severity Severity;
    }

    internal struct Notice
    {
        public string Text;
        public Severity Severity;
    }

    /// <summary>Everything the panel renders for one target, already in display order.</summary>
    internal sealed class HelpView
    {
        public PartInfo Info;
        public string Summary;
        public List<Badge> Badges = new List<Badge>();
        public List<Notice> RightNow = new List<Notice>();
        public List<string> Actions = new List<string>();
        public List<string> Dismantle = new List<string>();
        public bool DismantleIsAutomatic;
        public List<string> Warnings = new List<string>();
        public List<string> Tips = new List<string>();
        public List<string> GuideIds = new List<string>();
        public bool HasCuratedEntry;
        /// <summary>The body must not be cut (volatile, live, or flagged "dontCut" in the guide).</summary>
        public bool DontCut;
        public int Version;
    }

    /// <summary>
    /// Turns live game state into badges, "right now" warnings and fallback advice, and merges it
    /// with the curated guide entries.
    /// </summary>
    internal static class HazardRules
    {
        private const int kMaxWarnings = 5;
        private const int kMaxTips = 3;
        private const float kHeavyMassKg = 1000f;

        public static HelpView Build(PartInfo info, List<GuideHit> hits)
        {
            var view = new HelpView { Info = info };
            hits = hits ?? new List<GuideHit>();
            view.GuideIds.AddRange(hits.Select(h => h.Id).Where(id => !string.IsNullOrEmpty(id)));
            view.HasCuratedEntry = GuideDatabase.HasSpecificMatch(hits);
            view.DontCut = hits.Any(h => h.Entry.DontCut)
                || info.Has(Hazard.Explosive) || info.Has(Hazard.Leakable)
                || info.Has(Hazard.Reactor) || info.Has(Hazard.ActiveReactor)
                || (info.Has(Hazard.PowerGenerator) && !info.GeneratorInert)
                || info.IsBreaker;

            var summaryHit = hits.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.Entry.Summary));
            view.Summary = summaryHit != null ? Expand(summaryHit.Entry.Summary, info) : null;

            RefreshLive(view);
            BuildActions(view, hits);
            BuildDismantle(view, hits);

            foreach (var hit in hits)
            {
                AddAll(view.Warnings, hit.Entry.Warnings, info, kMaxWarnings);
                AddAll(view.Tips, hit.Entry.Tips, info, kMaxTips);
            }
            return view;
        }

        /// <summary>Recomputes the parts of the view that depend on fast-changing state.</summary>
        public static void RefreshLive(HelpView view)
        {
            view.Badges.Clear();
            view.RightNow.Clear();
            BuildBadges(view.Info, view.Badges);
            BuildRightNow(view.Info, view.DontCut, view.RightNow);
            view.Version++;
        }

        public static Color ColorOf(Severity severity)
        {
            switch (severity)
            {
                case Severity.Danger: return new Color(1f, 0.38f, 0.32f);
                case Severity.Caution: return new Color(1f, 0.74f, 0.25f);
                case Severity.Good: return new Color(0.45f, 0.92f, 0.52f);
                default: return new Color(0.58f, 0.8f, 1f);
            }
        }

        public static Color ColorOf(SalvageDestination destination)
        {
            switch (destination)
            {
                case SalvageDestination.Barge: return new Color(0.42f, 0.92f, 0.48f);
                case SalvageDestination.Processor: return new Color(0.35f, 0.85f, 1f);
                case SalvageDestination.Furnace: return new Color(1f, 0.42f, 0.32f);
                case SalvageDestination.Pickup: return new Color(1f, 0.85f, 0.35f);
                case SalvageDestination.Destroy: return new Color(0.7f, 0.7f, 0.72f);
                default: return new Color(0.6f, 0.64f, 0.68f);
            }
        }

        public static string DestinationLabel(SalvageDestination destination)
        {
            switch (destination)
            {
                case SalvageDestination.Barge: return Loc.Get(223, "Barge");
                case SalvageDestination.Processor: return Loc.Get(224, "Processor");
                case SalvageDestination.Furnace: return Loc.Get(225, "Furnace");
                case SalvageDestination.Pickup: return Loc.Get(226, "Pickup");
                case SalvageDestination.Destroy: return Loc.Get(9642, "Destroy");
                default: return null;
            }
        }

        public static string FormatMass(float kg)
        {
            if (kg <= 0f) return null;
            return kg >= 1000f ? $"{kg / 1000f:0.0} t" : $"{kg:0} kg";
        }

        public static string CutTools(CuttingTargetable.CuttableBy flags)
        {
            var tools = new List<string>();
            if ((flags & CuttingTargetable.CuttableBy.Scalpel) != 0) tools.Add("Stinger");
            if ((flags & PartInfo.SplitsawModes) != 0) tools.Add("Splitsaw");
            if ((flags & CuttingTargetable.CuttableBy.DemoCharge) != 0) tools.Add("Demo Charge");
            return tools.Count == 0 ? null : JoinOr(tools);
        }

        /// <summary>The tools that can cut the target with the player's current cutter grades.</summary>
        public static string UsableCutTools(PartInfo info)
        {
            var tools = new List<string>();
            if (info.StingerCanCut) tools.Add("Stinger");
            if (info.SplitsawCanCut) tools.Add("Splitsaw");
            if ((info.CuttableBy & CuttingTargetable.CuttableBy.DemoCharge) != 0) tools.Add("Demo Charge");
            return tools.Count == 0 ? null : JoinOr(tools);
        }

        public static string JointMethods(BreakableJointComponent.BreakableBy flags)
        {
            var methods = new List<string>();
            if ((flags & BreakableJointComponent.BreakableBy.Grapple) != 0) methods.Add("Grapple");
            if ((flags & BreakableJointComponent.BreakableBy.GrappleThrow) != 0 && (flags & BreakableJointComponent.BreakableBy.Grapple) == 0) methods.Add("a Grapple push");
            if ((flags & BreakableJointComponent.BreakableBy.Tether) != 0) methods.Add("Tethers");
            return methods.Count == 0 ? null : JoinOr(methods);
        }

        private static void BuildBadges(PartInfo info, List<Badge> badges)
        {
            void Add(string text, Severity severity) => badges.Add(new Badge { Text = text, Severity = severity });

            if (info.Has(Hazard.ActiveReactor)) Add("MELTDOWN", Severity.Danger);
            else if (info.Has(Hazard.Reactor)) Add("REACTOR", Severity.Danger);
            if (info.Has(Hazard.Exploding)) Add("EXPLODING", Severity.Danger);
            if (info.Has(Hazard.Burning)) Add("ON FIRE", Severity.Danger);
            if (info.Has(Hazard.Electrified)) Add("ELECTRIFIED", Severity.Danger);
            if (info.Has(Hazard.SealsPressurizedRoom)) Add("PRESSURE SEAL", Severity.Danger);
            if (info.Has(Hazard.Leaking)) Add("LEAKING", Severity.Danger);

            if (info.Has(Hazard.Explosive) && !info.Has(Hazard.Exploding) && !info.Has(Hazard.Reactor))
            {
                Add("EXPLOSIVE", Severity.Caution);
            }
            if (info.Has(Hazard.Flammable) && !info.Has(Hazard.Burning)) Add("FLAMMABLE", Severity.Caution);
            if (info.Has(Hazard.Leakable) && !info.Has(Hazard.Leaking)) Add("LEAKS IF CUT", Severity.Caution);
            if (info.Has(Hazard.Radioactive)) Add("RADIOACTIVE", Severity.Caution);
            if (info.Has(Hazard.Irradiated)) Add("IRRADIATED", Severity.Caution);
            if (info.Has(Hazard.PowerGenerator)) Add(info.GeneratorInert ? "GENERATOR · OFF" : "GENERATOR · LIVE", info.GeneratorInert ? Severity.Good : Severity.Danger);
            if (info.IsBreaker) Add(info.BreakerInert ? "FUSE · PULLED" : "FUSE", info.BreakerInert ? Severity.Good : Severity.Caution);
            if (info.Has(Hazard.ElectricitySource) && !info.Has(Hazard.PowerGenerator) && !info.Has(Hazard.Reactor)) Add("POWER SOURCE", Severity.Caution);
            if (info.Has(Hazard.Frozen)) Add("FROZEN", Severity.Info);

            if (info.HasCutInfo)
            {
                if (!info.Cuttable) Add("UNCUTTABLE", Severity.Info);
                else Add($"CUT GRADE {info.PowerRating}", info.AboveCutter ? Severity.Danger : Severity.Info);
            }
        }

        private static void BuildRightNow(PartInfo info, bool dontCut, List<Notice> notices)
        {
            void Add(Severity severity, string text) => notices.Add(new Notice { Severity = severity, Text = text });

            if (info.Has(Hazard.ActiveReactor))
            {
                Add(Severity.Danger, "Meltdown in progress. Get it into the Barge before its stability runs out.");
            }
            if (info.Has(Hazard.Exploding))
            {
                var when = info.ExplodesIn.HasValue && info.ExplodesIn.Value > 0.05f ? $" in {info.ExplodesIn.Value:0.0} s" : "";
                Add(Severity.Danger, $"About to explode{when}. Get clear now.");
            }
            if (info.Has(Hazard.Burning))
            {
                Add(Severity.Danger, "On fire. Fire spreads to flammable objects nearby; coolant puts it out.");
            }
            if (info.Has(Hazard.Electrified))
            {
                Add(Severity.Danger, "Electrified. It can shock you and arc to nearby metal; wait until it stops arcing.");
            }
            if (info.Has(Hazard.Leaking))
            {
                if (IsCoolant(info)) Add(Severity.Danger, "Leaking coolant. The spray flash-freezes things, making them brittle. Keep your distance.");
                else if (IsFuel(info)) Add(Severity.Danger, "Leaking fuel. Highly combustible; keep fire and arcing electricity away.");
                else Add(Severity.Danger, "Leaking. Keep clear of the leak.");
            }
            if (info.Has(Hazard.SealsPressurizedRoom))
            {
                var rooms = info.PressurizedRooms > 1 ? $"{info.PressurizedRooms} pressurized rooms" : "a pressurized room";
                Add(Severity.Danger, $"Cutting this breaches {rooms} and causes a violent decompression. Depressurize first with the room's Atmosphere Regulator.");
            }

            if (info.IsBreaker)
            {
                if (info.BreakerInert)
                {
                    Add(Severity.Good, "Fuse is disconnected and safe to salvage.");
                }
                else if (info.BreakerStage == 0)
                {
                    Add(Severity.Good, "Fuse lights are OFF. Pull the release lever now.");
                }
                else if (info.BreakerStage > 0)
                {
                    var lit = info.BreakerStages > 0 ? $" ({info.BreakerStage}/{info.BreakerStages} lit)" : "";
                    Add(Severity.Caution, $"Fuse lights are ON{lit}. Wait until every light is off, then pull the lever.");
                }
                else
                {
                    Add(Severity.Caution, "Fuse is live. Pull the lever only while all of its lights are off.");
                }
            }

            if (info.Has(Hazard.PowerGenerator))
            {
                if (!info.GeneratorInert)
                {
                    if (info.FusesTotal > 0)
                    {
                        Add(Severity.Danger, $"{info.FusesLive} of {info.FusesTotal} fuses still live. Pull every fuse before extracting the generator.");
                    }
                    else
                    {
                        Add(Severity.Danger, "Generator is live. Pull its fuses before extracting it.");
                    }
                }
                else if (info.InertReason == PowerGeneratorComponent.PowerDownType.Safe)
                {
                    Add(Severity.Good, "Generator powered down safely. Ready to extract.");
                }
                else
                {
                    Add(Severity.Info, "Generator is inert (it was shut down the hard way).");
                }
            }

            if (info.Has(Hazard.Reactor) && !info.Has(Hazard.ActiveReactor))
            {
                Add(Severity.Caution, "Disconnecting it starts a meltdown. Clear a route to the Barge first.");
            }

            if (info.AboveCutter && dontCut)
            {
                Add(Severity.Caution, $"Cut grade {info.PowerRating}, your cutter is grade {info.CutterRating}. Don't cut into it anyway - free it by cutting what holds it.");
            }
            else if (info.AboveCutter)
            {
                var demo = (info.CuttableBy & CuttingTargetable.CuttableBy.DemoCharge) != 0 ? " A Demo Charge can cut it." : "";
                Add(Severity.Caution, $"Cut grade {info.PowerRating}, your cutter is grade {info.CutterRating}.{demo} Otherwise cut what holds it.");
            }
            else if (info.HasCutInfo && !info.Cuttable && !info.IsJoint && !info.IsBreaker)
            {
                Add(Severity.Info, "Cannot be cut. Free it by cutting what holds it.");
            }
        }

        private static void BuildActions(HelpView view, List<GuideHit> hits)
        {
            var info = view.Info;
            var actions = view.Actions;

            foreach (var interaction in info.Interactions)
            {
                if (interaction.Done) AddUnique(actions, $"{interaction.Name} (done)");
                else if (interaction.Available) AddUnique(actions, $"Interact: {interaction.Name}");
                else AddUnique(actions, $"{interaction.Name} (not available yet)");
            }

            if (info.HasCutInfo && info.Cuttable)
            {
                if (view.DontCut)
                {
                    AddUnique(actions, "Don't cut into it - free it by cutting what holds it");
                }
                else if (info.AboveCutter)
                {
                    var tools = CutTools(info.CuttableBy);
                    if (tools != null) AddUnique(actions, $"Cut with the {tools} - needs grade {info.PowerRating}");
                }
                else
                {
                    var tools = UsableCutTools(info);
                    if (tools != null)
                    {
                        var note = info.Has(Hazard.SealsPressurizedRoom) ? " - after depressurizing the room" : "";
                        AddUnique(actions, $"Cut with the {tools}{note}");
                    }
                }
            }

            if (info.IsJoint)
            {
                var methods = JointMethods(info.JointBreakableBy);
                if (methods != null) AddUnique(actions, $"Pull it loose with the {methods}");
                else if ((info.JointBreakableBy & BreakableJointComponent.BreakableBy.Decompression) != 0) AddUnique(actions, "Breaks loose in a decompression");
            }

            float mass = Mathf.Max(info.Mass, 0f);
            if (mass >= kHeavyMassKg)
            {
                AddUnique(actions, $"Heavy ({FormatMass(mass)}): Tethers pull much harder than the Grapple");
            }

            switch (info.Destination)
            {
                case SalvageDestination.Pickup:
                    AddUnique(actions, "Collect it by hand");
                    break;
                case SalvageDestination.Barge:
                case SalvageDestination.Processor:
                case SalvageDestination.Furnace:
                    AddUnique(actions, $"Salvage it at the {DestinationLabel(info.Destination)}");
                    break;
            }

            foreach (var hit in hits)
            {
                AddAll(actions, hit.Entry.Actions, info, 12);
            }
        }

        private static void BuildDismantle(HelpView view, List<GuideHit> hits)
        {
            var info = view.Info;
            var source = hits.FirstOrDefault(h => h.Entry.Dismantle != null && h.Entry.Dismantle.Count > 0);
            if (source != null)
            {
                AddAll(view.Dismantle, source.Entry.Dismantle, info, 12);
                if (!source.Entry.ReplaceAutomatic)
                {
                    var finalStep = DestinationStep(info);
                    var label = DestinationLabel(info.Destination);
                    bool mentioned = label != null && view.Dismantle.Any(s => s.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (finalStep != null && !mentioned)
                    {
                        view.Dismantle.Add(finalStep);
                    }
                }
                return;
            }

            view.DismantleIsAutomatic = true;
            var steps = view.Dismantle;
            if (info.Has(Hazard.SealsPressurizedRoom))
            {
                steps.Add("Depressurize the room first with its Atmosphere Regulator.");
            }
            if (info.Has(Hazard.PowerGenerator))
            {
                steps.Add("Pull every fuse on the ship first, each only while its lights are off.");
            }
            if (info.Has(Hazard.Reactor))
            {
                steps.Add("Clear a path to the Barge before you disconnect it.");
            }
            if (info.Has(Hazard.ElectricitySource) || info.Has(Hazard.Electrified))
            {
                steps.Add("Check the Scanner's Systems Mode and remove power sources first; electrical parts can arc when cut or knocked.");
            }

            if (view.DontCut)
            {
                steps.Add("Don't cut into the body. Free it at its mounts, joints or cut points instead.");
            }
            else if (info.HasCutInfo && info.Cuttable)
            {
                var tools = UsableCutTools(info);
                if (info.AboveCutter)
                {
                    steps.Add($"It needs cut grade {info.PowerRating}. Cut the parts around it instead.");
                }
                else if (tools != null)
                {
                    steps.Add($"Cut it free with the {tools}.");
                }
            }
            if (info.IsJoint)
            {
                var methods = JointMethods(info.JointBreakableBy);
                if (methods != null) steps.Add($"Pull it loose with the {methods}.");
            }

            var destination = DestinationStep(info);
            if (destination != null)
            {
                steps.Add(destination);
            }
        }

        private static string DestinationStep(PartInfo info)
        {
            switch (info.Destination)
            {
                case SalvageDestination.Barge:
                case SalvageDestination.Processor:
                case SalvageDestination.Furnace:
                    return $"Send it to the {DestinationLabel(info.Destination)}.";
                case SalvageDestination.Pickup:
                    return "Collect it by hand.";
                default:
                    return null;
            }
        }

        private static bool IsCoolant(PartInfo info) =>
            info.ExplosiveType == ElementalType.Coolant
            || info.CategoryAssets.Any(c => c.IndexOf("Coolant", StringComparison.OrdinalIgnoreCase) >= 0)
            || (info.ObjectAsset ?? "").IndexOf("Cryo", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsFuel(PartInfo info) =>
            info.CategoryAssets.Any(c => c.IndexOf("Fuel", StringComparison.OrdinalIgnoreCase) >= 0)
            || (info.ObjectAsset ?? "").IndexOf("Fuel", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Replaces {name}, {destination}, {grade}, {cutter} and {tools} in guide text.</summary>
        internal static string Expand(string text, PartInfo info)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            {
                return text;
            }
            return text
                .Replace("{name}", info.Name ?? "it")
                .Replace("{destination}", DestinationLabel(info.Destination) ?? "salvage point")
                .Replace("{grade}", info.PowerRating.ToString())
                .Replace("{cutter}", info.CutterRating?.ToString() ?? "?")
                .Replace("{tools}", UsableCutTools(info) ?? CutTools(info.CuttableBy) ?? "cutter");
        }

        private static void AddAll(List<string> target, List<string> source, PartInfo info, int max)
        {
            if (source == null) return;
            foreach (var line in source)
            {
                if (target.Count >= max) return;
                if (string.IsNullOrWhiteSpace(line)) continue;
                AddUnique(target, Expand(line.Trim(), info));
            }
        }

        private static void AddUnique(List<string> list, string text)
        {
            if (!list.Any(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(text);
            }
        }

        private static string JoinOr(List<string> items)
        {
            if (items.Count == 1) return items[0];
            return string.Join(", ", items.Take(items.Count - 1)) + " or " + items[items.Count - 1];
        }
    }
}
