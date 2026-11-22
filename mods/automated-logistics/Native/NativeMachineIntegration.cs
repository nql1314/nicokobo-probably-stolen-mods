using HarmonyLib;
using Nicokobo.ContentInterop;
using Nicokobo.AutomatedLogistics.Integration;
using System.Reflection;

namespace Nicokobo.AutomatedLogistics.Native;

internal enum NativeMachineAutomationPhase { BeforeBatch, AfterBatch }
internal static class NativeMachineAutomationApi
{
    internal static IDisposable Subscribe(string owner, string key, NativeMachineAutomationPhase phase, Action<NativeMachineAutomationContext> callback)
    {
        ProductionHooks.Subscribe(owner, key,
            phase == NativeMachineAutomationPhase.BeforeBatch ? work =>
            {
                if (!MachineAccess.TryInventory(work.Machine, out var inventory) || inventory == null) return;
                var context = new NativeMachineAutomationContext(work.Machine, inventory); callback(context);
                if (context.Blocked) throw new InvalidOperationException("Logistics preflight rejected this batch");
            } : null,
            phase == NativeMachineAutomationPhase.AfterBatch ? result =>
            {
                if (!MachineAccess.TryInventory(result.Context.Machine, out var inventory) || inventory == null) return;
                callback(new NativeMachineAutomationContext(result.Context.Machine, inventory) { Result = result.Committed ? "produced:committed" : "failed", Work = result });
            } : null);
        // ContentRuntime.Detach removes all owner-scoped production subscriptions.
        return new CallbackLease(() => { });
    }
}
