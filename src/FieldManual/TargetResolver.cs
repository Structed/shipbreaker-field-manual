using System;
using BBI.Unity.Game;
using UnityEngine;

namespace FieldManual
{
    internal enum TargetSource
    {
        None,
        GameInfoPanel,
        Cutter,
        Crosshair,
    }

    /// <summary>Cut grades of the player's two cutter modes; null when unknown.</summary>
    internal struct CutterRatings
    {
        public int? Stinger;
        public int? Splitsaw;
    }

    /// <summary>
    /// Finds the structure part the player is currently looking at, preferring the game's own
    /// targeting so the panel agrees with the vanilla object-info HUD.
    /// </summary>
    internal sealed class TargetResolver
    {
        private const float kRaycastRange = 40f;
        private const float kLookupInterval = 1f;

        private readonly RaycastHit[] mHits = new RaycastHit[24];
        private ObjectInfoUIController mInfoUI;
        private CuttingController mCutter;
        private ScalpelController mScalpel;
        private EquipmentController mEquipment;
        private float mNextLookup;

        public static bool InGameplay
        {
            get
            {
                try
                {
                    return GameSession.CurrentGameState == GameSession.GameState.Gameplay;
                }
                catch
                {
                    return false;
                }
            }
        }

        public CutterRatings Ratings
        {
            get
            {
                var ratings = new CutterRatings();
                try
                {
                    if (mCutter != null && mCutter.ActiveCutData != null)
                    {
                        ratings.Splitsaw = mCutter.ActiveCutData.PowerRating;
                    }
                }
                catch
                {
                }
                try
                {
                    if (mScalpel != null && mScalpel.Data != null)
                    {
                        ratings.Stinger = mScalpel.Data.PowerRating;
                    }
                }
                catch
                {
                }
                return ratings;
            }
        }

        public EquipmentController.Equipment CurrentEquipment
        {
            get
            {
                try
                {
                    return mEquipment != null ? mEquipment.CurrentEquipment : EquipmentController.Equipment.None;
                }
                catch
                {
                    return EquipmentController.Equipment.None;
                }
            }
        }

        public StructurePart Resolve(out TargetSource source)
        {
            source = TargetSource.None;
            if (!InGameplay)
            {
                return null;
            }

            RefreshReferences();

            var equipment = CurrentEquipment;
            if (equipment == EquipmentController.Equipment.CuttingTool && TryCutter(out var cutPart))
            {
                source = TargetSource.Cutter;
                return cutPart;
            }

            if (equipment != EquipmentController.Equipment.CuttingTool && mInfoUI != null)
            {
                StructurePart part = null;
                try
                {
                    part = mInfoUI.GetCurrentStructurePart();
                }
                catch (Exception ex)
                {
                    LogOnce("info-ui", ex);
                }
                if (IsAlive(part))
                {
                    source = TargetSource.GameInfoPanel;
                    return part;
                }
            }

            var rayPart = RaycastFromCamera();
            if (rayPart != null)
            {
                source = TargetSource.Crosshair;
                return rayPart;
            }
            return null;
        }

        public void Reset()
        {
            mInfoUI = null;
            mCutter = null;
            mScalpel = null;
            mEquipment = null;
            mNextLookup = 0f;
        }

        private void RefreshReferences()
        {
            if (mInfoUI != null && mCutter != null && mScalpel != null && mEquipment != null)
            {
                return;
            }
            if (Time.unscaledTime < mNextLookup)
            {
                return;
            }
            mNextLookup = Time.unscaledTime + kLookupInterval;
            try
            {
                if (mInfoUI == null) mInfoUI = UnityEngine.Object.FindObjectOfType<ObjectInfoUIController>();
                if (mCutter == null) mCutter = UnityEngine.Object.FindObjectOfType<CuttingController>();
                if (mScalpel == null) mScalpel = UnityEngine.Object.FindObjectOfType<ScalpelController>();
                if (mEquipment == null) mEquipment = UnityEngine.Object.FindObjectOfType<EquipmentController>();
            }
            catch (Exception ex)
            {
                LogOnce("lookup", ex);
            }
        }

        private bool TryCutter(out StructurePart part)
        {
            part = null;
            if (mCutter == null)
            {
                return false;
            }
            try
            {
                if (mCutter.TryGetTargetableInRange(out part, out _, uiInfo: true) && IsAlive(part))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogOnce("cutter", ex);
            }
            part = null;
            return false;
        }

        private StructurePart RaycastFromCamera()
        {
            Transform cam = null;
            try
            {
                cam = LynxCameraController.MainCameraTransform;
            }
            catch
            {
            }
            if (cam == null)
            {
                var main = Camera.main;
                cam = main != null ? main.transform : null;
            }
            if (cam == null)
            {
                return null;
            }

            int count = Physics.RaycastNonAlloc(cam.position, cam.forward, mHits, kRaycastRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count <= 0)
            {
                return null;
            }

            float best = float.MaxValue;
            Collider bestCollider = null;
            for (int i = 0; i < count; i++)
            {
                var hit = mHits[i];
                if (hit.collider == null || hit.distance < 0.05f)
                {
                    continue;
                }
                if (hit.distance < best)
                {
                    best = hit.distance;
                    bestCollider = hit.collider;
                }
            }
            if (bestCollider == null)
            {
                return null;
            }
            var part = bestCollider.GetComponentInParent<StructurePart>();
            return IsAlive(part) ? part : null;
        }

        internal static bool IsAlive(StructurePart part)
        {
            return part != null && part.gameObject != null && part.gameObject.activeInHierarchy;
        }

        private static readonly System.Collections.Generic.HashSet<string> sLogged = new System.Collections.Generic.HashSet<string>();

        internal static void LogOnce(string key, Exception ex)
        {
            if (sLogged.Add(key))
            {
                FieldManualPlugin.Log.LogWarning($"[{key}] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
