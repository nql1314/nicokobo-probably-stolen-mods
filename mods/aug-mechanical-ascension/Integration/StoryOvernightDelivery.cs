using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private void DeliverOvernightUpdates()
    {
        if (!Ready || _busy || _state == null || _state.Pending != null) return;
        var counter = EmporiumEntry.Instance?.backInvinvElement;
        if (counter == null) return;
        int day = StoreStation.GetDayCounter();
        var updates = _state.Items.Where(x => !x.Removed).Select(receipt =>
            (Receipt: receipt, Day: StoryEvidence.OvernightUpdateDay(receipt.Key, _state, day)))
            .Where(x => x.Day >= 0 && x.Receipt.CounterNoticeDay < x.Day).ToArray();
        if (updates.Length == 0) return;
        var owned = Owned();
        foreach (var (receipt, updateDay) in updates)
        {
            // Move only the recorded original; never manufacture or substitute a copy.
            var item = owned.FirstOrDefault(x => x.uniqueId == receipt.Uid &&
                x.identifier == StoryEvidence.Prefix + receipt.Key && x.unitCount == 1);
            if (item == null) continue;
            _busy = true;
            try
            {
                StoryPhysicalItems.Write(item, _state);
                if (NativeObject.Handle(item.parentInventory) != NativeObject.Handle(counter))
                {
                    var moved = OfficialStoryTransfer.Move(item, counter, 1);
                    if (moved.Indeterminate) { Block("Story counter delivery unconfirmed: " + moved.Status); return; }
                    if (moved.Moved != 1) continue; // Keep the original and retry when space is available.
                }
                if (NativeObject.Handle(item.parentInventory) != NativeObject.Handle(counter) || item.unitCount != 1)
                { Block("Story counter delivery identity mismatch"); return; }
                if (!Stage(_state with
                {
                    Items = _state.Items.Select(x => x.Key == receipt.Key ? x with { CounterNoticeDay = updateDay } : x).ToArray(),
                    Revision = _state.Revision + 1
                })) return;
                _message = AugText.Get("aug.updated_story_item_placed_on_the_counter") + item.name;
            }
            finally { _busy = false; }
        }
    }
}
