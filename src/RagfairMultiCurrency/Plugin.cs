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
        public const string PluginVersion = "0.1.0";

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
