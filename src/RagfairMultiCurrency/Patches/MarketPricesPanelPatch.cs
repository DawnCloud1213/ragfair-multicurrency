using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RagfairMultiCurrency.Patches
{
    /// <summary>
    /// Adds a clickable block of extra price rows (USD x3 + EUR x3) under the vanilla market
    /// price panel. The vanilla three rouble rows are left untouched. Clicking one of our rows
    /// fills the matching currency input field through the game's own RequirementView.SetValue.
    /// </summary>
    internal static class MarketPricesPanelPatch
    {
        private const string BlockName = "RMC_MultiCurrencyBlock";
        private const string RowNamePrefix = "RMC_Row_";
        private const int RowCount = 6;

        private static bool _layoutDumped;
        private static int _dumpedNodes;

        private static readonly List<ClickableUIText> Rows = new List<ClickableUIText>();
        private static readonly List<float> RowAmounts = new List<float>();
        private static readonly List<ECurrencyType> RowCurrencies = new List<ECurrencyType>();

        private static AddOfferWindow _boundWindow;

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

            EnsureRows(window, panel);
            if (Rows.Count < RowCount)
            {
                return;
            }

            float usdRate = MultiCurrencyPlugin.UsdRate.Value;
            float eurRate = MultiCurrencyPlugin.EurRate.Value;

            RowAmounts.Clear();
            RowCurrencies.Clear();

            RowAmounts.Add(usdRate > 0f ? rubMin / usdRate : 0f);
            RowAmounts.Add(usdRate > 0f ? rubAvg / usdRate : 0f);
            RowAmounts.Add(usdRate > 0f ? rubMax / usdRate : 0f);
            RowAmounts.Add(eurRate > 0f ? rubMin / eurRate : 0f);
            RowAmounts.Add(eurRate > 0f ? rubAvg / eurRate : 0f);
            RowAmounts.Add(eurRate > 0f ? rubMax / eurRate : 0f);

            RowCurrencies.Add(ECurrencyType.USD);
            RowCurrencies.Add(ECurrencyType.USD);
            RowCurrencies.Add(ECurrencyType.USD);
            RowCurrencies.Add(ECurrencyType.EUR);
            RowCurrencies.Add(ECurrencyType.EUR);
            RowCurrencies.Add(ECurrencyType.EUR);

            bool showUsd = MultiCurrencyPlugin.ShowUsd.Value && usdRate > 0f;
            bool showEur = MultiCurrencyPlugin.ShowEur.Value && eurRate > 0f;

            for (int i = 0; i < RowCount; i++)
            {
                ClickableUIText row = Rows[i];
                if (row == null)
                {
                    continue;
                }

                bool visible = i < 3 ? showUsd : showEur;
                row.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                string text = BuildRowText(i, RowAmounts[i]);
                int captured = i;

                row.SetText(
                    text,
                    new Action<PointerEventData>(delegate(PointerEventData data) { OnRowClicked(captured); }),
                    new Action<string>(delegate(string link) { }));
            }
        }

        private static string BuildRowText(int index, float amount)
        {
            string currency;
            string metric;
            string symbol;
            string color;

            if (index < 3)
            {
                currency = "USD";
                symbol = "$";
                color = MultiCurrencyPlugin.UsdColor.Value;
            }
            else
            {
                currency = "EUR";
                symbol = "\u20AC";
                color = MultiCurrencyPlugin.EurColor.Value;
            }

            int slot = index % 3;
            if (slot == 0)
            {
                metric = "\u6700\u4F4E";
            }
            else if (slot == 1)
            {
                metric = "\u5E73\u5747";
            }
            else
            {
                metric = "\u6700\u9AD8";
            }

            string number = symbol + FormatMoney(amount, MultiCurrencyPlugin.Decimals.Value);
            return currency + "  " + metric + "  " + Wrap(number, color, MultiCurrencyPlugin.Colorize.Value);
        }

        private static void OnRowClicked(int index)
        {
            try
            {
                if (index < 0 || index >= RowAmounts.Count || index >= RowCurrencies.Count)
                {
                    return;
                }

                if (_boundWindow == null)
                {
                    return;
                }

                float amount = RowAmounts[index];
                ECurrencyType currency = RowCurrencies[index];

                if (amount <= 0f)
                {
                    return;
                }

                RequirementView view = FindRequirementView(_boundWindow, currency);
                if (view == null)
                {
                    if (MultiCurrencyPlugin.LogPrices.Value)
                    {
                        MultiCurrencyPlugin.Log.LogWarning("[RMC] no RequirementView for " + currency);
                    }

                    return;
                }

                string input = Math.Round((double)amount, 0, MidpointRounding.AwayFromZero)
                    .ToString("F0", CultureInfo.InvariantCulture);

                view.SetValue(input);

                if (MultiCurrencyPlugin.LogPrices.Value)
                {
                    MultiCurrencyPlugin.Log.LogInfo("[RMC] filled " + currency + " field with " + input);
                }
            }
            catch (Exception ex)
            {
                if (MultiCurrencyPlugin.Log != null)
                {
                    MultiCurrencyPlugin.Log.LogError("[RMC] click failed: " + ex);
                }
            }
        }

        private static RequirementView FindRequirementView(AddOfferWindow window, ECurrencyType currency)
        {
            // _rubRequirementView is private on AddOfferWindow, so it has to go through reflection.
            RequirementView rub = GetFieldValue<RequirementView>(window, "_rubRequirementView");
            if (rub != null && rub._currency == currency)
            {
                return rub;
            }

            RequirementView[] all = GetFieldValue<RequirementView[]>(window, "_requirementViews");
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i]._currency == currency)
                    {
                        return all[i];
                    }
                }
            }

            List<RequirementView> dynamicViews = GetFieldValue<List<RequirementView>>(window, "_dynamicRequirementViews");
            if (dynamicViews != null)
            {
                for (int i = 0; i < dynamicViews.Count; i++)
                {
                    if (dynamicViews[i] != null && dynamicViews[i]._currency == currency)
                    {
                        return dynamicViews[i];
                    }
                }
            }

            return null;
        }

        private static T GetFieldValue<T>(object instance, string fieldName) where T : class
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null)
            {
                return null;
            }

            return field.GetValue(instance) as T;
        }

        // ---------------------------------------------------------------- row creation

        private static void EnsureRows(AddOfferWindow window, ItemMarketPricesPanel panel)
        {
            Transform parent = panel.transform.parent;
            if (parent == null)
            {
                parent = window.transform;
            }

            Transform existing = parent.Find(BlockName);
            if (existing != null && existing.childCount == RowCount)
            {
                Rows.Clear();
                for (int i = 0; i < RowCount; i++)
                {
                    Rows.Add(existing.GetChild(i).GetComponent<ClickableUIText>());
                }

                _boundWindow = window;
                return;
            }

            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            Rows.Clear();
            _boundWindow = window;

            if (panel._lowestLabel == null)
            {
                return;
            }

            RectTransform panelRect = panel.transform as RectTransform;
            float panelWidth = panelRect != null ? panelRect.rect.width : 160f;
            float panelHeight = panelRect != null ? panelRect.rect.height : 57f;

            RectTransform labelRect = panel._lowestLabel.transform as RectTransform;
            float rowHeight = labelRect != null && labelRect.rect.height > 1f
                ? labelRect.rect.height
                : MultiCurrencyPlugin.FontSize.Value * 1.35f;

            float gap = MultiCurrencyPlugin.Gap.Value;
            float nudge = MultiCurrencyPlugin.VerticalNudge.Value;

            GameObject container = new GameObject(BlockName, typeof(RectTransform));
            RectTransform containerRect = container.GetComponent<RectTransform>();
            containerRect.SetParent(parent, false);
            containerRect.anchorMin = new Vector2(1f, 1f);
            containerRect.anchorMax = new Vector2(1f, 1f);
            containerRect.pivot = new Vector2(1f, 1f);
            containerRect.anchoredPosition = new Vector2(0f, -(panelHeight + gap + nudge));
            containerRect.sizeDelta = new Vector2(panelWidth + 80f, rowHeight * RowCount);
            container.transform.SetAsLastSibling();

            for (int i = 0; i < RowCount; i++)
            {
                GameObject rowObject = UnityEngine.Object.Instantiate<GameObject>(
                    panel._lowestLabel.gameObject,
                    containerRect,
                    false);

                rowObject.name = RowNamePrefix + i;
                rowObject.SetActive(true);

                RectTransform rowRect = rowObject.GetComponent<RectTransform>();
                if (rowRect != null)
                {
                    rowRect.anchorMin = new Vector2(1f, 1f);
                    rowRect.anchorMax = new Vector2(1f, 1f);
                    rowRect.pivot = new Vector2(1f, 1f);
                    rowRect.anchoredPosition = new Vector2(0f, -(i * rowHeight));
                    rowRect.sizeDelta = new Vector2(panelWidth + 80f, rowHeight);
                }

                ClickableUIText row = rowObject.GetComponent<ClickableUIText>();
                if (row == null)
                {
                    UnityEngine.Object.Destroy(rowObject);
                    Rows.Add(null);
                    continue;
                }

                if (row._textMessage != null)
                {
                    row._textMessage.fontSize = MultiCurrencyPlugin.FontSize.Value;
                    row._textMessage.alignment = TextAlignmentOptions.TopRight;
                    row._textMessage.enableWordWrapping = false;
                }

                Rows.Add(row);
            }

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                MultiCurrencyPlugin.Log.LogInfo(
                    "[RMC] clickable price block created under \"" + parent.name +
                    "\" rows=" + RowCount +
                    " rowHeight=" + rowHeight.ToString("0.##", CultureInfo.InvariantCulture) +
                    " at y=" + (-(panelHeight + gap + nudge)).ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        // ---------------------------------------------------------------- helpers

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
