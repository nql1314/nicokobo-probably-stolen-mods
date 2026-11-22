using System;
using System.Collections.Generic;

namespace StockReroll
{
    // Native supply callbacks established on Build 26284217; signatures and stock
    // calls rechecked against full-release Build 26317870. Do not
    // approve a whole client family: its other actions can spend cash, leave,
    // change visit counters, grant rewards or advance a tutorial.
    internal static class StockCallbackPolicy
    {
        static readonly HashSet<string> SupplyCallbacks = CreateCallbacks();

        internal static bool IsSupply(string declaringType, string method)
        {
            return declaringType != null && method != null
                && SupplyCallbacks.Contains(declaringType + "." + method);
        }

        static HashSet<string> CreateCallbacks()
        {
            var callbacks = new HashSet<string>(StringComparer.Ordinal);
            Add(callbacks, "StoreClientList+<>c",
                "<CreateJunker>b__19_0", "<CreateFarmerClientStandard>b__21_0",
                "<CreateStampSeller>b__22_0", "<CreateInventor>b__23_0",
                "<CreateInventorStorage>b__39_0", "<CreateShadyPharmacist>b__42_0",
                "<CreateScrapper>b__43_0", "<CreateMiner>b__46_0",
                "<CreateThief>b__56_0", "<CreatePettyThief>b__57_0",
                "<CreateFoodThief>b__58_0", "<CreateSupplier>b__61_0",
                "<CreateSupplierIntro>b__62_0", "<CreateWaterSeller>b__63_0",
                "<CreateScavBlood>b__68_0", "<CreateGunsmith>b__73_0",
                "<CreateLowerLevelRareMerchant>b__74_0", "<CreateRareLowerLevelChemist>b__75_0",
                "<CreateSecSurplusMerchant>b__76_0", "<CreateBrokeUpperLevel>b__79_0",
                "<CreateLotteryRep>b__86_2", "<CreateLotteryRep>b__86_3");
            Add(callbacks, "StoreClientList+<>c__DisplayClass20_0", "<CreateJunkerSellOnly>b__0");
            Add(callbacks, "StoreClientList+<>c__DisplayClass45_0", "<CreateLowerLevelChemist>b__0");
            Add(callbacks, "StoreClientList+<>c__DisplayClass49_0", "<CreateScavGeneral>b__0");
            Add(callbacks, "StoreClientList+<>c__DisplayClass51_0", "<CreateScavCrate>b__0");
            Add(callbacks, "StoreClientList+<>c__DisplayClass54_0", "<CreateInsScav>b__0");
            Add(callbacks, "StoreClientListAppraisal+<>c", "<CreateAppraisal1>b__0_0", "<CreateAppraisalRandom>b__3_0");
            Add(callbacks, "StoreClientListBarter+<>c", "<CreateAddictBlood>b__3_0", "<CreateScavBarter>b__4_0");
            Add(callbacks, "StoreClientListBarter+<>c__DisplayClass6_0", "<CreateLowerLevelChemistBarter>b__0");
            Add(callbacks, "StoreClientListCartel+<>c", "<CreateShadyFarmer>b__20_0");
            Add(callbacks, "StoreClientListEvent+<>c",
                "<CreateIceMiner>b__2_0", "<CreateScavengerHouseholdClient>b__8_0",
                "<CreatePanicBuyerClient>b__11_0", "<CreateCorporateBuyerClient>b__13_0",
                "<CreateGenericWaterBuyer>b__14_0", "<CreateFarmerSeedClient>b__21_0",
                "<CreateClinicVolunteerClient>b__27_0");
            Add(callbacks, "StoreClientListGun+<>c",
                "<CreateLLGunSeller>b__28_0", "<CreateMercGunSeller>b__29_0", "<CreateULGunSeller>b__30_0");
            Add(callbacks, "StoreClientListMinor+<>c",
                "<CreateNurse1>b__5_0", "<CreateScavWater>b__7_0", "<CreateScavHaul>b__8_0",
                "<CreateSalvagePilot>b__14_0", "<CreateOldScav>b__17_0", "<CreateFarmerMinor>b__19_0",
                "<CreatePeatClient>b__21_0", "<CreateConspiracyClient>b__22_0", "<CreateRadioactiveWasteCustomer>b__24_0");
            Add(callbacks, "StoreClientListRev+<>c", "<CreateRevRaider>b__0_0", "<CreateRevQuartermaster>b__1_0");
            Add(callbacks, "StoreClientListSpec+<>c",
                "<CreateRetiredGunsmith>b__2_0", "<CreateRetiredFarmer>b__3_0", "<CreateRetiredRancher>b__7_0",
                "<CreateRetiredWaterMerchant>b__8_0", "<CreateRetiredWinemaker>b__9_0",
                "<CreateRetiredJunker>b__10_0", "<CreateRetiredChemist>b__12_0",
                "<CreateRetiredAquafarmer>b__6_0", "<CreateScalper>b__15_1");
            Add(callbacks, "StoreClientListSurplus+<>c",
                "<CreateFoodSurplusClient>b__0_0", "<CreateFoodSurplusClient>b__0_1",
                "<CreateMedicalSurplusClient>b__1_0", "<CreateMedicalSurplusClient>b__1_1",
                "<CreateSubstanceSurplusClient>b__2_0", "<CreateSubstanceSurplusClient>b__2_1",
                "<CreateWeaponArmorSurplusClient>b__3_0", "<CreateWeaponArmorSurplusClient>b__3_1",
                "<CreateMaterialSurplusClient>b__4_0", "<CreateMaterialSurplusClient>b__4_1");
            Add(callbacks, "StoreClientListSupplier+<>c",
                "<CreateCourrierBeer>b__0_0", "<CreateCourrierMining>b__2_0",
                "<CreateCourrierChem>b__4_0", "<CreateCourrierPharma>b__6_0",
                "<CreateHypertechSmuggler>b__32_0");
            Add(callbacks, "StoreClientListSupplier+<>c__DisplayClass16_0", "<CreateScavGenericSeller>b__0");
            Add(callbacks, "StoreClientListSupplier+<>c__DisplayClass27_0", "<CreateThiefGenericSeller>b__0");
            Add(callbacks, "StoreClientListTier+<>c",
                "<CreateWaterScavenger>b__10_0", "<CreateThirstySpacer>b__11_0", "<CreateHydrationSeeker>b__12_0",
                "<CreateWaterShopper>b__13_0", "<CreateWaterStockPiler>b__14_0");
            Add(callbacks, "StoreClientListWanted+<>c",
                "<CreateWanted1>b__2_1", "<CreateWanted2>b__7_1", "<CreateWanted6>b__31_0", "<CreateWanted7>b__41_2",
                "<CreateWanted1Return>b__5_0", "<CreateWanted2Return>b__9_0");
            Add(callbacks, "StoreClientListWater+<>c",
                "<CreateWaterCompany>b__1_0", "<CreateWaterSellScav>b__2_0", "<CreateDishonestWaterSellScav>b__3_0",
                "<SetHonestWaterSellerDialog>b__5_0", "<SetHonestWaterSellerDialog>b__5_1");
            Add(callbacks, "StoreClientUniqueList+<>c",
                "<CreateGeorgioIntro>b__0_0", "<CreateGeorgioIntro2>b__1_0", "<CreateGeorgio2>b__4_0",
                "<CreateGeorgio3>b__5_0", "<CreateGeorgio4>b__6_0", "<UniqueEvidenceSeller>b__40_0",
                "<CreateLunCriminel>b__43_0", "<CreateOfficerLunSurplus>b__45_0",
                "<Eleanor3>b__50_0", "<Eleanor5>b__53_0", "<LandlordWholesale>b__88_0",
                "<CreateGeorgioLuckSale>b__8_0", "<Nellis3>b__86_0", "<GrandpaCheng3>b__92_0");
            return callbacks;
        }

        static void Add(HashSet<string> callbacks, string type, params string[] methods)
        {
            foreach (string method in methods)
                callbacks.Add(type + "." + method);
        }
    }
}
