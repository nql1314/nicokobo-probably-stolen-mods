using System;
using System.Collections.Generic;
using HarmonyLib;

namespace StockReroll
{
    internal static class StockReplay
    {
        static StoreClientInstance observedCustomer;
        static readonly HashSet<Dialogue> startedDialogues = new HashSet<Dialogue>();

        internal static void Reset()
        {
            observedCustomer = null;
            startedDialogues.Clear();
        }

        static void ObserveCustomer()
        {
            StoreClientInstance customer = StoreAccess.Current?.currentClientInstance;
            if (!ReferenceEquals(customer, observedCustomer))
            {
                observedCustomer = customer;
                startedDialogues.Clear();
            }
        }

        internal static void RecordCurrentDialogue(DialogUIManager ui)
        {
            ObserveCustomer();
            if (observedCustomer != null && ui.currentDialog != null)
                startedDialogues.Add(ui.currentDialog);
        }

        internal static bool TryGetActions(StoreClient client, bool allowModCallbacks, out List<Action> actions)
        {
            ObserveCustomer();
            actions = new List<Action>();
            var seen = new HashSet<Action>();
            if (!AddStockActions(client.identifier, client.OnArrival, allowModCallbacks, actions, seen))
                return false;

            // Start actions have no native execution flag. Observe successful
            // dialogue starts/advances, so later lines cannot stock goods early.
            var visited = new HashSet<Dialogue>();
            for (Dialogue dialogue = client.mainDialogue; dialogue != null && visited.Add(dialogue);
                dialogue = dialogue.nextDialogue)
            {
                if (startedDialogues.Contains(dialogue) || dialogue.isEndActionExecuted)
                    if (!AddStockActions(client.identifier, dialogue.startAction, allowModCallbacks, actions, seen))
                        return false;
                if (dialogue.isEndActionExecuted)
                    if (!AddStockActions(client.identifier, dialogue.endAction, allowModCallbacks, actions, seen))
                        return false;
            }
            return true;
        }

        static bool AddStockActions(string clientId, Action action, bool allowModCallbacks, List<Action> actions, HashSet<Action> seen)
        {
            if (action == null)
                return true;
            foreach (Action callback in action.GetInvocationList())
            {
                var method = callback.Method;
                // Unknown callbacks may have supplied some of the existing
                // goods, including vanilla items with no Mod source tag.
                // Keep the default refusal unless the player accepts replaying
                // their side effects, which stock rollback cannot undo.
                if (method.DeclaringType?.Assembly != typeof(StoreClientList).Assembly)
                {
                    if (!allowModCallbacks) return false;
                    if (seen.Add(callback)) actions.Add(callback);
                    continue;
                }
                if (StockCallbackPolicy.IsSupply(method.DeclaringType.FullName, method.Name)
                    && seen.Add(callback))
                    actions.Add(StockSupply.GetReplacement(method.DeclaringType.FullName, method.Name, clientId) ?? callback);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.StartDialogue))]
    internal static class DialogueStartedPatch
    {
        static void Postfix(DialogUIManager __instance, bool __result)
        {
            if (__result) StockReplay.RecordCurrentDialogue(__instance);
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.ContinueConversation))]
    internal static class DialogueAdvancedPatch
    {
        static void Postfix(DialogUIManager __instance) => StockReplay.RecordCurrentDialogue(__instance);
    }

}
