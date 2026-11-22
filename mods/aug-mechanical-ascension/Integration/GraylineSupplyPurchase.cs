using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private sealed record NativeCounterPurchase(StoryRuntimeFlow Flow, GameItem Item,
        IntPtr Inventory, GraylineSupplyState Before, GraylineSupplyState Prepared)
    {
        internal bool Finished;
    }

    private bool CanBuyNativeCounterItems(GameItem[] items)
    {
        try
        {
            if (!GraylineReady || _graylinePurchaseFailed || _graylineState?.VisitState != "arrived" || _graylineRequest == null ||
                _graylineStore!.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineActorId ||
                OfficialClientVisitApi.Status(Owner, _graylineRequest).Status != OfficialPresentationStatus.Arrived)
                return false;
            if (items.Length == 0 || items.Any(x => x == null) ||
                items.Select(x => NativeObject.Handle(x)).Distinct().Count() != items.Length)
                return false;
            long total = 0;
            foreach (var item in items)
            {
                if (!_graylineCounter.TryGetValue(NativeObject.Handle(item), out var offer) ||
                    OfficialInventoryApi.IsPlayerOwned(item) || item.unitCount != 1 || item.parentInventory == null ||
                    !GraylineRegistered(item.identifier)) return false;
                long price = item.GetNegociatedValue();
                if (price is < 0 or > int.MaxValue ||
                    GraylineSupplyRules.PurchaseDenial(_graylineState, _graylineState.AppointmentId!, offer.Operation,
                        item.identifier, 1, (int)price, _graylineStore.playerCash, nativeTrade: true) != null)
                    return false;
                total += price;
            }
            if (total > _graylineStore.playerCash || items.GroupBy(x => x.identifier).Any(group =>
                    _graylineState.Stock.Single(x => x.ItemId == group.Key).Remaining < group.Count()))
                return false;
            return true;
        }
        catch (Exception ex)
        {
            PauseGraylineVisit(ex);
            return false;
        }
    }

    private bool BeginNativeCounterPurchase(PlayerStore store, GameItem? item, out NativeCounterPurchase? purchase)
    {
        purchase = null;
        if (item == null || !_graylineCounter.TryGetValue(NativeObject.Handle(item), out var offer)) return true;
        if (NativeObject.Handle(store) != NativeObject.Handle(_graylineStore) || !CanBuyNativeCounterItems([item])) return false;
        try
        {
            var before = _graylineState!;
            var prepared = GraylineSupplyRules.Prepare(before, offer.Operation, item.identifier, 1,
                checked((int)item.GetNegociatedValue()), StoreStation.GetDayCounter(), store.playerCash,
                [item.uniqueId], nativeTrade: true);
            var attempt = new NativeCounterPurchase(this, item, NativeObject.Handle(item.parentInventory), before, prepared);
            if (!StageGrayline(prepared, purchasing: true)) return false;
            purchase = attempt;
            _graylineBusy = true;
            return true; // native price, OnItemBought, cash debit and item transfer
        }
        catch (Exception ex)
        {
            PauseGraylineVisit(ex);
            return false;
        }
    }

    private void FinishNativeCounterPurchase(NativeCounterPurchase purchase, Exception? error)
    {
        if (purchase.Finished) return;
        purchase.Finished = true;
        try
        {
            var pending = purchase.Prepared.Pending!;
            var item = purchase.Item;
            if (!GraylineSession() || _graylineState != purchase.Prepared)
                throw new InvalidOperationException("Native purchase session changed");
            if (_graylineStore!.playerCash == pending.CashBefore - pending.UnitPrice &&
                item.uniqueId == pending.OutputUids[0] && item.identifier == pending.ItemId && item.unitCount == 1 &&
                item.parentInventory != null && OfficialInventoryApi.IsPlayerOwned(item))
            {
                if (!StageGrayline(GraylineSupplyRules.Commit(purchase.Prepared), purchasing: true))
                    throw new InvalidOperationException("Native purchase receipt commit unconfirmed");
                _graylineMessage = AugText.Get("aug.bought_for_visit_stock_deducted", GraylineName(item.identifier), pending.UnitPrice);
            }
            else if (error == null && _graylineStore.playerCash == pending.CashBefore && !OfficialInventoryApi.IsPlayerOwned(item) &&
                NativeObject.Handle(item.parentInventory) == purchase.Inventory && item.uniqueId == pending.OutputUids[0] &&
                item.identifier == pending.ItemId && item.unitCount == 1)
            {
                // Another native eligibility check may decline the purchase.
                if (!StageGrayline(purchase.Before with { Revision = purchase.Prepared.Revision + 1 }, purchasing: true))
                    throw new InvalidOperationException("Declined native purchase cancellation unconfirmed");
            }
            else throw new InvalidOperationException("Native payment or ownership unconfirmed", error);
        }
        catch (Exception ex)
        {
            if (_graylineState == purchase.Prepared) PauseGraylineVisit(ex);
            else log("[ERROR] [Grayline] Native purchase: " + ex);
        }
        finally { _graylineBusy = false; }
    }

    private void PauseGraylineVisit(Exception error)
    {
        // Native callbacks can update reputation and acquisition history before the debit.
        _graylinePurchaseFailed = true;
        _graylineMessage = AugText.Get("aug.this_grayline_visit_is_paused_dismiss_him");
        log("[ERROR] [Grayline] Native purchase paused this visit: " + error);
    }
}
