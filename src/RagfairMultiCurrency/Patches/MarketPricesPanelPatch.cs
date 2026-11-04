using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RagfairMultiCurrency.Patches
{
    /// <summary>
    /// Builds a 3x3 reference-price grid inside the offer window "Requirement" area:
    /// row 1 = roubles, row 2 = dollars, row 3 = euros; columns = lowest / average / maximum.
    /// Clicking a cell clears every other currency field and fills the matching one through the
    /// game's own RequirementView.SetValue, which is exactly what the vanilla price click does.
    /// </summary>
    internal static class MarketPricesPanelPatch
    {
        private const string BlockName = "RMC_MultiCurrencyGrid";
        private const int ColCount = 3;
        private const int RowCount = 4; // header + RUB + USD + EUR
        private const float DefaultRowHeight = 19f;

        private static bool _layoutDumped;
        private static int _dumpedNodes;

        private static readonly List<ClickableUIText> Cells = new List<ClickableUIText>();
        private static readonly List<float> CellAmounts = new List<float>();          // 9 data cells
        private static readonly List<ECurrencyType> CellCurrencies = new List<ECurrencyType>();

        private static AddOfferWindow _boundWindow;
        private static float _rowHeight = DefaultRowHeight;

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

                UpdateGrid(window, __instance, rubMin, rubAvg, rubMax);
            }
            catch (Exception ex)
            {
                if (MultiCurrencyPlugin.Log != null)
                {
                    MultiCurrencyPlugin.Log.LogError("[RMC] " + ex);
                }
            }
        }

        private static void UpdateGrid(
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

            EnsureGrid(window, panel);
            if (Cells.Count < ColCount * RowCount)
            {
                return;
            }

            float usdRate = MultiCurrencyPlugin.UsdRate.Value;
            float eurRate = MultiCurrencyPlugin.EurRate.Value;

            CellAmounts.Clear();
            CellCurrencies.Clear();

            // Row 1: roubles (values shown as-is)
            CellAmounts.Add(rubMin);
            CellAmounts.Add(rubAvg);
            CellAmounts.Add(rubMax);
            CellCurrencies.Add(ECurrencyType.RUB);
            CellCurrencies.Add(ECurrencyType.RUB);
            CellCurrencies.Add(ECurrencyType.RUB);

            // Row 2: dollars
            CellAmounts.Add(usdRate > 0f ? rubMin / usdRate : 0f);
            CellAmounts.Add(usdRate > 0f ? rubAvg / usdRate : 0f);
            CellAmounts.Add(usdRate > 0f ? rubMax / usdRate : 0f);
            CellCurrencies.Add(ECurrencyType.USD);
            CellCurrencies.Add(ECurrencyType.USD);
            CellCurrencies.Add(ECurrencyType.USD);

            // Row 3: euros
            CellAmounts.Add(eurRate > 0f ? rubMin / eurRate : 0f);
            CellAmounts.Add(eurRate > 0f ? rubAvg / eurRate : 0f);
            CellAmounts.Add(eurRate > 0f ? rubMax / eurRate : 0f);
            CellCurrencies.Add(ECurrencyType.EUR);
            CellCurrencies.Add(ECurrencyType.EUR);
            CellCurrencies.Add(ECurrencyType.EUR);

            // Header row (not clickable)
            SetCell(0, "\u6700\u4F4E", "#9AA4B2", false, -1);
            SetCell(1, "\u5E73\u5747", "#9AA4B2", false, -1);
            SetCell(2, "\u6700\u9AD8", "#9AA4B2", false, -1);

            for (int dataIndex = 0; dataIndex < 9; dataIndex++)
            {
                int row = dataIndex / ColCount;      // 0=RUB, 1=USD, 2=EUR
                int col = dataIndex % ColCount;

                float amount = CellAmounts[dataIndex];
                string text;

                if (row == 0)
                {
                    text = FormatMoney(amount, 0) + " \u20BD";
                }
                else if (row == 1)
                {
                    text = "$" + FormatMoney(amount, MultiCurrencyPlugin.Decimals.Value);
                }
                else
                {
                    text = "\u20AC" + FormatMoney(amount, MultiCurrencyPlugin.Decimals.Value);
                }

                string color;
                if (row == 0)
                {
                    color = "#E6E6E6";
                }
                else if (row == 1)
                {
                    color = MultiCurrencyPlugin.UsdColor.Value;
                }
                else
                {
                    color = MultiCurrencyPlugin.EurColor.Value;
                }

                SetCell(ColCount + dataIndex, text, color, true, dataIndex);
            }
        }

        private static void SetCell(int cellIndex, string rawText, string color, bool clickable, int dataIndex)
        {
            if (cellIndex < 0 || cellIndex >= Cells.Count)
            {
                return;
            }

            ClickableUIText cell = Cells[cellIndex];
            if (cell == null)
            {
                return;
            }

            string text = MultiCurrencyPlugin.Colorize.Value && !string.IsNullOrEmpty(color)
                ? "<color=" + color + ">" + rawText + "</color>"
                : rawText;

            if (clickable)
            {
                int captured = dataIndex;
                cell.SetText(
                    text,
                    new Action<PointerEventData>(delegate(PointerEventData data) { OnCellClicked(captured); }),
                    new Action<string>(delegate(string link) { }));
            }
            else
            {
                cell.SetText(
                    text,
                    new Action<PointerEventData>(delegate(PointerEventData data) { }),
                    new Action<string>(delegate(string link) { }));
            }
        }

        private static void OnCellClicked(int dataIndex)
        {
            try
            {
                if (_boundWindow == null)
                {
                    return;
                }

                if (dataIndex < 0 || dataIndex >= CellAmounts.Count || dataIndex >= CellCurrencies.Count)
                {
                    return;
                }

                float amount = CellAmounts[dataIndex];
                ECurrencyType currency = CellCurrencies[dataIndex];

                if (amount <= 0f)
                {
                    return;
                }

                string input = Math.Round((double)amount, 0, MidpointRounding.AwayFromZero)
                    .ToString("F0", CultureInfo.InvariantCulture);

                // Clear every other currency first, so exactly one field carries an offer.
                List<RequirementView> views = GetAllRequirementViews(_boundWindow);
                for (int i = 0; i < views.Count; i++)
                {
                    if (views[i] != null && views[i]._currency != currency)
                    {
                        views[i].SetValue(string.Empty);
                    }
                }

                RequirementView target = FindRequirementView(_boundWindow, currency, views);
                if (target == null)
                {
                    if (MultiCurrencyPlugin.LogPrices.Value)
                    {
                        MultiCurrencyPlugin.Log.LogWarning("[RMC] no RequirementView for " + currency);
                    }

                    return;
                }

                target.SetValue(input);

                if (MultiCurrencyPlugin.LogPrices.Value)
                {
                    MultiCurrencyPlugin.Log.LogInfo(
                        "[RMC] cleared other currencies, filled " + currency + " with " + input);
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

        private static List<RequirementView> GetAllRequirementViews(AddOfferWindow window)
        {
            List<RequirementView> views = new List<RequirementView>();

            RequirementView rub = GetFieldValue<RequirementView>(window, "_rubRequirementView");
            if (rub != null)
            {
                views.Add(rub);
            }

            RequirementView[] all = GetFieldValue<RequirementView[]>(window, "_requirementViews");
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && !views.Contains(all[i]))
                    {
                        views.Add(all[i]);
                    }
                }
            }

            List<RequirementView> dynamicViews = GetFieldValue<List<RequirementView>>(window, "_dynamicRequirementViews");
            if (dynamicViews != null)
            {
                for (int i = 0; i < dynamicViews.Count; i++)
                {
                    if (dynamicViews[i] != null && !views.Contains(dynamicViews[i]))
                    {
                        views.Add(dynamicViews[i]);
                    }
                }
            }

            return views;
        }

        private static RequirementView FindRequirementView(
            AddOfferWindow window,
            ECurrencyType currency,
            List<RequirementView> known)
        {
            if (known != null)
            {
                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i] != null && known[i]._currency == currency)
                    {
                        return known[i];
                    }
                }
            }

            List<RequirementView> views = GetAllRequirementViews(window);
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] != null && views[i]._currency == currency)
                {
                    return views[i];
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

        // ---------------------------------------------------------------- grid creation

        private static void EnsureGrid(AddOfferWindow window, ItemMarketPricesPanel panel)
        {
            // Prices -> Labels -> PreviewPanel -> RequirementPart (the "Requirement" block with
            // a VerticalLayoutGroup). Dropping the grid into that group makes the vanilla rows
            // shift down by themselves instead of being covered.
            Transform labels = panel.transform.parent;
            Transform preview = labels != null ? labels.parent : null;
            Transform requirementPart = preview != null ? preview.parent : null;

            if (requirementPart == null)
            {
                requirementPart = window.transform;
            }

            Transform existing = requirementPart.Find(BlockName);
            if (existing != null && existing.childCount == ColCount * RowCount)
            {
                Cells.Clear();
                for (int i = 0; i < existing.childCount; i++)
                {
                    Cells.Add(existing.GetChild(i).GetComponent<ClickableUIText>());
                }

                _boundWindow = window;
                return;
            }

            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            Cells.Clear();
            _boundWindow = window;

            if (panel._lowestLabel == null)
            {
                return;
            }

            RectTransform labelRect = panel._lowestLabel.transform as RectTransform;
            _rowHeight = labelRect != null && labelRect.rect.height > 1f
                ? labelRect.rect.height
                : DefaultRowHeight;

            float gridHeight = _rowHeight * RowCount;

            GameObject grid = new GameObject(BlockName, typeof(RectTransform));
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            gridRect.SetParent(requirementPart, false);

            int previewIndex = preview != null ? preview.GetSiblingIndex() : 0;
            gridRect.SetSiblingIndex(previewIndex + 1);

            gridRect.anchorMin = new Vector2(0f, 1f);
            gridRect.anchorMax = new Vector2(1f, 1f);
            gridRect.pivot = new Vector2(0.5f, 1f);
            gridRect.anchoredPosition = Vector2.zero;
            gridRect.sizeDelta = new Vector2(0f, gridHeight);

            LayoutElement layout = grid.AddComponent<LayoutElement>();
            layout.preferredHeight = gridHeight;
            layout.minHeight = gridHeight;
            layout.flexibleHeight = 0f;

            for (int row = 0; row < RowCount; row++)
            {
                for (int col = 0; col < ColCount; col++)
                {
                    GameObject cellObject = UnityEngine.Object.Instantiate<GameObject>(
                        panel._lowestLabel.gameObject,
                        gridRect,
                        false);

                    cellObject.name = "RMC_Cell_" + row + "_" + col;
                    cellObject.SetActive(true);

                    RectTransform cellRect = cellObject.GetComponent<RectTransform>();
                    if (cellRect != null)
                    {
                        cellRect.anchorMin = new Vector2(
                            (float)col / ColCount,
                            1f - (float)(row + 1) / RowCount);
                        cellRect.anchorMax = new Vector2(
                            (float)(col + 1) / ColCount,
                            1f - (float)row / RowCount);
                        cellRect.offsetMin = Vector2.zero;
                        cellRect.offsetMax = Vector2.zero;
                    }

                    ClickableUIText cell = cellObject.GetComponent<ClickableUIText>();
                    if (cell == null)
                    {
                        UnityEngine.Object.Destroy(cellObject);
                        Cells.Add(null);
                        continue;
                    }

                    if (cell._textMessage != null)
                    {
                        cell._textMessage.fontSize = MultiCurrencyPlugin.FontSize.Value;
                        cell._textMessage.alignment = TextAlignmentOptions.Center;
                        cell._textMessage.enableWordWrapping = false;
                    }

                    Cells.Add(cell);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(requirementPart as RectTransform);

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                MultiCurrencyPlugin.Log.LogInfo(
                    "[RMC] reference grid created under \"" + requirementPart.name +
                    "\" cells=" + Cells.Count +
                    " rowHeight=" + _rowHeight.ToString("0.##", CultureInfo.InvariantCulture));
            }
        }

        // ---------------------------------------------------------------- helpers

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
