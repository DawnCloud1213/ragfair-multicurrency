using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;

namespace RagfairMultiCurrency.Patches
{
    /// <summary>
    /// Makes the "auto select similar items" toggle take effect immediately.
    ///
    /// Vanilla behaviour: AddOfferWindow.AutoSelectSimilarChanged(bool) only stores the flag on
    /// RagfairNewOfferContext and returns. The flag is read later, inside SelectItem, so ticking the
    /// checkbox does nothing until the offer window is reopened.
    ///
    /// This patch:
    ///   1. On tick: replays what the vanilla algorithm would have done, by clearing the selection
    ///      and calling SelectItem again (which reads AutoSelectSimilar and picks the similar items).
    ///      Clearing first is mandatory: SelectItem starts with
    ///      "if (_selectedItems.Contains(item)) { DeselectItem(item); return; }" and the anchor is
    ///      already in the selection, so calling it directly would deselect everything instead.
    ///   2. On untick: shrinks the selection back to the last clicked item. Vanilla never shrinks.
    ///   3. Remembers the last clicked item, so the shrink target is the most recent click (B), not
    ///      the item that was selected when the box was first ticked.
    ///
    /// Everything else (clicking B while ticked, capacity clamping, same-container scoping) is
    /// already handled by the vanilla SelectItem, so nothing here reimplements it.
    /// </summary>
    internal static class AutoSelectSimilarPatch
    {
        private const string Prefix = "[RMC] ";

        // The item most recently selected by the player. Vanilla AddToSelection() resets
        // SelectedItem to _selectedItems.FirstOrDefault(), and HashSet order is not stable, so the
        // anchor has to be tracked here instead of read back from the context.
        private static Item _lastAnchor;
        private static RagfairNewOfferContext _boundContext;

        // ---------------------------------------------------------------- patch 1: the toggle

        private static void TogglePostfix(AddOfferWindow __instance, bool value)
        {
            try
            {
                if (!MultiCurrencyPlugin.InstantAutoSelectSimilar.Value)
                {
                    return;
                }

                RagfairNewOfferContext context = GetOfferContext(__instance);
                if (context == null)
                {
                    return;
                }

                // The window keeps _offerContext across sessions, but a fresh Show() builds a new
                // one. Drop the stale anchor so a leftover item from the previous session cannot be
                // revived by an untick in the new one.
                if (!ReferenceEquals(context, _boundContext))
                {
                    _boundContext = context;
                    _lastAnchor = null;
                }

                if (value)
                {
                    ApplyAutoSelect(context);
                }
                else
                {
                    ShrinkToAnchor(context);
                }
            }
            catch (Exception ex)
            {
                LogError("auto select toggle failed: " + ex);
            }
        }

        private static void ApplyAutoSelect(RagfairNewOfferContext context)
        {
            Item anchor = context.SelectedItem;
            if (anchor == null)
            {
                // Nothing selected yet: there is no anchor to expand from, so stay quiet.
                return;
            }

            _lastAnchor = anchor;

            // Vanilla clears the list itself inside SelectItem's auto-select branch, but the
            // "already selected" early-return fires before that branch is reached, so clear first.
            context.ClearSelectedList();

            // Re-selecting the anchor makes vanilla run its own similar-item scan
            // (same container, Item.Compare, CanBeSelectedAtRagfair, clamped to capacity).
            context.SelectItem(anchor);

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                LogInfo(
                    "auto select applied; item=\"" + Describe(anchor) + "\" selected=" +
                    context.SelectedItems.Count);
            }
        }

        private static void ShrinkToAnchor(RagfairNewOfferContext context)
        {
            Item anchor = _lastAnchor;
            if (anchor == null)
            {
                // No click recorded for this window yet: leave the selection untouched, which is
                // exactly what vanilla does when the box is unticked.
                return;
            }

            if (context.SelectedItems.Count <= 1 && ContainsReference(context.SelectedItems, anchor))
            {
                return;
            }

            // Snapshot first: DeselectItem raises OnItemSelectionChanged, which can mutate
            // the live collection while it is being enumerated.
            List<Item> current = new List<Item>();
            foreach (Item item in context.SelectedItems)
            {
                current.Add(item);
            }

            int removed = 0;
            for (int i = 0; i < current.Count; i++)
            {
                Item item = current[i];
                if (item == null || ReferenceEquals(item, anchor))
                {
                    continue;
                }

                context.DeselectItem(item);
                removed++;
            }

            if (MultiCurrencyPlugin.LogPrices.Value)
            {
                LogInfo(
                    "auto select released; kept=\"" + Describe(anchor) + "\" removed=" + removed +
                    " selected=" + context.SelectedItems.Count);
            }
        }

        // ---------------------------------------------------------------- patch 2: anchor tracking

        private static void SelectItemPostfix(Item selectedItem)
        {
            try
            {
                if (!MultiCurrencyPlugin.InstantAutoSelectSimilar.Value)
                {
                    return;
                }

                if (selectedItem != null)
                {
                    _lastAnchor = selectedItem;
                }
            }
            catch (Exception ex)
            {
                LogError("anchor tracking failed: " + ex);
            }
        }

        // ---------------------------------------------------------------- helpers

        internal static void Reset()
        {
            _lastAnchor = null;
        }

        private static RagfairNewOfferContext GetOfferContext(AddOfferWindow window)
        {
            if (window == null)
            {
                return null;
            }

            FieldInfo field = typeof(AddOfferWindow).GetField(
                "_offerContext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null)
            {
                return null;
            }

            return field.GetValue(window) as RagfairNewOfferContext;
        }

        private static bool ContainsReference(IReadOnlyCollection<Item> items, Item target)
        {
            foreach (Item item in items)
            {
                if (ReferenceEquals(item, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(Item item)
        {
            if (item == null)
            {
                return "<null>";
            }

            string name = item.Name;
            return string.IsNullOrEmpty(name) ? item.StringTemplateId : name;
        }

        private static void LogInfo(string message)
        {
            if (MultiCurrencyPlugin.Log != null)
            {
                MultiCurrencyPlugin.Log.LogInfo(Prefix + message);
            }
        }

        private static void LogError(string message)
        {
            if (MultiCurrencyPlugin.Log != null)
            {
                MultiCurrencyPlugin.Log.LogError(Prefix + message);
            }
        }
    }
}