using System;
using System.Globalization;
using System.Text;
using EFT.UI;
using EFT.UI.Ragfair;
using TMPro;
using UnityEngine;

namespace RagfairMultiCurrency.Patches
{
    /// <summary>
    /// Adds a dedicated block of extra lines (USD x3 + EUR x3) next to the vanilla
    /// market price panel. The vanilla three rouble lines are left completely untouched,
    /// because the panel layout cannot absorb an inline suffix without wrapping into garbage.
    /// </summary>
    internal static class MarketPricesPanelPatch
    {
        private const string BlockName = "RMC_MultiCurrencyBlock";

        private static bool _layoutDumped;
        private static int _dumpedNodes;

        private static void Postfix(ItemMarketPricesPanel __instance, ItemMarketPrices result)
        {
            try
            {
                if (__instance == null || result == null)
                {
                    return;
                }

                if (!MultiCurrencyPlugin.Enabled.Value)
                {
                    return;
                }

                AddOfferWindow window = __instance.GetComponentInParent<AddOfferWindow>();
                bool inOfferWindow = window != null;
                bool shouldDecorate = MultiCurrencyPlugin.Scope.Value == "All" || inOfferWindow;

                float rubMin = result.min;
                float rubAvg = result.avg;
                float rubMax = result.max;

                if (MultiCurrencyPlugin.LogPrices.Value)
                {
                    MultiCurrencyPlugin.Log.LogInfo(
                        "[RMC] hook fired; scope=" + MultiCurrencyPlugin.Scope.Value +
                        " inAddOfferWindow=" + inOfferWindow +
                        " decorate=" + shouldDecorate +
                        " rawRoubles min/avg/max = " +
                        rubMin.ToString("0.##", CultureInfo.InvariantCulture) + " / " +
                        rubAvg.ToString("0.##", CultureInfo.InvariantCulture) + " / " +
                        rubMax.ToString("0.##", CultureInfo.InvariantCulture));
                }

                if (!shouldDecorate)
                {
                    return;
                }

                if (MultiCurrencyPlugin.DumpLayout.Value && !_layoutDumped)
                {
                    _layoutDumped = true;
                    DumpLayout(__instance, window);
                }

                if (window == null)
                {
                    return;
                }

                UpdateBlock(window, __instance, rubMin, rubAvg, rubMax);
            }
            catch (Exception ex)
            {
                // Never let a display bug bubble up into the game.
                if (MultiCurrencyPlugin.Log != null)
                {
                    MultiCurrencyPlugin.Log.LogError("[RMC] " + ex);
                }
            }
        }

        private static void UpdateBlock(
            AddOfferWindow window,
            ItemMarketPricesPanel panel,
            float rubMin,
            float rubAvg,
            float rubMax)
        {
            if (rubMin <= 0f && rubAvg <= 0f && rubMax <= 0f)
            {
                return;
            }

            TextMeshProUGUI text = FindOrCreateBlock(window, panel);
            if (text == null)
            {
                return;
            }

            text.text = BuildText(rubMin, rubAvg, rubMax);
        }

        private static TextMeshProUGUI FindOrCreateBlock(AddOfferWindow window, ItemMarketPricesPanel panel)
        {
            Transform existing = window.transform.Find(BlockName);
            if (existing != null)
            {
                TextMeshProUGUI existingText = existing.GetComponent<TextMeshProUGUI>();
                if (existingText != null)
                {
                    existing.gameObject.SetActive(true);
                    return existingText;
                }
            }

            if (panel._lowestLabel == null || panel._lowestLabel._textMessage == null)
            {
                return null;
            }

            GameObject source = panel._lowestLabel._textMessage.gameObject;
            GameObject clone = UnityEngine.Object.Instantiate<GameObject>(source, window.transform, false);
            clone.name = BlockName;
            clone.SetActive(true);

            RectTransform rect = clone.GetComponent<RectTransform>();
            if (rect != null)
            {
                Vector2 anchor = new Vector2(MultiCurrencyPlugin.AnchorX.Value, MultiCurrencyPlugin.AnchorY.Value);
                rect.anchorMin = anchor;
                rect.anchorMax = anchor;
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(MultiCurrencyPlugin.OffsetX.Value, MultiCurrencyPlugin.OffsetY.Value);
                rect.sizeDelta = new Vector2(MultiCurrencyPlugin.BlockWidth.Value, MultiCurrencyPlugin.BlockHeight.Value);
            }

            TextMeshProUGUI text = clone.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                return null;
            }

            text.fontSize = MultiCurrencyPlugin.FontSize.Value;
            text.lineSpacing = MultiCurrencyPlugin.LineSpacing.Value;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = false;
            text.text = string.Empty;

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                MultiCurrencyPlugin.Log.LogInfo("[RMC] extra price block created under AddOfferWindow.");
            }

            return text;
        }

        private static string BuildText(float rubMin, float rubAvg, float rubMax)
        {
            StringBuilder builder = new StringBuilder();

            if (MultiCurrencyPlugin.ShowUsd.Value && MultiCurrencyPlugin.UsdRate.Value > 0f)
            {
                builder.Append(Line("USD", "\u6700\u4F4E", rubMin / MultiCurrencyPlugin.UsdRate.Value, "$", MultiCurrencyPlugin.UsdColor.Value));
                builder.Append(Line("USD", "\u5E73\u5747", rubAvg / MultiCurrencyPlugin.UsdRate.Value, "$", MultiCurrencyPlugin.UsdColor.Value));
                builder.Append(Line("USD", "\u6700\u9AD8", rubMax / MultiCurrencyPlugin.UsdRate.Value, "$", MultiCurrencyPlugin.UsdColor.Value));
            }

            if (MultiCurrencyPlugin.ShowEur.Value && MultiCurrencyPlugin.EurRate.Value > 0f)
            {
                builder.Append(Line("EUR", "\u6700\u4F4E", rubMin / MultiCurrencyPlugin.EurRate.Value, "\u20AC", MultiCurrencyPlugin.EurColor.Value));
                builder.Append(Line("EUR", "\u5E73\u5747", rubAvg / MultiCurrencyPlugin.EurRate.Value, "\u20AC", MultiCurrencyPlugin.EurColor.Value));
                builder.Append(Line("EUR", "\u6700\u9AD8", rubMax / MultiCurrencyPlugin.EurRate.Value, "\u20AC", MultiCurrencyPlugin.EurColor.Value));
            }

            return builder.ToString();
        }

        private static string Line(string currency, string metric, float value, string symbol, string color)
        {
            string number = symbol + FormatMoney(value, MultiCurrencyPlugin.Decimals.Value);
            string colored = Wrap(number, color, MultiCurrencyPlugin.Colorize.Value);
            return currency + "  " + metric + "  " + colored + "\n";
        }

        private static string Wrap(string value, string color, bool colorize)
        {
            if (!colorize || string.IsNullOrEmpty(color))
            {
                return value;
            }

            return "<color=" + color + ">" + value + "</color>";
        }

        private static string FormatMoney(float value, int decimals)
        {
            double rounded = Math.Round((double)value, decimals, MidpointRounding.AwayFromZero);
            string format = "N" + decimals.ToString(CultureInfo.InvariantCulture);
            return rounded.ToString(format, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- layout dump

        private static void DumpLayout(ItemMarketPricesPanel panel, AddOfferWindow window)
        {
            try
            {
                MultiCurrencyPlugin.Log.LogInfo("[RMC] ===== market price panel layout dump =====");
                MultiCurrencyPlugin.Log.LogInfo("[RMC] panel class: " + panel.GetType().FullName);

                // Parent chain: tells us which container a new block should live in.
                Transform cursor = panel.transform;
                for (int i = 0; i < 6 && cursor != null; i++)
                {
                    DumpSingle(cursor, i);
                    cursor = cursor.parent;
                }

                MultiCurrencyPlugin.Log.LogInfo("[RMC] inAddOfferWindow=" + (window != null));

                _dumpedNodes = 0;

                if (window != null)
                {
                    MultiCurrencyPlugin.Log.LogInfo("[RMC] --- AddOfferWindow subtree ---");
                    DumpTransform(window.transform, 0);
                }
                else if (panel._pricesPanel != null)
                {
                    MultiCurrencyPlugin.Log.LogInfo("[RMC] --- pricesPanel subtree ---");
                    DumpTransform(panel._pricesPanel.transform, 0);
                }

                MultiCurrencyPlugin.Log.LogInfo("[RMC] ===== end layout dump =====");
            }
            catch (Exception ex)
            {
                if (MultiCurrencyPlugin.Log != null)
                {
                    MultiCurrencyPlugin.Log.LogError("[RMC] dump failed: " + ex);
                }
            }
        }

        private static string DescribeRect(Transform transform)
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null)
            {
                return "  (no RectTransform)";
            }

            return "  anchoredPos=" + rect.anchoredPosition.x.ToString("0.##") + "," + rect.anchoredPosition.y.ToString("0.##") +
                   "  sizeDelta=" + rect.sizeDelta.x.ToString("0.##") + "," + rect.sizeDelta.y.ToString("0.##") +
                   "  rect=" + rect.rect.width.ToString("0.##") + "x" + rect.rect.height.ToString("0.##") +
                   "  anchorMin=" + rect.anchorMin.x.ToString("0.##") + "," + rect.anchorMin.y.ToString("0.##") +
                   "  anchorMax=" + rect.anchorMax.x.ToString("0.##") + "," + rect.anchorMax.y.ToString("0.##") +
                   "  pivot=" + rect.pivot.x.ToString("0.##") + "," + rect.pivot.y.ToString("0.##");
        }

        private static string DescribeComponents(Transform transform)
        {
            Component[] components = transform.GetComponents<Component>();
            string componentNames = string.Empty;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                componentNames += components[i].GetType().Name + ",";
            }

            if (componentNames.Length == 0)
            {
                return string.Empty;
            }

            return "  comps=[" + componentNames + "]";
        }

        private static void DumpSingle(Transform transform, int depth)
        {
            if (transform == null)
            {
                return;
            }

            string line = "  [chain" + depth + "] " + transform.name + DescribeRect(transform) + DescribeComponents(transform);

            TextMeshProUGUI tmp = transform.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                line += "  text=\"" + tmp.text + "\"  fontSize=" + tmp.fontSize.ToString("0.##");
            }

            MultiCurrencyPlugin.Log.LogInfo("[RMC] " + line);
        }

        private static void DumpTransform(Transform transform, int depth)
        {
            if (transform == null || depth > 8)
            {
                return;
            }

            _dumpedNodes++;
            if (_dumpedNodes > 500)
            {
                if (_dumpedNodes == 501)
                {
                    MultiCurrencyPlugin.Log.LogInfo("[RMC] ... dump truncated at 500 nodes ...");
                }

                return;
            }

            string indent = new string(' ', depth * 2);
            string line = indent + "> " + transform.name + DescribeRect(transform) + DescribeComponents(transform);

            TextMeshProUGUI tmp = transform.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                line += "  text=\"" + tmp.text + "\"  fontSize=" + tmp.fontSize.ToString("0.##");
            }

            MultiCurrencyPlugin.Log.LogInfo("[RMC] " + line);

            for (int i = 0; i < transform.childCount; i++)
            {
                DumpTransform(transform.GetChild(i), depth + 1);
            }
        }
    }
}
