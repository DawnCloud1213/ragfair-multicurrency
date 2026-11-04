using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.UI.Ragfair;
using HarmonyLib;

namespace RagfairMultiCurrency
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class MultiCurrencyPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dawncloud.ragfairmulticurrency";
        public const string PluginName = "Ragfair Multi Currency Prices";
        public const string PluginVersion = "0.2.0";

        internal static MultiCurrencyPlugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Scope;
        internal static ConfigEntry<float> UsdRate;
        internal static ConfigEntry<float> EurRate;
        internal static ConfigEntry<bool> ShowUsd;
        internal static ConfigEntry<bool> ShowEur;
        internal static ConfigEntry<int> Decimals;
        internal static ConfigEntry<bool> Colorize;
        internal static ConfigEntry<string> UsdColor;
        internal static ConfigEntry<string> EurColor;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;
        internal static ConfigEntry<float> BlockWidth;
        internal static ConfigEntry<float> BlockHeight;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<float> LineSpacing;
        internal static ConfigEntry<bool> DumpLayout;
        internal static ConfigEntry<bool> LogPrices;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch. When disabled the price panel behaves exactly like vanilla.");

            Scope = Config.Bind("General", "Scope", "AddOfferWindowOnly",
                new ConfigDescription(
                    "AddOfferWindowOnly = only the 'add offer' window. All = every market price panel in the game.",
                    new AcceptableValueList<string>(new string[] { "AddOfferWindowOnly", "All" })));

            UsdRate = Config.Bind("Rates", "RoublesPerUsd", 110f,
                new ConfigDescription(
                    "How many roubles 1 US dollar is worth. Adjust to match the exchange rate on your profile.",
                    new AcceptableValueRange<float>(1f, 5000f)));

            EurRate = Config.Bind("Rates", "RoublesPerEur", 130f,
                new ConfigDescription(
                    "How many roubles 1 euro is worth.",
                    new AcceptableValueRange<float>(1f, 5000f)));

            ShowUsd = Config.Bind("Display", "ShowUsd", true,
                "Append the dollar value to each price line.");

            ShowEur = Config.Bind("Display", "ShowEur", true,
                "Append the euro value to each price line.");

            Decimals = Config.Bind("Display", "Decimals", 0,
                new ConfigDescription(
                    "Decimal places for the converted prices.",
                    new AcceptableValueRange<int>(0, 2)));

            Colorize = Config.Bind("Display", "Colorize", true,
                "Color the converted values so they stand out from the rouble number.");

            UsdColor = Config.Bind("Display", "UsdColor", "#4CD964",
                "TMP color of the dollar value, used when Colorize is on.");

            EurColor = Config.Bind("Display", "EurColor", "#5B8DEF",
                "TMP color of the euro value, used when Colorize is on.");

            AnchorX = Config.Bind("Layout", "AnchorX", 0.02f,
                new ConfigDescription(
                    "Horizontal anchor of the extra price block inside the offer window. 0 = left edge, 1 = right edge.",
                    new AcceptableValueRange<float>(0f, 1f)));

            AnchorY = Config.Bind("Layout", "AnchorY", 0.16f,
                new ConfigDescription(
                    "Vertical anchor of the extra price block inside the offer window. 0 = bottom edge, 1 = top edge.",
                    new AcceptableValueRange<float>(0f, 1f)));

            OffsetX = Config.Bind("Layout", "OffsetX", 24f,
                new ConfigDescription(
                    "Pixel offset applied on top of the anchor, horizontal.",
                    new AcceptableValueRange<float>(-900f, 900f)));

            OffsetY = Config.Bind("Layout", "OffsetY", 0f,
                new ConfigDescription(
                    "Pixel offset applied on top of the anchor, vertical.",
                    new AcceptableValueRange<float>(-900f, 900f)));

            BlockWidth = Config.Bind("Layout", "BlockWidth", 320f,
                new ConfigDescription(
                    "Width of the extra price block in pixels.",
                    new AcceptableValueRange<float>(80f, 900f)));

            BlockHeight = Config.Bind("Layout", "BlockHeight", 200f,
                new ConfigDescription(
                    "Height of the extra price block in pixels.",
                    new AcceptableValueRange<float>(40f, 900f)));

            FontSize = Config.Bind("Layout", "FontSize", 20f,
                new ConfigDescription(
                    "Font size of the extra price block.",
                    new AcceptableValueRange<float>(6f, 60f)));

            LineSpacing = Config.Bind("Layout", "LineSpacing", 1.1f,
                new ConfigDescription(
                    "Line spacing multiplier of the extra price block.",
                    new AcceptableValueRange<float>(0.5f, 3f)));

            DumpLayout = Config.Bind("Debug", "DumpLayout", true,
                "Write the market price panel hierarchy (names, rects, texts) to the BepInEx log once per session. Needed to design the multi-column layout.");

            LogPrices = Config.Bind("Debug", "LogPrices", true,
                "Write every fetched min/avg/max plus the converted values to the BepInEx log.");

            _harmony = new Harmony(PluginGuid);

            MethodInfo target = AccessTools.Method(typeof(ItemMarketPricesPanel), "CG_UpdatePrices");
            if (target == null)
            {
                Logger.LogError("[RMC] ItemMarketPricesPanel.CG_UpdatePrices not found. Patch skipped.");
                return;
            }

            MethodInfo postfix = typeof(Patches.MarketPricesPanelPatch).GetMethod(
                "Postfix",
                BindingFlags.Static | BindingFlags.NonPublic);

            if (postfix == null)
            {
                Logger.LogError("[RMC] MarketPricesPanelPatch.Postfix not found. Patch skipped.");
                return;
            }

            _harmony.Patch(target, null, new HarmonyMethod(postfix));
            Logger.LogInfo("[RMC] Patched " + target.DeclaringType.FullName + "." + target.Name);
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }
    }
}
