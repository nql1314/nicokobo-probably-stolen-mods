using System;
using System.Collections.Generic;

namespace StockReroll
{
    // Originally split for Build 26284217; Build 26317870 metadata rechecked.
    // These native callbacks combine supply with visit or
    // commissary changes. Generate only their stock instead of suppressing
    // shared game methods with Harmony while the callbacks run.
    internal static class StockSupply
    {
        internal static Action GetReplacement(string declaringType, string method, string clientId)
        {
            switch (declaringType + "." + method)
            {
                case "StoreClientList+<>c__DisplayClass20_0.<CreateJunkerSellOnly>b__0":
                    return PlaceJunkerStock;
                case "StoreClientList+<>c__DisplayClass49_0.<CreateScavGeneral>b__0":
                    return StoreClientList.AddScavGeneralStock;
                case "StoreClientList+<>c__DisplayClass51_0.<CreateScavCrate>b__0":
                    return PlaceScavCrateStock;
                case "StoreClientList+<>c.<CreateFoodThief>b__58_0":
                    return PlaceFoodThiefStock;
                case "StoreClientList+<>c__DisplayClass54_0.<CreateInsScav>b__0":
                    return PlaceInspectableStock;
                case "StoreClientList+<>c.<CreateLotteryRep>b__86_2":
                case "StoreClientList+<>c.<CreateLotteryRep>b__86_3":
                    return PlaceLotteryStock;
                // These native callbacks introduce the visitor after supplying
                // goods. Route the registered clients to stock-only helpers.
                case "StoreClientListSupplier+<>c__DisplayClass16_0.<CreateScavGenericSeller>b__0":
                    switch (clientId)
                    {
                        case "scavGenericFoodSeller": return StoreClientListSupplier.AddScavFoodStock;
                        case "scavGenericHouseholdSeller": return StoreClientListSupplier.AddScavHouseholdStock;
                        case "scavGenericMaterialSeller": return StoreClientListSupplier.AddScavMaterialStock;
                        case "scavGenericMedSeller": return StoreClientListSupplier.AddScavMedStock;
                        default: throw new InvalidOperationException("Unknown scavenger stock source: " + clientId);
                    }
                case "StoreClientListSupplier+<>c__DisplayClass27_0.<CreateThiefGenericSeller>b__0":
                    switch (clientId)
                    {
                        case "thiefGenericFoodSeller": return StoreClientListSupplier.AddThiefFoodStock;
                        case "thiefGenericHouseholdSeller": return StoreClientListSupplier.AddThiefHouseholdStock;
                        case "thiefGenericMaterialSeller": return StoreClientListSupplier.AddThiefMaterialStock;
                        case "thiefGenericMedSeller": return StoreClientListSupplier.AddThiefMedStock;
                        case "thiefGenericLuxurySeller": return StoreClientListSupplier.AddThiefLuxuryStock;
                        default: throw new InvalidOperationException("Unknown thief stock source: " + clientId);
                    }
                default:
                    return null;
            }
        }

        static void PlaceInspectableStock()
        {
            PlayerStore store = StoreAccess.Current;
            var factories = new List<Func<GameItem>>();
            if (store.isStampIntroduced) factories.Add(PreBuiltItemHelper.CreateRandomStamp);
            if (store.isVisualInspectionIntroduced) factories.Add(PreBuiltItemHelper.RandomCigarette);
            if (store.isInjectorIntroduced) factories.Add(PreBuiltItemHelper.CreateRandomInjector);
            Func<GameItem> createItem = factories[RNG.GetRandomIntExculsive(0, factories.Count)];
            store.AddDirectSellingItemToTable(createItem());
            if (StoreStation.Instance.dayCounter >= 15) store.AddDirectSellingItemToTable(createItem());
            if (StoreStation.Instance.dayCounter >= 40) store.AddDirectSellingItemToTable(createItem());
            if (StoreStation.Instance.dayCounter >= 75) store.AddDirectSellingItemToTable(createItem());
            if (EmporiumEntry.Instance.IsFrontCounterEmptyNonOwned())
                for (int i = 0; i < 5; i++) store.AddDirectSellingItemToTable("nutrifruit");
        }

        static void PlaceLotteryStock()
        {
            // The first visit also grants a leaflet, and later visits increment
            // lotteryMetTime. Neither belongs to a stock reroll.
            PlayerStore store = StoreAccess.Current;
            for (int i = 0; i < 10; i++) store.AddDirectSellingItemToTable("lottery_ticket");
            for (int i = 0; i < 5; i++) store.AddDirectSellingItemToTable("scratcher");
        }

        static void PlaceJunkerStock()
        {
            PlayerStore store = StoreAccess.Current;
            store.AddDirectSellingItemToTable("scrap_metal");
            store.AddDirectSellingItemToTable("scrap_metal");
            store.AddDirectSellingItemToTable("scrap_metal");
            store.AddDirectSellingItemToTable("common_electronic");
            store.AddDirectSellingItemToTable("wire");
            store.AddDirectSellingItemToTable("wire");
        }

        static void PlaceScavCrateStock()
        {
            PlayerStore store = StoreAccess.Current;
            string itemId = TableMaster.Roll("packedFoodTable");
            // The native >=20 branch precedes its >=40/>=60 branches, so the
            // later thresholds never run. Keep the current game's quantities.
            int min = StoreStation.Instance.dayCounter >= 20 ? 6 : 5;
            int max = min + 1;
            // Vanilla draws a new limit for each condition, not once per batch.
            for (int i = 0; i < RNG.GetRandomInt(min, max); i++)
                store.AddDirectSellingItemToTable(ItemSpawner.Spawn(itemId));

            var items = EmporiumEntry.Instance.frontInvinvElement.childItems;
            if (items.Count != 0)
            {
                long baseValue = items[0].GetBaseValue();
                if (baseValue <= 10)
                    store.AddDirectSellingItemToTable(ItemSpawner.Spawn(itemId));
                if (baseValue <= 16)
                    store.AddDirectSellingItemToTable(ItemSpawner.Spawn(itemId));
            }
        }

        static void PlaceFoodThiefStock()
        {
            PlayerStore store = StoreAccess.Current;
            float multiplier = StoreClientList.GetBMRepStockMult();
            void Roll(string itemId, float chance)
            {
                if (RNG.Roll(chance * multiplier))
                    store.AddDirectSellingItemToTable(itemId,
                        isOwend: false, isStolen: true, ignorePerk: false, forcedHeat: 25);
            }

            Roll("galaxy_blend", 15f);
            Roll("galaxy_blend", 10f);
            Roll("galaxy_blend", 8f);
            Roll("processed_milk", 8f);
            Roll("processed_milk", 8f);
            Roll("processed_milk", 12f);
            Roll("processed_milk", 10f);
            Roll("processed_meat", 10f);
            Roll("processed_meat", 10f);
            Roll("processed_meat", 10f);
            Roll("processed_meat", 10f);
            Roll("nutrifruit", 10f);
            Roll("nutrifruit", 10f);
            Roll("nutrifruit", 10f);
            Roll("nutrifruit", 10f);
            Roll("nutrifruit", 20f);
            Roll("processed_juice", 10f);
            Roll("processed_juice", 5f);
            Roll("processed_juice", 5f);
            Roll("processed_juice", 5f);
            store.AddDirectSellingItemToTable("nutrifruit",
                isOwend: false, isStolen: true, ignorePerk: false, forcedHeat: 25);
        }
    }
}
