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
    /// Reference prices live in the requirement area, on the LEFT of every offer input field:
    /// the vanilla input is shrunk to the right side and three price cells (lowest / average /
    /// maximum) take the remaining space of the same row. Clicking a cell clears the other
    /// currency fields and fills the matching one through RequirementView.SetValue.
    /// </summary>
    internal static class MarketPricesPanelPatch
    {
        private const string CellPrefix = "RMC_Price_";
        private const int ColCount = 3;
        private const float CellAreaRatio = 0.6f;   // left 60% = prices, right 40% = input field

        private static bool _layoutDumped;
        private static int _dumpedNodes;

        // index = currencyRow * 3 + metric(0=low,1=avg,2=max)
        private static readonly float[] CellAmounts = new float[9];
        private static readonly ECurrencyType[] CellCurrencies = new ECurrencyType[9];

        private static readonly Dictionary<ECurrencyType, ClickableUIText[]> CellsByCurrency =
            new Dictionary<ECurrencyType, ClickableUIText[]>();

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

                HideVanillaPriceRows(__instance);
                HideRequirementTitle(__instance);

                if (MultiCurrencyPlugin.DumpLayout.Value && !_layoutDumped)
                {
                    _layoutDumped = true;
                    DumpLayout(__instance, window);
                }

                if (window == null)
                {
                    return;
                }

                UpdateRowPrices(window, __instance, rubMin, rubAvg, rubMax);
            }
            catch (Exception ex)
            {
                if (MultiCurrencyPlugin.Log != null)
                {
                    MultiCurrencyPlugin.Log.LogError("[RMC] " + ex);
                }
            }
        }

        private static void HideVanillaPriceRows(ItemMarketPricesPanel panel)
        {
            if (!MultiCurrencyPlugin.HideVanillaPrices.Value)
            {
                return;
            }

            if (panel._pricesPanel != null && panel._pricesPanel.activeSelf)
            {
                panel._pricesPanel.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- row prices

        private static void UpdateRowPrices(
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

            _boundWindow = window;

            List<RequirementView> views = GetAllRequirementViews(window);
            if (views.Count == 0)
            {
                return;
            }

            float usdRate = MultiCurrencyPlugin.UsdRate.Value;
            float eurRate = MultiCurrencyPlugin.EurRate.Value;

            ECurrencyType[] order = new ECurrencyType[]
            {
                ECurrencyType.RUB,
                ECurrencyType.USD,
                ECurrencyType.EUR
            };

            for (int row = 0; row < order.Length; row++)
            {
                RequirementView view = FindInList(views, order[row]);
                if (view == null)
                {
                    continue;
                }

                ClickableUIText[] cells = EnsureRowCells(view, order[row]);
                if (cells == null)
                {
                    continue;
                }

                if (row == 0)
                {
                    EnsureHeader(view, panel);
                }

                float[] amounts;
                if (row == 0)
                {
                    amounts = new float[] { rubMin, rubAvg, rubMax };
                }
                else if (row == 1)
                {
                    amounts = new float[]
                    {
                        usdRate > 0f ? rubMin / usdRate : 0f,
                        usdRate > 0f ? rubAvg / usdRate : 0f,
                        usdRate > 0f ? rubMax / usdRate : 0f
                    };
                }
                else
                {
                    amounts = new float[]
                    {
                        eurRate > 0f ? rubMin / eurRate : 0f,
                        eurRate > 0f ? rubAvg / eurRate : 0f,
                        eurRate > 0f ? rubMax / eurRate : 0f
                    };
                }

                string color = row == 0
                    ? "#E6E6E6"
                    : (row == 1 ? MultiCurrencyPlugin.UsdColor.Value : MultiCurrencyPlugin.EurColor.Value);

                for (int metric = 0; metric < ColCount; metric++)
                {
                    int dataIndex = row * ColCount + metric;
                    CellAmounts[dataIndex] = amounts[metric];
                    CellCurrencies[dataIndex] = order[row];

                    string text;
                    if (row == 0)
                    {
                        text = FormatMoney(amounts[metric], 0) + " \u20BD";
                    }
                    else if (row == 1)
                    {
                        text = "$" + FormatMoney(amounts[metric], MultiCurrencyPlugin.Decimals.Value);
                    }
                    else
                    {
                        text = "\u20AC" + FormatMoney(amounts[metric], MultiCurrencyPlugin.Decimals.Value);
                    }

                    ApplyCell(cells[metric], text, color, dataIndex);
                }
            }
        }

        private static void ApplyCell(ClickableUIText cell, string rawText, string color, int dataIndex)
        {
            if (cell == null)
            {
                return;
            }

            string text = MultiCurrencyPlugin.Colorize.Value && !string.IsNullOrEmpty(color)
                ? "<color=" + color + ">" + rawText + "</color>"
                : rawText;

            int captured = dataIndex;
            cell.SetText(
                text,
                new Action<PointerEventData>(delegate(PointerEventData data) { OnCellClicked(captured); }),
                new Action<string>(delegate(string link) { }));
        }

        private static Color32 ParseColor(string hex, Color32 fallback)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return fallback;
            }

            string value = hex.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.Length != 6)
            {
                return fallback;
            }

            try
            {
                byte r = byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new Color32(r, g, b, 255);
            }
            catch
            {
                return fallback;
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

                if (dataIndex < 0 || dataIndex >= CellAmounts.Length)
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

                List<RequirementView> views = GetAllRequirementViews(_boundWindow);
                for (int i = 0; i < views.Count; i++)
                {
                    if (views[i] != null && views[i]._currency != currency)
                    {
                        views[i].SetValue(string.Empty);
                    }
                }

                RequirementView target = FindInList(views, currency);
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

        // ---------------------------------------------------------------- header + vanilla title

        private const string HeaderName = "RMC_Header";

        private static void EnsureHeader(RequirementView rubView, ItemMarketPricesPanel panel)
        {
            if (rubView._inputField == null)
            {
                return;
            }

            Transform parent = rubView._inputField.transform.parent;
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(HeaderName);
            if (existing != null && existing.childCount == 4)
            {
                return;
            }

            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            if (panel._lowestLabel == null)
            {
                return;
            }

            GameObject header = new GameObject(HeaderName, typeof(RectTransform));
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.SetParent(parent, false);
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 0f);
            headerRect.offsetMin = new Vector2(0f, 18f);
            headerRect.offsetMax = new Vector2(0f, 38f);
            headerRect.SetAsLastSibling();

            string[] labels = new string[]
            {
                "\u6700\u4F4E",   // lowest
                "\u5E73\u5747",   // average
                "\u6700\u9AD8",   // maximum
                "\u62A5\u4EF7"    // offer
            };

            float[] widths = new float[] { 0.2f, 0.2f, 0.2f, 0.4f };

            float x = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                GameObject cellObject = UnityEngine.Object.Instantiate<GameObject>(
                    panel._lowestLabel.gameObject,
                    headerRect,
                    false);

                cellObject.name = "RMC_Header_" + i;
                cellObject.SetActive(true);

                RectTransform cellRect = cellObject.GetComponent<RectTransform>();
                if (cellRect != null)
                {
                    cellRect.anchorMin = new Vector2(x, 0f);
                    cellRect.anchorMax = new Vector2(x + widths[i], 1f);
                    cellRect.offsetMin = Vector2.zero;
                    cellRect.offsetMax = Vector2.zero;
                }

                ClickableUIText cell = cellObject.GetComponent<ClickableUIText>();
                if (cell == null || cell._textMessage == null)
                {
                    UnityEngine.Object.Destroy(cellObject);
                    x += widths[i];
                    continue;
                }

                cell._textMessage.fontSize = MultiCurrencyPlugin.FontSize.Value;
                cell._textMessage.alignment = TextAlignmentOptions.Center;
                cell._textMessage.enableWordWrapping = false;
                cell.SetText(
                    "<color=#9AA4B2>" + labels[i] + "</color>",
                    new Action<PointerEventData>(delegate(PointerEventData data) { }),
                    new Action<string>(delegate(string link) { }));

                x += widths[i];
            }
        }

        private static void HideRequirementTitle(ItemMarketPricesPanel panel)
        {
            if (!MultiCurrencyPlugin.HideVanillaPrices.Value)
            {
                return;
            }

            Transform labels = panel.transform.parent;
            Transform preview = labels != null ? labels.parent : null;
            Transform requirementPart = preview != null ? preview.parent : null;
            if (requirementPart == null)
            {
                return;
            }

            TextMeshProUGUI[] texts = requirementPart.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TextMeshProUGUI text = texts[i];
                if (text == null || text.text == null)
                {
                    continue;
                }

                string value = text.text.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                // Hide the vanilla "Requirement" section header on any language build.
                if (value == "\u8981\u6C42" || value.StartsWith("\u8981\u6C42"))
                {
                    if (text.gameObject.activeSelf)
                    {
                        text.gameObject.SetActive(false);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- cell creation

        private static ClickableUIText[] EnsureRowCells(RequirementView view, ECurrencyType currency)
        {
            ClickableUIText[] cached;
            if (CellsByCurrency.TryGetValue(currency, out cached) && cached != null)
            {
                bool alive = true;
                for (int i = 0; i < cached.Length; i++)
                {
                    if (cached[i] == null)
                    {
                        alive = false;
                        break;
                    }
                }

                if (alive)
                {
                    return cached;
                }

                CellsByCurrency.Remove(currency);
            }

            if (view._inputField == null)
            {
                return null;
            }

            Transform inputTransform = view._inputField.transform;
            Transform parent = inputTransform.parent;
            if (parent == null)
            {
                return null;
            }

            // Shrink the vanilla input to the right side of the row.
            RectTransform inputRect = inputTransform as RectTransform;
            if (inputRect != null)
            {
                Vector2 min = inputRect.anchorMin;
                Vector2 max = inputRect.anchorMax;
                float span = max.x - min.x;

                if (span > 0.9f)
                {
                    // Stretched across the row: keep the right share only.
                    inputRect.anchorMin = new Vector2(CellAreaRatio, min.y);
                }
                else if (inputRect.sizeDelta.x > 1f)
                {
                    inputRect.sizeDelta = new Vector2(
                        inputRect.sizeDelta.x * (1f - CellAreaRatio),
                        inputRect.sizeDelta.y);
                }
            }

            RectTransform labelTemplate = null;
            ItemMarketPricesPanel templatePanel = _boundWindow != null
                ? _boundWindow.GetComponentInChildren<ItemMarketPricesPanel>()
                : null;
            if (templatePanel != null && templatePanel._lowestLabel != null)
            {
                labelTemplate = templatePanel._lowestLabel.transform as RectTransform;
            }

            if (labelTemplate == null)
            {
                return null;
            }

            ClickableUIText[] cells = new ClickableUIText[ColCount];
            float cellWidth = CellAreaRatio / ColCount;

            for (int metric = 0; metric < ColCount; metric++)
            {
                GameObject cellObject = UnityEngine.Object.Instantiate<GameObject>(
                    templatePanel._lowestLabel.gameObject,
                    parent,
                    false);

                cellObject.name = CellPrefix + currency + "_" + metric;
                cellObject.SetActive(true);

                RectTransform cellRect = cellObject.GetComponent<RectTransform>();
                if (cellRect != null)
                {
                    cellRect.anchorMin = new Vector2(metric * cellWidth, 0f);
                    cellRect.anchorMax = new Vector2((metric + 1) * cellWidth, 1f);
                    cellRect.offsetMin = Vector2.zero;
                    cellRect.offsetMax = Vector2.zero;
                    cellRect.SetAsLastSibling();

                    // Keep the cell below the input in z-order so the input stays clickable
                    // where they overlap (they should not overlap, but be safe).
                    cellRect.SetSiblingIndex(Math.Max(0, cellRect.GetSiblingIndex()));
                }

                ClickableUIText cell = cellObject.GetComponent<ClickableUIText>();
                if (cell == null)
                {
                    UnityEngine.Object.Destroy(cellObject);
                    cells[metric] = null;
                    continue;
                }

                if (cell._textMessage != null)
                {
                    cell._textMessage.fontSize = MultiCurrencyPlugin.FontSize.Value;
                    cell._textMessage.alignment = TextAlignmentOptions.Center;
                    cell._textMessage.enableWordWrapping = false;
                }

                cells[metric] = cell;
            }

            CellsByCurrency[currency] = cells;

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                MultiCurrencyPlugin.Log.LogInfo(
                    "[RMC] row cells created for " + currency +
                    " under \"" + parent.name + "\"");
            }

            return cells;
        }

        // ---------------------------------------------------------------- requirement views

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

        private static RequirementView FindInList(List<RequirementView> views, ECurrencyType currency)
        {
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

                Transform cursor = panel.transform;
                for (int i = 0; i < 6 && cursor != null; i++)
                {
                    DumpSingle(cursor, i);
                    cursor = cursor.parent;
                }

                DumpRequirementArea(panel);

                if (window != null)
                {
                    _dumpedNodes = 0;
                    MultiCurrencyPlugin.Log.LogInfo("[RMC] --- AddOfferWindow subtree ---");
                    DumpTransform(window.transform, 0);
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

        private static void DumpRequirementArea(ItemMarketPricesPanel panel)
        {
            AddOfferWindow window = panel.GetComponentInParent<AddOfferWindow>();
            if (window == null)
            {
                MultiCurrencyPlugin.Log.LogInfo("[RMC] requirement area: no AddOfferWindow");
                return;
            }

            List<RequirementView> views = GetAllRequirementViews(window);
            MultiCurrencyPlugin.Log.LogInfo("[RMC] --- requirement views: " + views.Count + " ---");

            for (int i = 0; i < views.Count; i++)
            {
                RequirementView view = views[i];
                if (view == null)
                {
                    continue;
                }

                MultiCurrencyPlugin.Log.LogInfo(
                    "[RMC]   view[" + i + "] currency=" + view._currency +
                    " name=" + view.name +
                    DescribeRect(view.transform) +
                    DescribeComponents(view.transform));

                if (view._inputField != null)
                {
                    MultiCurrencyPlugin.Log.LogInfo(
                        "[RMC]     inputField " + view._inputField.name +
                        DescribeRect(view._inputField.transform) +
                        DescribeComponents(view._inputField.transform));

                    Transform parent = view._inputField.transform.parent;
                    if (parent != null)
                    {
                        MultiCurrencyPlugin.Log.LogInfo(
                            "[RMC]     inputParent " + parent.name +
                            DescribeRect(parent) +
                            DescribeComponents(parent) +
                            " kids=" + parent.childCount);

                        for (int k = 0; k < parent.childCount && k < 12; k++)
                        {
                            Transform kid = parent.GetChild(k);
                            MultiCurrencyPlugin.Log.LogInfo(
                                "[RMC]       kid[" + k + "] " + kid.name +
                                DescribeRect(kid) +
                                DescribeComponents(kid));
                        }
                    }
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
            if (transform == null || depth > 6)
            {
                return;
            }

            _dumpedNodes++;
            if (_dumpedNodes > 200)
            {
                return;
            }

            if (transform.name.IndexOf("ragfair_layout", StringComparison.Ordinal) >= 0)
            {
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
