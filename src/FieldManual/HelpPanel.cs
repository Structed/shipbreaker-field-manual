using System;
using System.Collections.Generic;
using System.Text;
using BBI.Unity.Game;
using UnityEngine;

namespace FieldManual
{
    /// <summary>Input handling, target tracking and the IMGUI side panel.</summary>
    internal sealed class HelpPanel : MonoBehaviour
    {
        private const float kResolveInterval = 0.1f;
        private const float kDynamicInterval = 0.5f;
        private const float kTargetGrace = 1f;
        private const float kHintDuration = 8f;

        private readonly TargetResolver mResolver = new TargetResolver();
        private readonly HashSet<string> mLoggedUnknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool mVisible;
        private bool mWasInGameplay;
        private float mHintUntil;

        private StructurePart mTargetPart;
        private PartInfo mInfo;
        private HelpView mView;
        private float mNextResolve;
        private float mNextDynamic;
        private float mLastSeen;

        // Layout cache
        private readonly List<DrawItem> mItems = new List<DrawItem>();
        private int mLayoutVersion = -1;
        private HelpView mLayoutView;
        private float mLayoutScale;
        private float mLayoutWidth;
        private float mLayoutHeight;
        private float mLayoutMaxHeight;

        // Styles
        private bool mStylesReady;
        private float mStyleScale;
        private Texture2D mTexWhite;
        private GUIStyle mTitle, mSubtitle, mChips, mSection, mSectionWarn, mBody, mBodyDim, mDesc, mBadge, mFooter, mHint, mEmpty;
        private readonly Dictionary<Severity, GUIStyle> mNoticeStyles = new Dictionary<Severity, GUIStyle>();
        private readonly Dictionary<Severity, GUIStyle> mBadgeStyles = new Dictionary<Severity, GUIStyle>();

        private static readonly Color kPanel = new Color(0.055f, 0.07f, 0.085f, 1f);
        private static readonly Color kAccent = new Color(1f, 0.55f, 0.12f, 1f);
        private static readonly Color kText = new Color(0.93f, 0.95f, 0.97f);
        private static readonly Color kDim = new Color(0.62f, 0.66f, 0.7f);

        private enum DrawKind
        {
            Label,
            Fill,
        }

        private struct DrawItem
        {
            public DrawKind Kind;
            public Rect Rect;
            public string Text;
            public GUIStyle Style;
            public Color Color;
        }

        private void Awake()
        {
            mVisible = FieldManualPlugin.VisibleOnStart.Value;
        }

        private void Update()
        {
            bool inGameplay = TargetResolver.InGameplay;
            if (inGameplay && !mWasInGameplay)
            {
                mResolver.Reset();
                ClearTarget();
                if (FieldManualPlugin.ShowKeyHint.Value && !mVisible)
                {
                    mHintUntil = Time.unscaledTime + kHintDuration;
                }
            }
            mWasInGameplay = inGameplay;
            if (!inGameplay)
            {
                return;
            }

            HandleKeys();

            if (!mVisible)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= mNextResolve)
            {
                mNextResolve = now + kResolveInterval;
                Tick(now);
            }
        }

        private void HandleKeys()
        {
            try
            {
                if (FieldManualPlugin.ToggleKey.Value.IsDown())
                {
                    mVisible = !mVisible;
                    mHintUntil = 0f;
                    if (!mVisible) ClearTarget();
                    else mNextResolve = 0f;
                }
                else if (FieldManualPlugin.ReloadGuideKey.Value.IsDown())
                {
                    GuideDatabase.Load();
                    Loc.ClearCache();
                    if (mInfo != null) Rebuild();
                }
                else if (FieldManualPlugin.InspectKey.Value.IsDown())
                {
                    LogInspection();
                }
            }
            catch (Exception ex)
            {
                TargetResolver.LogOnce("keys", ex);
            }
        }

        private void Tick(float now)
        {
            StructurePart part = null;
            TargetSource source = TargetSource.None;
            try
            {
                part = mResolver.Resolve(out source);
            }
            catch (Exception ex)
            {
                TargetResolver.LogOnce("resolve", ex);
            }

            if (part != null)
            {
                mLastSeen = now;
                if (part != mTargetPart || mInfo == null)
                {
                    SetTarget(part, source, now);
                    return;
                }
                if (!TargetResolver.IsAlive(mTargetPart))
                {
                    ClearTarget();
                    return;
                }
                if (now >= mNextDynamic)
                {
                    mNextDynamic = now + kDynamicInterval;
                    try
                    {
                        PartInspector.RefreshDynamic(mInfo, mResolver.Ratings);
                        Rebuild();
                    }
                    catch (Exception ex)
                    {
                        TargetResolver.LogOnce("refresh", ex);
                    }
                }
                else if (mInfo.IsBreaker && mView != null)
                {
                    try
                    {
                        if (PartInspector.RefreshBreaker(mInfo))
                        {
                            HazardRules.RefreshLive(mView);
                        }
                    }
                    catch (Exception ex)
                    {
                        TargetResolver.LogOnce("breaker-refresh", ex);
                    }
                }
            }
            else if (mInfo != null && (now - mLastSeen > kTargetGrace || !TargetResolver.IsAlive(mTargetPart)))
            {
                ClearTarget();
            }
        }

        private void SetTarget(StructurePart part, TargetSource source, float now)
        {
            try
            {
                mTargetPart = part;
                mInfo = PartInspector.Inspect(part, mResolver.Ratings);
                mNextDynamic = now + kDynamicInterval;
                Rebuild();
                if (FieldManualPlugin.VerboseLogging.Value)
                {
                    FieldManualPlugin.Log.LogInfo($"Target ({source}): {mInfo.Name} [{mInfo.ObjectAsset} / {mInfo.PartAsset}] guide: {string.Join(", ", mView.GuideIds)}");
                }
                if (FieldManualPlugin.LogUnknownObjects.Value && !mView.HasCuratedEntry)
                {
                    var key = mInfo.ObjectAsset ?? mInfo.PartAsset ?? mInfo.Name;
                    if (!string.IsNullOrEmpty(key) && mLoggedUnknown.Add(key))
                    {
                        FieldManualPlugin.Log.LogInfo($"No curated guide entry for '{mInfo.Name}' (object '{mInfo.ObjectAsset}', part '{mInfo.PartAsset}', hazards {mInfo.Hazards}).");
                    }
                }
            }
            catch (Exception ex)
            {
                TargetResolver.LogOnce("inspect", ex);
                ClearTarget();
            }
        }

        private void Rebuild()
        {
            mView = HazardRules.Build(mInfo, GuideDatabase.Match(mInfo));
        }

        private void ClearTarget()
        {
            mTargetPart = null;
            mInfo = null;
            mView = null;
        }

        private void LogInspection()
        {
            var part = mTargetPart;
            if (part == null)
            {
                part = mResolver.Resolve(out _);
            }
            if (part == null)
            {
                FieldManualPlugin.Log.LogInfo("Inspect: nothing targeted.");
                return;
            }
            var info = part == mTargetPart && mInfo != null ? mInfo : PartInspector.Inspect(part, mResolver.Ratings);
            var hits = GuideDatabase.Match(info);
            var sb = new StringBuilder();
            sb.AppendLine(PartInspector.Describe(info));
            sb.Append("  Guide matches: ");
            if (hits.Count == 0) sb.Append("(none)");
            foreach (var hit in hits) sb.Append($"{hit.Id}[{hit.Score},{hit.Entry.Origin}] ");
            FieldManualPlugin.Log.LogInfo(sb.ToString());
        }

        // ------------------------------------------------------------------ rendering

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !TargetResolver.InGameplay || Time.timeScale <= 0.001f)
            {
                return;
            }
            bool showHint = !mVisible && Time.unscaledTime < mHintUntil;
            if (!mVisible && !showHint)
            {
                return;
            }

            GUI.depth = -900;
            float scale = Screen.height / 1080f * FieldManualPlugin.Scale.Value;
            EnsureStyles(scale);

            if (showHint)
            {
                var text = $"{FieldManualPlugin.ToggleKey.Value}: field manual";
                var size = mHint.CalcSize(new GUIContent(text));
                GUI.Label(new Rect(Screen.width - size.x - 24 * scale, Screen.height * FieldManualPlugin.TopOffset.Value, size.x, size.y), text, mHint);
                return;
            }

            float width = Mathf.Min(FieldManualPlugin.PanelWidth.Value * scale, Screen.width * 0.6f);
            float margin = 20 * scale;
            float top = Screen.height * FieldManualPlugin.TopOffset.Value;
            float maxHeight = Mathf.Max(80 * scale, Screen.height - top - margin);
            if (mLayoutView != mView || mLayoutVersion != (mView?.Version ?? -1) || !Mathf.Approximately(mLayoutScale, scale)
                || !Mathf.Approximately(mLayoutWidth, width) || !Mathf.Approximately(mLayoutMaxHeight, maxHeight))
            {
                BuildLayout(scale, width, maxHeight);
            }

            float height = Mathf.Min(mLayoutHeight, maxHeight);
            bool clipped = mLayoutHeight > maxHeight;
            var panel = new Rect(Screen.width - width - margin, top, width, height);

            var old = GUI.color;
            Fill(panel, new Color(kPanel.r, kPanel.g, kPanel.b, FieldManualPlugin.Opacity.Value));
            var accent = mInfo != null ? HazardRules.ColorOf(mInfo.Destination) : kAccent;
            if (mInfo != null && mInfo.Destination == SalvageDestination.Unknown) accent = kAccent;
            Fill(new Rect(panel.x, panel.y, 4 * scale, panel.height), accent);

            GUI.BeginGroup(panel);
            foreach (var item in mItems)
            {
                if (item.Rect.y > height) break;
                if (item.Kind == DrawKind.Fill)
                {
                    Fill(item.Rect, item.Color);
                }
                else
                {
                    GUI.Label(item.Rect, item.Text, item.Style);
                }
            }
            GUI.EndGroup();

            if (clipped)
            {
                Fill(new Rect(panel.x, panel.yMax - 22 * scale, panel.width, 22 * scale), new Color(kPanel.r, kPanel.g, kPanel.b, 0.95f));
                GUI.Label(new Rect(panel.x, panel.yMax - 22 * scale, panel.width, 22 * scale), "...", mFooter);
            }
            GUI.color = old;
        }

        private void BuildLayout(float scale, float width, float maxHeight)
        {
            mLayoutView = mView;
            mLayoutVersion = mView?.Version ?? -1;
            mLayoutScale = scale;
            mLayoutWidth = width;
            mLayoutMaxHeight = maxHeight;

            // When the panel doesn't fit, drop the least important content first so safety information stays visible.
            for (int compact = 0; compact <= 3; compact++)
            {
                LayoutContent(scale, width, compact);
                if (mLayoutHeight <= maxHeight)
                {
                    break;
                }
            }
        }

        /// <param name="compact">0 = everything; 1 = no description; 2 = also no tips or systems; 3 = also warnings first, no footer.</param>
        private void LayoutContent(float scale, float width, int compact)
        {
            mItems.Clear();

            float pad = 16 * scale;
            float x = pad + 4 * scale;
            float inner = width - x - pad;
            float y = 12 * scale;

            if (mView == null || mInfo == null)
            {
                y = AddText("FIELD MANUAL", mSection, x, y, inner) + 4 * scale;
                y = AddText("Aim at an object to see what it is, what you can do with it and how to take it apart safely.", mEmpty, x, y, inner) + 10 * scale;
                y = AddText(FooterText(), mFooter, x, y, inner) + 10 * scale;
                mLayoutHeight = y;
                return;
            }

            var info = mInfo;
            var view = mView;

            y = AddText(info.Name ?? "Unknown object", mTitle, x, y, inner);
            if (info.Categories.Count > 0)
            {
                y = AddText(string.Join("  ·  ", info.Categories), mSubtitle, x, y, inner);
            }
            var chips = ChipsText(info);
            if (!string.IsNullOrEmpty(chips))
            {
                y = AddText(chips, mChips, x, y + 2 * scale, inner);
            }

            if (view.Badges.Count > 0)
            {
                y += 6 * scale;
                y = AddBadges(view.Badges, x, y, inner, scale);
            }

            if (!string.IsNullOrEmpty(view.Summary))
            {
                y = AddText(view.Summary, mBody, x, y + 8 * scale, inner);
            }

            if (view.RightNow.Count > 0)
            {
                y = AddSection("RIGHT NOW", x, y, inner, scale);
                foreach (var notice in view.RightNow)
                {
                    y = AddNotice(notice, x, y, inner, scale);
                }
            }

            if (compact >= 3)
            {
                y = AddWarnings(view, x, y, inner, scale);
            }

            if (view.Actions.Count > 0)
            {
                y = AddSection("WHAT YOU CAN DO", x, y, inner, scale);
                foreach (var action in view.Actions)
                {
                    y = AddBullet("•", action, mBody, x, y, inner, scale);
                }
            }

            if (view.Dismantle.Count > 0)
            {
                y = AddSection(view.DismantleIsAutomatic ? "DISMANTLE SAFELY (general advice)" : "DISMANTLE SAFELY", x, y, inner, scale);
                for (int i = 0; i < view.Dismantle.Count; i++)
                {
                    y = AddBullet($"{i + 1}.", view.Dismantle[i], mBody, x, y, inner, scale);
                }
            }

            if (compact < 3)
            {
                y = AddWarnings(view, x, y, inner, scale);
            }

            if (compact < 2 && view.Tips.Count > 0)
            {
                y = AddSection("TIPS", x, y, inner, scale);
                foreach (var tip in view.Tips)
                {
                    y = AddBullet("·", tip, mBodyDim, x, y, inner, scale);
                }
            }

            if (compact < 2 && FieldManualPlugin.ShowSystems.Value && info.Systems.Count > 0)
            {
                y = AddSection("SYSTEMS", x, y, inner, scale);
                y = AddText(string.Join(", ", info.Systems), mBodyDim, x, y, inner);
            }

            if (compact < 1 && FieldManualPlugin.ShowDescription.Value && !string.IsNullOrEmpty(info.Description))
            {
                y += 10 * scale;
                Fill(x, y, inner, Mathf.Max(1f, scale), new Color(1f, 1f, 1f, 0.12f));
                y = AddText(info.Description, mDesc, x, y + 8 * scale, inner);
            }

            y += 10 * scale;
            if (compact < 3)
            {
                y = AddText(FooterText(), mFooter, x, y, inner) + 10 * scale;
            }
            mLayoutHeight = y;
        }

        private float AddWarnings(HelpView view, float x, float y, float inner, float scale)
        {
            if (view.Warnings.Count == 0)
            {
                return y;
            }
            y = AddText("WATCH OUT", mSectionWarn, x, y + 12 * scale, inner) + 2 * scale;
            foreach (var warning in view.Warnings)
            {
                y = AddBullet("!", warning, mBody, x, y, inner, scale);
            }
            return y;
        }

        private string ChipsText(PartInfo info)
        {
            var parts = new List<string>();
            var dest = HazardRules.DestinationLabel(info.Destination);
            if (dest != null)
            {
                parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(HazardRules.ColorOf(info.Destination))}><b>{dest.ToUpperInvariant()}</b></color>");
            }
            if (FieldManualPlugin.ShowStats.Value)
            {
                if (!string.IsNullOrEmpty(info.Value)) parts.Add($"{info.Value} cr");
                var mass = HazardRules.FormatMass(info.Mass);
                if (mass != null) parts.Add(mass);
                if (info.IsJoint && info.JointDurability > 0f) parts.Add($"joint {info.JointDurability * 100f:0}%");
            }
            return string.Join("   ·   ", parts);
        }

        private static string FooterText()
        {
            return $"{FieldManualPlugin.ToggleKey.Value}: hide   ·   {FieldManualPlugin.InspectKey.Value}: log details   ·   {FieldManualPlugin.ReloadGuideKey.Value}: reload guide";
        }

        private float AddText(string text, GUIStyle style, float x, float y, float width)
        {
            float h = style.CalcHeight(new GUIContent(text), width);
            mItems.Add(new DrawItem { Kind = DrawKind.Label, Rect = new Rect(x, y, width, h), Text = text, Style = style });
            return y + h;
        }

        private float AddSection(string title, float x, float y, float width, float scale)
        {
            y += 12 * scale;
            return AddText(title, mSection, x, y, width) + 2 * scale;
        }

        private float AddBullet(string bullet, string text, GUIStyle style, float x, float y, float width, float scale)
        {
            float indent = 20 * scale;
            mItems.Add(new DrawItem { Kind = DrawKind.Label, Rect = new Rect(x, y, indent, style.lineHeight + 4 * scale), Text = bullet, Style = mBodyDim });
            return AddText(text, style, x + indent, y, width - indent) + 3 * scale;
        }

        private float AddNotice(Notice notice, float x, float y, float width, float scale)
        {
            float stripe = 3 * scale;
            float indent = 12 * scale;
            float h = mBody.CalcHeight(new GUIContent(notice.Text), width - indent);
            Fill(x, y + 2 * scale, stripe, h - 2 * scale, HazardRules.ColorOf(notice.Severity));
            mItems.Add(new DrawItem { Kind = DrawKind.Label, Rect = new Rect(x + indent, y, width - indent, h), Text = notice.Text, Style = mNoticeStyles[notice.Severity] });
            return y + h + 4 * scale;
        }

        private float AddBadges(List<Badge> badges, float x, float y, float width, float scale)
        {
            float gap = 5 * scale;
            float bx = x;
            float lineHeight = 0f;
            foreach (var badge in badges)
            {
                var size = mBadge.CalcSize(new GUIContent(badge.Text));
                float w = size.x + 12 * scale;
                float h = size.y + 4 * scale;
                if (bx > x && bx + w > x + width)
                {
                    bx = x;
                    y += lineHeight + gap;
                    lineHeight = 0f;
                }
                var color = HazardRules.ColorOf(badge.Severity);
                Fill(bx, y, w, h, new Color(color.r, color.g, color.b, 0.22f));
                Fill(bx, y, 2 * scale, h, color);
                mItems.Add(new DrawItem { Kind = DrawKind.Label, Rect = new Rect(bx, y, w, h), Text = badge.Text, Style = mBadgeStyles[badge.Severity] });
                bx += w + gap;
                lineHeight = Mathf.Max(lineHeight, h);
            }
            return y + lineHeight;
        }

        private void Fill(float x, float y, float w, float h, Color color)
        {
            mItems.Add(new DrawItem { Kind = DrawKind.Fill, Rect = new Rect(x, y, w, h), Color = color });
        }

        private void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, mTexWhite);
            GUI.color = old;
        }

        private void EnsureStyles(float scale)
        {
            if (mStylesReady && Mathf.Approximately(scale, mStyleScale) && mTexWhite != null)
            {
                return;
            }
            mStyleScale = scale;
            if (mTexWhite == null)
            {
                mTexWhite = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                mTexWhite.SetPixel(0, 0, Color.white);
                mTexWhite.Apply();
            }

            int Size(float px) => Mathf.Max(8, Mathf.RoundToInt(px * scale));
            mTitle = Text(Size(22), FontStyle.Bold, kText, true);
            mSubtitle = Text(Size(13), FontStyle.Normal, kDim, true);
            mChips = Text(Size(14), FontStyle.Normal, kText, true);
            mChips.richText = true;
            mSection = Text(Size(12), FontStyle.Bold, kAccent, false);
            mSectionWarn = Text(Size(12), FontStyle.Bold, HazardRules.ColorOf(Severity.Caution), false);
            mBody = Text(Size(14), FontStyle.Normal, kText, true);
            mBodyDim = Text(Size(14), FontStyle.Normal, kDim, true);
            mDesc = Text(Size(13), FontStyle.Italic, kDim, true);
            mBadge = Text(Size(11), FontStyle.Bold, kText, false);
            mBadge.alignment = TextAnchor.MiddleCenter;
            mNoticeStyles.Clear();
            mBadgeStyles.Clear();
            foreach (Severity severity in Enum.GetValues(typeof(Severity)))
            {
                var color = HazardRules.ColorOf(severity);
                var notice = new GUIStyle(mBody);
                notice.normal.textColor = severity == Severity.Danger || severity == Severity.Good ? Color.Lerp(color, Color.white, 0.35f) : kText;
                mNoticeStyles[severity] = notice;
                var badge = new GUIStyle(mBadge);
                badge.normal.textColor = Color.Lerp(color, Color.white, 0.25f);
                mBadgeStyles[severity] = badge;
            }
            mFooter = Text(Size(11), FontStyle.Normal, new Color(kDim.r, kDim.g, kDim.b, 0.8f), true);
            mFooter.alignment = TextAnchor.MiddleCenter;
            mHint = Text(Size(15), FontStyle.Bold, new Color(1f, 0.6f, 0.2f, 0.9f), false);
            mHint.alignment = TextAnchor.MiddleRight;
            mEmpty = Text(Size(14), FontStyle.Normal, kDim, true);
            mStylesReady = true;
            mLayoutVersion = -2;
        }

        private static GUIStyle Text(int size, FontStyle fontStyle, Color color, bool wrap)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = TextAnchor.UpperLeft,
                wordWrap = wrap,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            style.normal.textColor = color;
            return style;
        }
    }
}
