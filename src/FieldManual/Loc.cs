using System;
using System.Collections.Generic;
using BBI.Unity.Game;

namespace FieldManual
{
    /// <summary>Thin, failure-tolerant wrapper around the game's localization service.</summary>
    internal static class Loc
    {
        private static readonly Dictionary<string, string> sCache = new Dictionary<string, string>();

        public static string Get(string locId, string fallback = null)
        {
            if (string.IsNullOrEmpty(locId))
            {
                return fallback;
            }
            if (sCache.TryGetValue(locId, out var cached))
            {
                return cached ?? fallback;
            }
            string result = null;
            try
            {
                var service = Main.Instance != null ? Main.Instance.LocalizationService : null;
                if (service != null && service.TryLocalize(locId, out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    result = Clean(text);
                }
            }
            catch (Exception ex)
            {
                TargetResolver.LogOnce("loc", ex);
            }
            sCache[locId] = result;
            return result ?? fallback;
        }

        public static string Get(int numericId, string fallback) => Get("LocID:" + numericId, fallback);

        public static void ClearCache() => sCache.Clear();

        // Game strings may contain TextMeshPro rich-text tags that IMGUI cannot render.
        internal static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
            {
                return text?.Trim();
            }
            var sb = new System.Text.StringBuilder(text.Length);
            bool inTag = false;
            foreach (char c in text)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>' && inTag) { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
