using System;
using System.Globalization;
using EFT.UI;
using EFT.UI.Ragfair;
using TMPro;
using UnityEngine;

namespace RagfairMultiCurrency.Patches
{
    /// <summary>
    /// Appends USD / EUR values to the vanilla market price lines.
    /// Hooked on the callback that fires after the game fetched min/avg/max for an item.
    /// </summary>
    internal static class MarketPricesPanelPatch
    {
        // Zero-width marker used to detect that a label was already decorated by us.
        // Vanilla re-sets the text on every fetch, so this is mostly a safety net.
        private const string Mark = "\u200B\u200B";

        private static bool _layoutDumped;

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

                bool inOfferWindow = __instance.GetComponentInParent<AddOfferWindow>() != null;
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
                    DumpLayout(__instance);
                }

                if (rubMin <= 0f && rubAvg <= 0f && rubMax <= 0f)
                {
                    return;
                }

                Decorate(__instance._lowestLabel, rubMin);
                Decorate(__instance._averageLabel, rubAvg);
                Decorate(__instance._maximumLabel, rubMax);
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

        private static void Decorate(ClickableUIText label, float roubles)
        {
            if (label == null)
            {
                return;
            }

            TextMeshProUGUI text = label._textMessage;
            if (text == null)
            {
                return;
            }

            if (roubles <= 0f)
            {
                return;
            }

            string current = text.text;
            if (current == null)
            {
                current = string.Empty;
            }

            if (current.IndexOf(Mark, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            string suffix = BuildSuffix(roubles);
            if (suffix.Length == 0)
            {
                return;
            }

            text.text = current + Mark + suffix;
        }

        private static string BuildSuffix(float roubles)
        {
            string suffix = string.Empty;

            if (MultiCurrencyPlugin.ShowUsd.Value && MultiCurrencyPlugin.UsdRate.Value > 0f)
            {
                float value = roubles / MultiCurrencyPlugin.UsdRate.Value;
                string formatted = FormatMoney(value, MultiCurrencyPlugin.Decimals.Value);
                suffix += "   " + Wrap(
                    "$" + formatted,
                    MultiCurrencyPlugin.UsdColor.Value,
                    MultiCurrencyPlugin.Colorize.Value);
            }

            if (MultiCurrencyPlugin.ShowEur.Value && MultiCurrencyPlugin.EurRate.Value > 0f)
            {
                float value = roubles / MultiCurrencyPlugin.EurRate.Value;
                string formatted = FormatMoney(value, MultiCurrencyPlugin.Decimals.Value);
                suffix += "   " + Wrap(
                    "\u20AC" + formatted,
                    MultiCurrencyPlugin.EurColor.Value,
                    MultiCurrencyPlugin.Colorize.Value);
            }

            return suffix;
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

        private static void DumpLayout(ItemMarketPricesPanel panel)
        {
            try
            {
                MultiCurrencyPlugin.Log.LogInfo("[RMC] ===== market price panel layout dump =====");
                MultiCurrencyPlugin.Log.LogInfo("[RMC] panel class: " + panel.GetType().FullName);

                if (panel._pricesPanel != null)
                {
                    DumpTransform(panel._pricesPanel.transform, 0);
                }
                else
                {
                    DumpTransform(panel.transform, 0);
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

        private static void DumpTransform(Transform transform, int depth)
        {
            if (transform == null || depth > 8)
            {
                return;
            }

            string indent = new string(' ', depth * 2);
            string line = indent + "> " + transform.name;

            RectTransform rect = transform as RectTransform;
            if (rect != null)
            {
                line += "  anchoredPos=" + rect.anchoredPosition.x.ToString("0.##") + "," + rect.anchoredPosition.y.ToString("0.##") +
                        "  sizeDelta=" + rect.sizeDelta.x.ToString("0.##") + "," + rect.sizeDelta.y.ToString("0.##") +
                        "  anchorMin=" + rect.anchorMin.x.ToString("0.##") + "," + rect.anchorMin.y.ToString("0.##") +
                        "  anchorMax=" + rect.anchorMax.x.ToString("0.##") + "," + rect.anchorMax.y.ToString("0.##") +
                        "  pivot=" + rect.pivot.x.ToString("0.##") + "," + rect.pivot.y.ToString("0.##");
            }

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

            if (componentNames.Length > 0)
            {
                line += "  comps=[" + componentNames + "]";
            }

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
