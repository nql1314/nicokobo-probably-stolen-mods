using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Official;
internal static class OfficialScenario
{
    internal static bool IsCurrent => PlayerStore.HasInstance
        ? PlayerStore.Instance.startScenarioId==StartDefinition.Id
        : NewGameData.Instance!=null&&NewGameData.Instance.startScenarioId==StartDefinition.Id;
}
